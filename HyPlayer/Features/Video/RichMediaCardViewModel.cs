namespace HyPlayer.Features.Video;

public sealed class RichMediaCardViewModel
{
    public string ActualId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string CoverUrl { get; init; } = string.Empty;
}
