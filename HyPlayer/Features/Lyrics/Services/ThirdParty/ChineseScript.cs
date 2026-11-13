using System.Runtime.InteropServices;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal static partial class ChineseScript
{
    private const uint LcmapSimplifiedChinese = 0x02000000;

    [LibraryImport("kernel32.dll", EntryPoint = "LCMapStringEx", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int LCMapStringEx(string localeName, uint mapFlags, char* source, int sourceLength,
        char* destination, int destinationLength, nint versionInformation, nint reserved, nint sortHandle);

    public static unsafe string ToSimplified(string value)
    {
        if (value.Length == 0) return value;
        var buffer = new char[value.Length];
        fixed (char* source = value)
        fixed (char* destination = buffer)
        {
            var written = LCMapStringEx("zh-CN", LcmapSimplifiedChinese, source, value.Length, destination,
                buffer.Length, 0, 0, 0);
            return written == value.Length ? new string(buffer) : value;
        }
    }
}
