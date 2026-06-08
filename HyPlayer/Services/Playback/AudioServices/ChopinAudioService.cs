using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices.IPlaybackSpeedChangeable;

namespace HyPlayer.Services.Playback.AudioServices;

public sealed class ChopinAudioService :
    AudioServiceBase,
    IPlayAudioTicketService,
    IPauseAudioTicketService,
    IStopAudioTicketService,
    IAudioTicketSeekableService,
    IOutgoingVolumeChangeable,
    IAudioTicketVolumeChangeable,
    IPlaybackRateChangeableService,
    IAudioTicketListProvidable
{
    private readonly IPlayer _player;
    private readonly List<ChopinAudioTicket> _tickets = [];
    private readonly object _ticketSyncRoot = new();

    public ChopinAudioService(IPlayer player)
    {
        _player = player;
    }

    public override string Id => "hyplayer.chopin";

    public override string Name => "HyPlayer Chopin AudioService";

    public override async Task<AudioTicketBase> GetAudioTicketAsync(MusicResourceBase musicResource, CancellationToken ctk = default)
    {
        return await CreateAudioTicketAsync(musicResource, setAsPrimarySource: true, ctk: ctk);
    }

    public async Task<ChopinAudioTicket> CreateAudioTicketAsync(
        MusicResourceBase musicResource,
        bool setAsPrimarySource,
        double? initialVolume = null,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (musicResource is not IChopinPlaybackSourceResource && musicResource.Uri is null)
        {
            throw new ArgumentException("Music resource must have a Uri.", nameof(musicResource));
        }

        AudioGraphPlaybackSource? source = null;
        try
        {
            var targetVolume = initialVolume ?? 1d;
            if (musicResource is IChopinPlaybackSourceResource chopinResource)
            {
                source = await chopinResource.CreatePlaybackSourceAsync(ctk);
                if (source is null)
                {
                    throw new ArgumentException("Music resource did not create a playback source.", nameof(musicResource));
                }

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
        ctk.ThrowIfCancellationRequested();
        if (audioTicket is not ChopinAudioTicket ticket)
        {
            return Task.CompletedTask;
        }

        _player.DisconnectPlaybackSource(ticket.PlaybackSource);
        if (ticket.PlaybackSource is IDisposable disposable)
        {
            disposable.Dispose();
        }

        lock (_ticketSyncRoot)
        {
            _tickets.Remove(ticket);
        }

        return Task.CompletedTask;
    }

    public override Task<List<AudioTicketBase>> GetCreatedAudioTicketsAsync(CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        lock (_ticketSyncRoot)
        {
            return Task.FromResult(_tickets.Cast<AudioTicketBase>().ToList());
        }
    }

    public Task<List<AudioTicketBase>> GetAudioTicketListAsync(CancellationToken ctk = default)
    {
        return GetCreatedAudioTicketsAsync(ctk);
    }

    public Task PlayAudioTicketAsync(AudioTicketBase ticket, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.PlayPlaybackSource(chopinTicket.PlaybackSource);
            chopinTicket.Status = AudioTicketStatus.Playing;
        }

        return Task.CompletedTask;
    }

    public Task PauseAudioTicketAsync(AudioTicketBase ticket, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.PausePlaybackSource(chopinTicket.PlaybackSource);
            chopinTicket.Status = AudioTicketStatus.Paused;
        }

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

    public Task SeekAudioTicketAsync(AudioTicketBase audioTicket, double position, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (audioTicket is ChopinAudioTicket chopinTicket)
        {
            _player.SeekPlaybackSource(TimeSpan.FromMilliseconds(position), chopinTicket.PlaybackSource);
        }

        return Task.CompletedTask;
    }

    public Task ChangeOutgoingVolumeAsync(double volume, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        _player.SetOutputVolume(volume);
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

    public Task ChangePlaybackSpeedAsync(AudioTicketBase ticket, double playbackSpeed, CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        if (ticket is ChopinAudioTicket chopinTicket)
        {
            _player.SetPlaybackSourceSpeed(playbackSpeed, chopinTicket.PlaybackSource);
        }

        return Task.CompletedTask;
    }
}
