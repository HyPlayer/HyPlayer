using System;
using Windows.Storage;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Lyrics.Services;

public enum LyricSourceKind
{
    Auto,
    Own,
    Amll,
    Kugou,
    Lrclib
}

public enum LyricSourceState
{
    Available,
    Unavailable,
    Disabled
}

public sealed record LyricSourceOption(LyricSourceKind Kind, LyricSourceState State, bool WordLevel);

public sealed class LyricSourcePreferences
{
    private const string ContainerName = "LyricSourceChoices";

    public LyricSourceKind Get(SingleSongBase item)
    {
        return Container().Values.TryGetValue(Key(item), out var value) && value is string text &&
               Enum.TryParse<LyricSourceKind>(text, out var kind)
            ? kind
            : LyricSourceKind.Auto;
    }

    public void Set(SingleSongBase item, LyricSourceKind kind)
    {
        var values = Container().Values;
        if (kind == LyricSourceKind.Auto) values.Remove(Key(item));
        else values[Key(item)] = kind.ToString();
    }

    private static ApplicationDataContainer Container()
    {
        return ApplicationData.Current.LocalSettings.CreateContainer(ContainerName,
            ApplicationDataCreateDisposition.Always);
    }

    private static string Key(SingleSongBase item)
    {
        return $"{item.ProviderId}:{item.ActualId}";
    }
}
