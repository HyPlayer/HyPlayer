using System.Net;
using System;
using System.Text;
using System.Security.Cryptography;

namespace HyPlayer.Classes;

internal static class Extensions
{
    public static string EscapeForPath(this string str)
    {
        return str.Replace("\\", "＼").Replace("/", "／").Replace(":", "：").Replace("?", "？").Replace("\"", "＂")
            .Replace("<", "＜").Replace(">", "＞").Replace("|", "｜").Replace("*", "＊");
    }

    public static byte[] ToByteArrayUtf8(this string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }

    public static string ToHexStringLower(this byte[] value)
    {
        var sb = new StringBuilder();
        foreach (var t in value) sb.Append(t.ToString("x2"));

        return sb.ToString();
    }

    public static string ToHexStringUpper(this byte[] value)
    {
        var sb = new StringBuilder();
        foreach (var t in value) sb.Append(t.ToString("X2"));

        return sb.ToString();
    }

    public static string ToBase64String(this byte[] value)
    {
        return Convert.ToBase64String(value);
    }
#nullable enable
    private static MD5? _md5;
#nullable restore
    public static byte[] ComputeMd5(this byte[] value)
    {
        _md5 ??= MD5.Create();
        return _md5.ComputeHash(value);
    }

    public static byte[] RandomBytes(this Random random, int length)
    {
        var buffer = new byte[length];
        random.NextBytes(buffer);
        return buffer;
    }

    public static string Get(this CookieCollection cookies, string name, string defaultValue)
    {
        return cookies[name]?.Value ?? defaultValue;
    }
}