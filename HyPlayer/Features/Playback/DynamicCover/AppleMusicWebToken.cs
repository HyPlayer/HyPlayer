using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyPlayer.Features.Playback.DynamicCover;

internal readonly record struct AppleMusicToken(string Value, DateTimeOffset ExpiresAt);

internal static partial class AppleMusicWebToken
{
    [GeneratedRegex("<script[^>]+src=\"(?<src>[^\"]*/assets/index[^\"]*\\.js)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex JwtRegex();

    public static string? FindScriptPath(string html)
    {
        var match = ScriptRegex().Match(html);
        return match.Success ? match.Groups["src"].Value : null;
    }

    public static AppleMusicToken? FindToken(string script, DateTimeOffset now)
    {
        AppleMusicToken? fallback = null;
        foreach (Match match in JwtRegex().Matches(script))
        {
            if (!TryReadPayload(match.Value, out var issuer, out var expiresAt) || expiresAt <= now) continue;
            var token = new AppleMusicToken(match.Value, expiresAt);
            if (issuer == "AMPWebPlay") return token;
            fallback ??= token;
        }

        return fallback;
    }

    private static bool TryReadPayload(string jwt, out string? issuer, out DateTimeOffset expiresAt)
    {
        issuer = null;
        expiresAt = default;
        var parts = jwt.Split('.');
        if (parts.Length != 3) return false;
        try
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var root = payload.RootElement;
            if (!root.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number) return false;
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
            if (root.TryGetProperty("iss", out var iss) && iss.ValueKind == JsonValueKind.String)
                issuer = iss.GetString();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
