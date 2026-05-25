#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace HyPlayer.Domain.Music;

public class NCSong
{
    public NCAlbum Album { get; set; }
    public string Alias { get; set; }
    public List<NCArtist> Artist { get; set; }
    public string CDName { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsCloud { get; set; }
    public bool IsVip { get; set; }
    public double LengthInMilliseconds { get; set; }
    public string MVId { get; set; }
    public int Order { get; set; } = 0;
    public string SongId { get; set; }
    public string SongName { get; set; }
    public SingleSongBase ProviderSong { get; set; }
    public int TrackId { get; set; } = -1;
    public string TranslatedName { get; set; }
    public HyPlayItemType Type { get; set; }
    public int DisplayOrder => Order + 1;

    public Uri Cover =>
        Ioc.Default.GetRequiredService<Setting>().noImage
            ? null
            : new Uri((Album.Cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_SINGLENCSONG_COVER);
    public string CoverString =>
        Ioc.Default.GetRequiredService<Setting>().noImage
            ? null
            : new Uri((Album.Cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.Name)); }
    }

    public string ConvertTranslate(string source)
    {
        return string.IsNullOrEmpty(source) ? "" : "(" + source + ")";
    }
}

public class NCAlbumSong : NCSong
{
    public string DiscName { get; set; }
}

public partial class DiscSongs(IEnumerable<NCAlbumSong> items) : List<NCAlbumSong>(items)
{
    public string Key { get; set; }
}
