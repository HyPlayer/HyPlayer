using System;

namespace HyPlayer.Features.Comments;

internal static class CommentPageText
{
    public const int PageSize = 20;

    public static string Format(int page, int? totalCount)
    {
        var current = Math.Max(1, page);
        if (totalCount is not > 0)
            return $"第 {current} 页";

        var total = Math.Max(current, (totalCount.Value + PageSize - 1) / PageSize);
        return $"第 {current} 页 / 共 {total} 页";
    }
}
