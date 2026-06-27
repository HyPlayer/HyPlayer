#region

using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Serialization;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TagLib;

#endregion

namespace HyPlayer.Infrastructure.Netease;

internal static class The163KeyHelper
{
    private static readonly Aes _aes = Create163Aes();

    private static Aes Create163Aes()
    {
        var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.Key = Encoding.UTF8.GetBytes(@"#14ljk_!\]&0U<'(");
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    /// <summary>
    ///     尝试获取网易云音乐ID
    /// </summary>
    /// <param Name="tag"></param>
    /// <param Name="trackId"></param>
    /// <returns></returns>
    public static bool TryGetTrackId(Tag tag, out ulong trackId)
    {
        if (tag is null)
            throw new ArgumentNullException(nameof(tag));

        trackId = 0;
        var the163Key = tag.Comment;
        if (!Is163KeyCandidate(the163Key))
            the163Key = tag.Description;
        if (!Is163KeyCandidate(the163Key))
            return false;
        try
        {
            TryGetTrackId(the163Key, out trackId);
        }
        catch
        {
            return false;
        }

        return true;
    }


    public static bool TryGetTrackId(string the163Key, out ulong trackId)
    {
        if (string.IsNullOrEmpty(the163Key))
            throw new ArgumentNullException(nameof(the163Key));
        trackId = 0;
        try
        {
            the163Key = the163Key.Substring(22);
            var byt163Key = Convert.FromBase64String(the163Key);
            using (var cryptoTransform = _aes.CreateDecryptor())
            {
                byt163Key = cryptoTransform.TransformFinalBlock(byt163Key, 0, byt163Key.Length);
            }
            var key = JsonSerializer.Deserialize<The163KeyClass>(Encoding.UTF8.GetString(byt163Key).Substring(6), JsonDefaults.Options);
            trackId = (ulong)key.musicId;
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool TryGetMusicInfo(Tag tag, out The163KeyClass KeyStruct)
    {
        if (tag is null)
            throw new ArgumentNullException(nameof(tag));

        KeyStruct = new The163KeyClass();
        var the163Key = tag.Comment;
        if (!Is163KeyCandidate(the163Key))
            the163Key = tag.Description;
        if (!Is163KeyCandidate(the163Key))
            return false;
        try
        {
            TryGetMusicInfo(the163Key, out KeyStruct);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool TryGetMusicInfo(string the163Key, out The163KeyClass KeyStruct)
    {
        if (string.IsNullOrEmpty(the163Key))
            throw new ArgumentNullException(nameof(the163Key));
        KeyStruct = new The163KeyClass();
        try
        {
            the163Key = the163Key.Substring(22);
            var byt163Key = Convert.FromBase64String(the163Key);
            using (var cryptoTransform = _aes.CreateDecryptor())
            {
                byt163Key = cryptoTransform.TransformFinalBlock(byt163Key, 0, byt163Key.Length);
            }

            KeyStruct = JsonSerializer.Deserialize<The163KeyClass>(Encoding.UTF8.GetString(byt163Key)
                .Substring(6), JsonDefaults.Options);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool TrySetMusicInfo(Tag tag, The163KeyClass key)
    {
        try
        {
            var enc = "music:" + JsonSerializer.Serialize(key, JsonDefaults.Options);
            var toEncryptArray = Encoding.UTF8.GetBytes(enc);
            byte[] resultArray;
            using (var cryptoTransform = _aes.CreateEncryptor())
            {
                resultArray = cryptoTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            }

            tag.Description = "163 key(Don't modify):" + Convert.ToBase64String(resultArray, 0, resultArray.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySetMusicInfo(Tag tag, SingleSongBase song, int bitrate, string format)
    {
        if (tag is null)
            throw new ArgumentNullException(nameof(tag));

        try
        {
            var key = new The163KeyClass
            {
                album = song.Album?.Name,
                albumId = ulong.TryParse(song.Album?.ActualId, out var albumId) ? albumId : 0,
                albumPic = GetCover(song),
                bitrate = bitrate,
                duration = song.Duration,
                musicId = long.TryParse(song.ActualId, out var musicId) ? musicId : 0,
                musicName = song.Name,
                format = format.ToLowerInvariant()
            };

            key.artist = GetArtistKey(song);
            return TrySetMusicInfo(tag, key);
        }
        catch
        {
            return false;
        }
    }

    private static List<List<object>> GetArtistKey(SingleSongBase song)
    {
        return song.CreatorList?.Select(name => new List<object> { name, 0 }).ToList() ?? [];
    }

    private static string? GetCover(SingleSongBase song)
    {
        var coverProvider = song.Album as IHasCover ?? song as IHasCover;
        if (coverProvider is null)
            return null;

        var result = coverProvider.GetCoverAsync().GetAwaiter().GetResult();
        return result is IResourceResultOf<Uri?> uriResult
            ? uriResult.GetResourceAsync().GetAwaiter().GetResult()?.ToString()
            : null;
    }

    public static string Get163Key(Tag tag)
    {
        var the163Key = tag.Comment;
        if (!Is163KeyCandidate(the163Key))
            the163Key = tag.Description;
        if (!Is163KeyCandidate(the163Key))
            return null;
        return the163Key;
    }

    private static bool Is163KeyCandidate(string s)
    {
        return !string.IsNullOrEmpty(s) && s.StartsWith("163 key(Don't modify):", StringComparison.Ordinal);
    }
}

public class The163KeyClass
{
    public ulong albumId { get; set; }

    //public string[] Alias { get; set; }
    public string album { get; set; }
    public long musicId { get; set; }
    public string musicName { get; set; }
    public double duration { get; set; }
    public int bitrate { get; set; }
    public string albumPic { get; set; }
    public List<List<object>> artist { get; set; }
    public string format { get; set; }
}
