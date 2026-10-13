#nullable enable
using System;

namespace HyPlayer.Shell.ExpandedPlayer;

internal readonly record struct CoverColorTicket(long Sequence, object Cover);

internal sealed class CoverColorRefreshGate
{
    private readonly object _sync = new();
    private long _applied;
    private object? _cover;
    private long _latest;

    public CoverColorTicket Begin(object cover)
    {
        ArgumentNullException.ThrowIfNull(cover);
        lock (_sync)
        {
            _cover = cover;
            return new CoverColorTicket(++_latest, cover);
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

    private bool IsCurrentCore(CoverColorTicket ticket, object? playingCover)
    {
        return ReferenceEquals(ticket.Cover, playingCover)
               && ReferenceEquals(ticket.Cover, _cover)
               && ticket.Sequence > _applied;
    }
}
