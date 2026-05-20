namespace HyPlayer.Domain.Music;

public abstract record MusicResource
{
    private MusicResource()
    {
    }

    public sealed record Album(string Id) : MusicResource;
    public sealed record Artist(string Id) : MusicResource;
    public sealed record DailyRecommend(string Id) : MusicResource;
    public sealed record Playlist(string Id) : MusicResource;
    public sealed record Radio(string Id) : MusicResource;
    public sealed record Song(string Id) : MusicResource;

    public string ToPlaybackSourceKey() => this switch
    {
        Album album => "al" + album.Id,
        Artist artist => "ar" + artist.Id,
        DailyRecommend dailyRecommend => dailyRecommend.Id,
        Playlist playlist => "pl" + playlist.Id,
        Radio radio => "rd" + radio.Id,
        Song song => "ns" + song.Id,
        _ => throw new System.InvalidOperationException("Unsupported music resource type.")
    };

    public static bool TryParseExternalResource(string value, out MusicResource resource)
    {
        resource = value.Length >= 2 && IsNumeric(value[2..]) ? value[..2] switch
        {
            "al" => new Album(value[2..]),
            "ar" => new Artist(value[2..]),
            "ns" => new Song(value[2..]),
            "pl" => new Playlist(value[2..]),
            "rd" => new Radio(value[2..]),
            _ => null!
        } : null!;

        return resource is not null;
    }

    private static bool IsNumeric(string value)
    {
        if (value.Length == 0) return false;
        foreach (var c in value)
            if (!char.IsDigit(c)) return false;
        return true;
    }
}
