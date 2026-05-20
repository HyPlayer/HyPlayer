using HyPlayer.NeteaseApi.Models;

namespace HyPlayer.Domain.Comments;

public sealed record CommentTarget(NeteaseResourceType ResourceType, string ResourceId)
{
    public static CommentTarget Album(string id) => new(NeteaseResourceType.Album, id);

    public static CommentTarget MLog(string id) => new(NeteaseResourceType.MLog, id);

    public static CommentTarget MV(string id) => new(NeteaseResourceType.MV, id);

    public static CommentTarget Playlist(string id) => new(NeteaseResourceType.Playlist, id);

    public static CommentTarget RadioProgram(string id) => new(NeteaseResourceType.RadioProgram, id);

    public static CommentTarget Song(string id) => new(NeteaseResourceType.Song, id);

    public static bool TryParseExternalResource(string value, out CommentTarget target)
    {
        target = value.Length >= 2 && value[2..].Length > 0 ? value[..2] switch
        {
            "al" => Album(value[2..]),
            "fm" => RadioProgram(value[2..]),
            "mb" => MLog(value[2..]),
            "mv" => MV(value[2..]),
            "pl" => Playlist(value[2..]),
            "sg" => Song(value[2..]),
            _ => null!
        } : null!;

        return target is not null;
    }
}
