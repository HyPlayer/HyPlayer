using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Buffer = Windows.Storage.Streams.Buffer;

namespace HyPlayer.Platform.Network;

public static class MIMEHelper
{
    public static Dictionary<string, string> NcmFileMimeTypes { get; } = new()
    {
        { "mp3", "audio/mpeg" },
        { "flac", "audio/x-flac" }
    };

    public const uint PictureFileHeaderCapacity = 10;

    public static string GetNCMFileMimeType(string fileExtension)
    {
        if (NcmFileMimeTypes.TryGetValue(fileExtension, out var mimeType)) return mimeType;

        throw new ArgumentException();
    }

    public static async Task<Guid> GetPictureCodecFromStream(IRandomAccessStream pictureStream)
    {
        var buffer = new Buffer(PictureFileHeaderCapacity);
        await pictureStream.ReadAsync(buffer, PictureFileHeaderCapacity, InputStreamOptions.None);
        if (buffer.Length < PictureFileHeaderCapacity) throw new ArgumentOutOfRangeException();
        var byteArray = buffer.ToArray();
        if (byteArray[0] == 0x89 && byteArray[1] == 0x50 && byteArray[2] == 0x4e &&
            byteArray[3] == 0x47)
            // PNG
            return BitmapDecoder.PngDecoderId;

        if (byteArray[6] == 0x4a && byteArray[7] == 0x46 && byteArray[8] == 0x49 &&
            byteArray[9] == 0x46)
            // JPEG
            return BitmapDecoder.JpegDecoderId;

        if (byteArray[0] == 0x52 && byteArray[1] == 0x49 && byteArray[2] == 0x46 &&
            byteArray[3] == 0x46 && byteArray[8] == 0x57)
            // WEBP
            return BitmapDecoder.WebpDecoderId;

        throw new ArgumentOutOfRangeException();
    }

    public static Guid GetPictureCodecFromBuffer(Buffer buffer)
    {
        if (buffer.Length < PictureFileHeaderCapacity) throw new ArgumentOutOfRangeException();
        var byteArray = buffer.ToArray();
        if (byteArray[0] == 0x89 && byteArray[1] == 0x50 && byteArray[2] == 0x4e &&
            byteArray[3] == 0x47)
            // PNG
            return BitmapDecoder.PngDecoderId;

        if (byteArray[6] == 0x4a && byteArray[7] == 0x46 && byteArray[8] == 0x49 &&
            byteArray[9] == 0x46)
            // JPEG
            return BitmapDecoder.JpegDecoderId;

        if (byteArray[0] == 0x52 && byteArray[1] == 0x49 && byteArray[2] == 0x46 &&
            byteArray[3] == 0x46 && byteArray[8] == 0x57)
            // WEBP
            return BitmapDecoder.WebpDecoderId;

        throw new ArgumentOutOfRangeException();
    }
}
