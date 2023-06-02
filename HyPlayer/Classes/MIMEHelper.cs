using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Buffer = Windows.Storage.Streams.Buffer;

namespace HyPlayer.Classes
{
    public static class MIMEHelper
    {
        public static readonly uint FILE_HEADER_CAPACITY = 10;
        public static async Task<Guid> GetPictureCodec(IRandomAccessStream stream)
        {
            var buffer = new Buffer(FILE_HEADER_CAPACITY);
            using var pictureStream = stream.CloneStream();
            await pictureStream.ReadAsync(buffer, FILE_HEADER_CAPACITY, InputStreamOptions.None);
            if (buffer.Length < 10) throw new ArgumentOutOfRangeException();
            var byteArray = buffer.ToArray();
            if (byteArray[0] == 0x89 && byteArray[1] == 0x50 && byteArray[2] == 0x4e &&
                byteArray[3] == 0x47)
            {
                // PNG
                return BitmapDecoder.PngDecoderId;
            }

            if (byteArray[6] == 0x4a && byteArray[7] == 0x46 && byteArray[8] == 0x49 &&
                byteArray[9] == 0x46)
            {
                // JPEG
                return BitmapDecoder.JpegDecoderId;
            }

            if (byteArray[0] == 0x52 && byteArray[1] == 0x49 && byteArray[2] == 0x46 &&
                byteArray[3] == 0x46 && byteArray[8] == 0x57)
            {
                // WEBP
                return BitmapDecoder.WebpDecoderId;
            }

            throw new ArgumentOutOfRangeException();
        }
    }
}
