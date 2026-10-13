#nullable enable
using System;
using System.Collections.Generic;

namespace HyPlayer.Shell.ExpandedPlayer;

internal readonly record struct CoverColorTicket(long Sequence, object Cover, long Started);

internal sealed class CoverColorRefreshGate(long pendingLimit)
{
    private readonly List<CoverColorTicket> _running = [];
    private readonly object _sync = new();
    private long _applied;
    private object? _appliedCover;
    private long _latest;

    public CoverColorTicket Begin(object cover, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(cover);
        lock (_sync)
        {
            _running.RemoveAll(running => timestamp - running.Started >= pendingLimit);
            var ticket = new CoverColorTicket(++_latest, cover, timestamp);
            _running.Add(ticket);
            return ticket;
        }
    }

    public bool IsCurrent(CoverColorTicket ticket, object? playingCover)
    {
        lock (_sync)
        {
            return IsCurrentCore(ticket, playingCover);
        }
    }

    public bool TryApply(CoverColorTicket ticket, object? playingCover)
    {
        lock (_sync)
        {
            if (!IsCurrentCore(ticket, playingCover)) return false;
            _applied = ticket.Sequence;
            return true;
        }
    }

    public void Complete(CoverColorTicket ticket, bool applied)
    {
        lock (_sync)
        {
            _running.RemoveAll(running => running.Sequence == ticket.Sequence);
            if (applied && ticket.Sequence == _applied) _appliedCover = ticket.Cover;
        }
    }

    public bool NeedsRefresh(object? playingCover, long timestamp)
    {
        lock (_sync)
        {
            if (playingCover is null || ReferenceEquals(_appliedCover, playingCover)) return false;
            foreach (var running in _running)
                if (ReferenceEquals(running.Cover, playingCover) && timestamp - running.Started < pendingLimit)
                    return false;
            return true;
        }
    }

    private bool IsCurrentCore(CoverColorTicket ticket, object? playingCover)
    {
        return ReferenceEquals(ticket.Cover, playingCover) && ticket.Sequence > _applied;
    }
}
