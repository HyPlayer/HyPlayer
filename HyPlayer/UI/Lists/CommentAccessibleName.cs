using System.Text;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.UI.Lists;

internal static class CommentAccessibleName
{
    public const int MaxTextLength = 80;

    public static string Format(CommentBase? comment)
    {
        return comment is null ? string.Empty : Format(comment.Sender?.Name, comment.Content);
    }

    public static string Format(string? user, string? text)
    {
        var name = Collapse(user, int.MaxValue);
        var content = Collapse(text, MaxTextLength);
        if (name.Length == 0)
            return content;

        return content.Length == 0 ? name : $"{name}: {content}";
    }

    private static string Collapse(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(character);
        }

        if (builder.Length <= maxLength)
            return builder.ToString();

        var length = maxLength;
        if (char.IsHighSurrogate(builder[length - 1]))
            length--;
        return builder.ToString(0, length).TrimEnd() + "…";
    }
}
