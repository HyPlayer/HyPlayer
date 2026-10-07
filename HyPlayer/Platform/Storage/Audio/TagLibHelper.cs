using System;
using System.IO;
using TagLib;
using TagLib.Mpeg;
using File = TagLib.File;

namespace HyPlayer.Platform.Storage.Audio;

public static class TagLibHelper
{
    public static Picture CreateCoverPicture(Stream jpegStream)
    {
        jpegStream.Position = 0;
        var data = ByteVector.FromStream(jpegStream);
        if (data.Count == 0)
            throw new InvalidDataException("封面图片为空");
        return new Picture(data)
        {
            Type = PictureType.FrontCover,
            MimeType = "image/jpeg",
            Description = "Cover.jpg"
        };
    }

    public static void SetCover(File file, Picture picture)
    {
        // ID3v2.3 APIC is also understood by older Windows/portable players.
        if (file is AudioFile && file.GetTag(TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3)
        {
            id3.Version = 3;
            id3.Pictures = [picture];
        }
        file.Tag.Pictures = [picture];
    }

    public static File Create(File.IFileAbstraction abstraction, string extensions)
    {
        return extensions switch
        {
            ".flac" => new TagLib.Flac.File(abstraction),
            ".mp3" => new AudioFile(abstraction),
            ".ape" => new TagLib.Ape.File(abstraction),
            ".m4a" => new TagLib.Mpeg4.File(abstraction),
            ".wav" => new TagLib.Riff.File(abstraction),
            ".aac" => new AudioFile(abstraction),
            _ => throw new ArgumentOutOfRangeException(nameof(extensions))
        };
    }
}
