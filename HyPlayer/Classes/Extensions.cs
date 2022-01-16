namespace HyPlayer.Classes;

public static class Extensions
{
    public static string EscapeForPath(this string str)
    {
        return str.Replace("\\", "＼").Replace("/", "／").Replace(":", "：").Replace("?", "？").Replace("\"", "＂")
            .Replace("<", "＜").Replace(">", "＞").Replace("|", "｜");
    }
}