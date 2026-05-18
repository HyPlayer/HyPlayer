namespace HyPlayer.Classes;

public abstract record AppRoute
{
    private AppRoute()
    {
    }

    public sealed record Album(string Id) : AppRoute;
    public sealed record Artist(string Id) : AppRoute;
    public sealed record DailyRecommend : AppRoute;
    public sealed record Favorite : AppRoute;
    public sealed record History : AppRoute;
    public sealed record Home : AppRoute;
    public sealed record LikedSongs : AppRoute;
    public sealed record LocalMusic : AppRoute;
    public sealed record Me(string? UserId = null) : AppRoute;
    public sealed record MusicCloud : AppRoute;
    public sealed record MV(string Id) : AppRoute;
    public sealed record Playlist(string Id) : AppRoute;
    public sealed record Radio(string Id) : AppRoute;
    public sealed record Settings : AppRoute;
    public sealed record Song(string Id) : AppRoute;

    public static bool TryParseExternalResource(string value, out AppRoute route)
    {
        route = value.Length >= 2 && IsNumeric(value[2..]) ? value[..2] switch
        {
            "al" => new Album(value[2..]),
            "ar" => new Artist(value[2..]),
            "ml" => new MV(value[2..]),
            "ns" => new Song(value[2..]),
            "pl" => new Playlist(value[2..]),
            "rd" => new Radio(value[2..]),
            "us" => new Me(value[2..]),
            _ => null!
        } : null!;

        return route is not null;
    }

    private static bool IsNumeric(string value)
    {
        if (value.Length == 0) return false;
        foreach (var c in value)
            if (!char.IsDigit(c)) return false;
        return true;
    }
}
