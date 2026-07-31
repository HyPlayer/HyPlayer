using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using static HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices.IPlaybackSpeedChangeable;

namespace HyPlayer.Platform.Playback.AudioServices;

public sealed class ChopinAudioService :
    AudioServiceBase,
    IPlayAudioTicketService,
    IPauseAudioTicketService,
    IStopAudioTicketService,
    IAudioTicketSeekableService,
    IOutgoingVolumeChangeable,
    IAudioTicketVolumeChangeable,
    IPlaybackRateChangeableService,
    IPreparedAudioTicketService,
    IAudioTicketListProvidable
{
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly IPlayer _player;
    private readonly PlaybackSettings _setting;
    private readonly List<ChopinAudioTicket> _tickets = [];
    private readonly object _ticketSyncRoot = new();

    public ChopinAudioService(IPlayer player, PlaybackSettings setting)
    {
        _player = player;
        _setting = setting;
    }

    public override string Id => "hyplayer.chopin";

    public override string Name => "HyPlayer Chopin AudioService";

    public Task<List<AudioTicketBase>> GetAudioTicketListAsync(CancellationToken ctk = default)
    {
        return GetCreatedAudioTicketsAsync(ctk);
    }

    public Task SeekAudioTicketAsync(AudioTicketBase audioTicket, double position, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (audioTicket is ChopinAudioTicket chopinTicket)
            _player.SeekPlaybackSource(TimeSpan.FromMilliseconds(position), chopinTicket.PlaybackSource);

        return Task.CompletedTask;
    }

    public Task ChangeVolumeAsync(AudioTicketBase ticket, double volume, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.SetPlaybackSourceOutputVolume(volume, chopinTicket.PlaybackSource);
            chopinTicket.Volume = volume;
        }

        return Task.CompletedTask;
    }

    public Task ChangeOutgoingVolumeAsync(double volume, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        _player.SetOutputVolume(volume);
        return Task.CompletedTask;
    }

    public Task PauseAudioTicketAsync(AudioTicketBase ticket, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.PausePlaybackSource(chopinTicket.PlaybackSource);
            if (ReferenceEquals(_player.PrimaryPlaybackSource, chopinTicket.PlaybackSource))
                _player.PauseAll();
            chopinTicket.Status = AudioTicketStatus.Paused;
        }

        return Task.CompletedTask;
    }

    public Task PlayAudioTicketAsync(AudioTicketBase ticket, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.PlayPlaybackSource(chopinTicket.PlaybackSource);
            _player.PlayAll();
            chopinTicket.Status = AudioTicketStatus.Playing;
        }

        return Task.CompletedTask;
    }

    public Task ChangePlaybackSpeedAsync(AudioTicketBase ticket, double playbackSpeed, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
            _player.SetPlaybackSourceSpeed(playbackSpeed, chopinTicket.PlaybackSource);

        return Task.CompletedTask;
    }

    public async Task<AudioTicketBase> GetPreparedAudioTicketAsync(
        MusicResourceBase musicResource,
        CancellationToken ctk = default)
    {
        return await CreateAudioTicketAsync(
            musicResource,
            false,
            ctk: ctk).ConfigureAwait(false);
    }

    public Task SetPrimaryAudioTicketAsync(AudioTicketBase ticket, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is not ChopinAudioTicket chopinTicket)
            throw new ArgumentException("Ticket does not belong to Chopin.", nameof(ticket));

        _player.SetPrimaryPlaybackSource(chopinTicket.PlaybackSource);
        return Task.CompletedTask;
    }

    public Task StopTicketAsync(AudioTicketBase ticket, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.PausePlaybackSource(chopinTicket.PlaybackSource);
            chopinTicket.Status = AudioTicketStatus.Stopped;
        }

        return Task.CompletedTask;
    }

    public override async Task<AudioTicketBase> GetAudioTicketAsync(MusicResourceBase musicResource,
        CancellationToken ctk = default)
    {
        return await CreateAudioTicketAsync(musicResource, true, ctk: ctk);
    }

    public async Task<ChopinAudioTicket> CreateAudioTicketAsync(
        MusicResourceBase musicResource,
        bool setAsPrimarySource,
        double? initialVolume = null,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        await EnsurePlayerInitializedAsync(ctk).ConfigureAwait(false);
        if (musicResource is not IChopinPlaybackSourceResource && musicResource.Uri is null)
            throw new ArgumentException("Music resource must have a Uri.", nameof(musicResource));

        AudioGraphPlaybackSource? source = null;
        try
        {
            var targetVolume = initialVolume ?? 1d;
            if (musicResource is IChopinPlaybackSourceResource chopinResource)
            {
                source = await chopinResource.CreatePlaybackSourceAsync(ctk);
                if (source is null)
                    throw new ArgumentException("Music resource did not create a playback source.",
                        nameof(musicResource));

                targetVolume = initialVolume ?? chopinResource.SuggestedVolume ?? 1d;
            }
            else
            {
                source = new AudioGraphPlaybackSource(musicResource.Uri);
                await source.CreatePlaybackSource();
            }

            ctk.ThrowIfCancellationRequested();

            await _player.ConnectPlaybackSourceAsync(source, new PlaybackOptions
            {
                AutoPlay = false,
                SetAsPrimarySource = setAsPrimarySource,
                Volume = targetVolume
            });

            var ticket = new ChopinAudioTicket
            {
                Status = AudioTicketStatus.Paused,
                AudioServiceId = Id,
                MusicResource = musicResource,
                PlaybackSource = source,
                Volume = targetVolume
            };

            lock (_ticketSyncRoot)
            {
                _tickets.Add(ticket);
            }

            return ticket;
        }
        catch
        {
            source?.Dispose();
            throw;
        }
    }

    public override Task DisposeAudioTicketAsync(AudioTicketBase audioTicket, CancellationToken ctk = default)
    {
        if (audioTicket is not ChopinAudioTicket ticket || !ticket.TryBeginDispose())
            return Task.CompletedTask;

        try
        {
            try
            {
                _player.PausePlaybackSource(ticket.PlaybackSource);
            }
            catch
            {
                // Disconnect performs its own non-cancellable stop and remains the authority
                // for whether the graph node was actually detached.
            }

            ticket.Status = AudioTicketStatus.Stopped;
            _player.DisconnectPlaybackSource(ticket.PlaybackSource);
            if (ticket.PlaybackSource is IDisposable disposable)
                disposable.Dispose();

            lock (_ticketSyncRoot)
            {
                _tickets.Remove(ticket);
            }

            ticket.CompleteDispose();
            return Task.CompletedTask;
        }
        catch
        {
            ticket.CancelDispose();
            throw;
        }
    }

    public override Task<List<AudioTicketBase>> GetCreatedAudioTicketsAsync(CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        lock (_ticketSyncRoot)
        {
            var tickets = new List<AudioTicketBase>(_tickets.Count);
            foreach (var ticket in _tickets)
                tickets.Add(ticket);
            return Task.FromResult(tickets);
        }
    }

    private async Task EnsurePlayerInitializedAsync(CancellationToken ctk)
    {
        if (_player is AudioGraphPlayer { PlayerCreated: true })
            return;

        await _initializeLock.WaitAsync(ctk).ConfigureAwait(false);
        try
        {
            if (_player is AudioGraphPlayer { PlayerCreated: true })
                return;

            await _player.InitializePlayer(new AudioGraphAudioSetting
            {
                DefaultDeviceId = _setting.AudioRenderDevice,
                OutputVolume = _setting.Volume / 100d,
                AutoFallback = true,
                EnableFFTProcessing = _setting.EnableFFT
            }).ConfigureAwait(false);
        }
        finally
        {
            _initializeLock.Release();
        }
    }
}