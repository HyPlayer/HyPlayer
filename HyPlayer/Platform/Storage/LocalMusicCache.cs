using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.Platform.Playback.LocalProvider;

namespace HyPlayer.Platform.Storage;

public static class LocalMusicCache
{
    private const string CacheFileName = "local_music_cache.tsv";
    private const string ListSeparator = "; ";
    private const int ColumnCount = 10;

    public static async Task SaveAsync(IReadOnlyList<LocalSong> items)
    {
        try
        {
            var builder = new StringBuilder();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.ActualId)) continue;

                var artists = item.Artists ?? [];
                builder.Append(Clean(item.ActualId)).Append('\t')
                    .Append(Clean(item.Name)).Append('\t')
                    .Append(JoinList(item.CreatorList ?? [])).Append('\t')
                    .Append(JoinList(artists.Select(artist => artist.ActualId))).Append('\t')
                    .Append(Clean(item.Album?.Name)).Append('\t')
                    .Append(Clean(item.Album?.ActualId)).Append('\t')
                    .Append(item.Duration).Append('\t')
                    .Append(Clean(item.ExtensionName)).Append('\t')
                    .Append(item.IsNcm ? '1' : '0').Append('\t')
                    .Append(Clean(item.InfoTag)).Append('\n');
            }

            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                CacheFileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, builder.ToString());
        }
        catch
        {
        }
    }

    public static async Task<List<LocalSong>?> LoadAsync()
    {
        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(CacheFileName) is not StorageFile file)
                return null;

            var items = new List<LocalSong>();
            foreach (var line in await FileIO.ReadLinesAsync(file))
            {
                var parts = line.Split('\t');
                if (parts.Length < ColumnCount || string.IsNullOrEmpty(parts[0])) continue;

                var artistNames = parts[2].Split(ListSeparator);
                var artistIds = parts[3].Split(ListSeparator);
                var artists = new List<LocalArtist>();
                for (var i = 0; i < artistNames.Length; i++)
                {
                    if (string.IsNullOrEmpty(artistNames[i])) continue;

                    artists.Add(new LocalArtist
                    {
                        Name = artistNames[i],
                        ActualId = i < artistIds.Length ? artistIds[i] : string.Empty
                    });
                }

                if (artists.Count == 0)
                    artists.Add(new LocalArtist { Name = "未知歌手", ActualId = string.Empty });

                items.Add(new LocalSong
                {
                    ActualId = parts[0],
                    Name = string.IsNullOrEmpty(parts[1])
                        ? System.IO.Path.GetFileNameWithoutExtension(parts[0])
                        : parts[1],
                    CreatorList = artists.Select(artist => artist.Name).ToList(),
                    Artists = artists,
                    Album = new LocalAlbum { Name = parts[4], ActualId = parts[5] },
                    Duration = long.TryParse(parts[6], out var duration) ? duration : 0,
                    ExtensionName = parts[7],
                    IsNcm = parts[8] == "1",
                    InfoTag = parts[9],
                    CdName = "01",
                    Available = true
                });
            }

            return items;
        }
        catch
        {
            return null;
        }
    }

    private static string Clean(string? value)
    {
        return (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string JoinList(IEnumerable<string?> values)
    {
        return string.Join(ListSeparator, values.Select(value => Clean(value).Replace(ListSeparator, ";")));
    }
}
