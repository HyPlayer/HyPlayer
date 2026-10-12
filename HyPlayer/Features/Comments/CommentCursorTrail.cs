using System.Collections.Generic;

namespace HyPlayer.Features.Comments;

internal sealed class CommentCursorTrail
{
    private readonly Dictionary<int, string> _cursors = [];

    public string For(int page)
    {
        return page > 1 && _cursors.TryGetValue(page, out var cursor) ? cursor : null;
    }

    public void Record(int loadedPage, string nextCursor)
    {
        if (string.IsNullOrEmpty(nextCursor))
            _cursors.Remove(loadedPage + 1);
        else
            _cursors[loadedPage + 1] = nextCursor;
    }

    public void Clear()
    {
        _cursors.Clear();
    }
}
