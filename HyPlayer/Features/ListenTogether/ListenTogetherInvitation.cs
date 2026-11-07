using System;
using System.Text.RegularExpressions;

namespace HyPlayer.Features.ListenTogether;

public sealed partial record ListenTogetherInvitation(string RoomId, string InviterId, bool IsMulti = false)
{
    private const string ShareHost = "st.music.163.com";
    private const string SharePath = "/listen-together/share/";
    private const string MultiSharePath = "/listen-together/multishare/index.html";
    private static readonly string[] ShortLinkHosts = ["163cn.tv", "163cn.link"];

    public static string BuildShareLink(string roomId, string inviterId, string? songId)
    {
        var song = string.IsNullOrEmpty(songId) ? string.Empty : $"songId={Uri.EscapeDataString(songId)}&";
        return $"https://{ShareHost}{SharePath}?{song}roomId={Uri.EscapeDataString(roomId)}&inviterId={Uri.EscapeDataString(inviterId)}";
    }

    public static string BuildMultiShareLink(string roomId, string inviterId)
    {
        return $"https://{ShareHost}{MultiSharePath}?roomId={Uri.EscapeDataString(roomId)}&inviterUid={Uri.EscapeDataString(inviterId)}&isFLT=false";
    }

    public static bool TryParse(string? text, out ListenTogetherInvitation? invitation)
    {
        invitation = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        var link = LinkRegex().Match(trimmed);
        if (link.Success)
        {
            if (!Uri.TryCreate(link.Value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Host, ShareHost, StringComparison.OrdinalIgnoreCase))
                return false;
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.Equals(SharePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                path.Equals(SharePath + "index.html", StringComparison.OrdinalIgnoreCase))
                return TryFromQuery(uri.Query, false, out invitation);
            if (path.Equals(MultiSharePath, StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/listen-together/multishare", StringComparison.OrdinalIgnoreCase))
                return TryFromQuery(uri.Query, true, out invitation);
            return false;
        }

        if (trimmed.Contains("roomId=", StringComparison.OrdinalIgnoreCase))
            return TryFromQuery(trimmed, trimmed.Contains("inviterUid=", StringComparison.OrdinalIgnoreCase),
                out invitation);

        var pair = PairRegex().Match(trimmed);
        if (!pair.Success) return false;
        invitation = new ListenTogetherInvitation(pair.Groups["room"].Value, pair.Groups["inviter"].Value);
        return true;
    }

    public static bool TryGetShortLink(string? text, out Uri? shortLink)
    {
        shortLink = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (Match match in LinkRegex().Matches(text))
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps &&
                uri.Scheme != Uri.UriSchemeHttp)
                continue;
            if (Array.Exists(ShortLinkHosts, host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)))
            {
                shortLink = uri;
                return true;
            }
        }

        return false;
    }

    private static bool TryFromQuery(string query, bool isMulti, out ListenTogetherInvitation? invitation)
    {
        invitation = null;
        string? roomId = null;
        string? inviterId = null;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var key = part[..separator].Trim();
            var value = Uri.UnescapeDataString(part[(separator + 1)..].Trim());
            if (key.Equals("roomId", StringComparison.OrdinalIgnoreCase))
            {
                if (roomId is not null) return false;
                roomId = value;
            }
            else if (key.Equals(isMulti ? "inviterUid" : "inviterId", StringComparison.OrdinalIgnoreCase))
            {
                if (inviterId is not null) return false;
                inviterId = value;
            }
            else if (isMulti && key.Equals("isFLT", StringComparison.OrdinalIgnoreCase) &&
                     value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!IsValidId(roomId) || !IsValidId(inviterId)) return false;
        invitation = new ListenTogetherInvitation(roomId!, inviterId!, isMulti);
        return true;
    }

    private static bool IsValidId(string? value)
    {
        return !string.IsNullOrEmpty(value) && IdRegex().IsMatch(value);
    }

    [GeneratedRegex(@"https?://[\x21-\x7E]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^(?<room>[A-Za-z0-9_-]{1,64})[\s,，/]+(?<inviter>\d{1,20})$")]
    private static partial Regex PairRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex IdRegex();
}
