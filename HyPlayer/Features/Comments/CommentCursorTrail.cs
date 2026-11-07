using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Comments;

internal sealed class CommentCursorTrail
{
    public const int MaxPagesPerJump = 10;

    private readonly Dictionary<int, string> _tokens = [];
    private bool _offsetAddressable;

    public string For(int page)
    {
        return page > 1 && _tokens.TryGetValue(page, out var token) ? token : null;
    }

    public bool CanOpen(int page)
    {
        return page <= 1 || _offsetAddressable || _tokens.ContainsKey(page);
    }

    public void Record(int loadedPage, ProviderPageResult<CommentBase> result)
    {
        if (result.NextOffset is not null)
            _offsetAddressable = true;

        if (result.HasMore && result.NextOffset is null && !string.IsNullOrEmpty(result.NextPageToken))
            _tokens[loadedPage + 1] = result.NextPageToken;
        else
            _tokens.Remove(loadedPage + 1);
    }

    public async Task<(ProviderPageResult<CommentBase> Result, int Page)> OpenAsync(
        int page,
        Func<int, string, Task<ProviderPageResult<CommentBase>>> load)
    {
        var current = Math.Max(1, page);
        while (!CanOpen(current))
            current--;

        var walked = 0;
        while (true)
        {
            var result = await load(current, For(current));
            Record(current, result);
            if (current >= page)
                return (result, current);

            if (CanOpen(page))
            {
                current = page;
            }
            else if (walked < MaxPagesPerJump && CanOpen(current + 1))
            {
                current++;
                walked++;
            }
            else
            {
                return (result, current);
            }
        }
    }
}
