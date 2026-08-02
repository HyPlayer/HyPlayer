namespace HyPlayer.Domain;

/// <summary>
///     Lightweight user display info for XAML-bound types.
///     Replaces NCUser usage in Comment and UserDisplay without required-member constraints.
/// </summary>
public sealed class CommentUserInfo
{
    public string ActualId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Description { get; set; }
}