namespace HyPlayer.Domain.Music;

public enum SongListQueueScopeKind
{
    VisibleSongs,
    Playlist,
    Album,
    Radio,
    Artist,
    SingleSong,
    Content
}

/// <summary>
/// Source ID 前缀常量 — 与 <see cref="Services.Abstractions.IQueueSourceProvider.Prefix"/> 保持一致。
/// 用于 <see cref="SongListQueueScope"/> 构造 sourceId / playSourceId 字符串。
/// </summary>
public static class QueueSourcePrefixes
{
    public const string Playlist = "pl";
    public const string SingleSong = "ns";
    public const string Album = "al";
    public const string Singer = "sa";
    public const string SingerAlias = "sh";
    public const string Radio = "rd";
    public const string Content = "Content";
}

public sealed record SongListQueueScope(
    SongListQueueScopeKind Kind,
    string? Id = null,
    bool IsCompleteSource = false)
{
    public static SongListQueueScope Visible { get; } = new(SongListQueueScopeKind.VisibleSongs);

    public static SongListQueueScope Content { get; } = new(SongListQueueScopeKind.Content, IsCompleteSource: false);

    public static SongListQueueScope Playlist(string playlistId, bool isCompleteSource = true)
    {
        return new SongListQueueScope(SongListQueueScopeKind.Playlist, playlistId, isCompleteSource);
    }

    public static SongListQueueScope Album(string albumId, bool isCompleteSource = true)
    {
        return new SongListQueueScope(SongListQueueScopeKind.Album, albumId, isCompleteSource);
    }

    public static SongListQueueScope Radio(string radioId, bool isCompleteSource = true)
    {
        return new SongListQueueScope(SongListQueueScopeKind.Radio, radioId, isCompleteSource);
    }

    public bool CanLoadCompleteSource => IsCompleteSource && Kind is SongListQueueScopeKind.Playlist or SongListQueueScopeKind.Album or SongListQueueScopeKind.Radio;

    public string? ToSourceId()
    {
        return Kind switch
        {
            SongListQueueScopeKind.Playlist when Id != null => $"{QueueSourcePrefixes.Playlist}{Id}",
            SongListQueueScopeKind.Album when Id != null => $"{QueueSourcePrefixes.Album}{Id}",
            SongListQueueScopeKind.Radio when Id != null => $"{QueueSourcePrefixes.Radio}{Id}",
            _ => null
        };
    }

    public string? ToPlaySourceId()
    {
        return Kind switch
        {
            SongListQueueScopeKind.Playlist when Id != null => $"{QueueSourcePrefixes.Playlist}{Id}",
            SongListQueueScopeKind.Album when Id != null => $"{QueueSourcePrefixes.Album}{Id}",
            SongListQueueScopeKind.Radio when Id != null => $"{QueueSourcePrefixes.Radio}{Id}",
            SongListQueueScopeKind.Content => QueueSourcePrefixes.Content,
            _ => null
        };
    }
}
