using HyPlayer.Platform.Storage.Audio;
using TagLib;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class AudioCoverTests
{
    // A JPEG signature is enough for TagLib's APIC/picture-block format detection.
    private static readonly byte[] CoverBytes = [0xff, 0xd8, 0xff, 0xe0, 0, 16, 74, 70, 73, 70, 0, 1, 0xff, 0xd9];

    [Test]
    public void EncodedCover_ShouldReadFromStartAndIdentifyFrontCover()
    {
        using var encoded = new MemoryStream();
        encoded.Write(CoverBytes); // encoder leaves the stream at EOF
        var picture = TagLibHelper.CreateCoverPicture(encoded);
        AssertCover(picture);
    }

    [Test]
    public void Mp3Cover_ShouldSurviveSaveAsId3v23Apic()
    {
        using var audio = new MemoryStream();
        var frame = new byte[417]; // MPEG-1 Layer III, 128 kbps, 44.1 kHz
        frame[0] = 0xff;
        frame[1] = 0xfb;
        frame[2] = 0x90;
        for (var i = 0; i < 16; i++) audio.Write(frame);
        RoundTrip(audio, ".mp3");
    }

    [Test]
    public void FlacCover_ShouldSurviveSaveAsNativePictureBlock()
    {
        using var audio = new MemoryStream();
        audio.Write("fLaC"u8);
        audio.Write([0x80, 0, 0, 34]); // final STREAMINFO block
        var streamInfo = new byte[34];
        streamInfo[0] = streamInfo[2] = 0x10; // block size 4096
        ulong format = ((ulong)44100 << 44) | (1UL << 41) | (15UL << 36) | 44100;
        for (var i = 0; i < 8; i++) streamInfo[10 + i] = (byte)(format >> (56 - 8 * i));
        audio.Write(streamInfo);
        RoundTrip(audio, ".flac");
    }

    private static void RoundTrip(MemoryStream audio, string extension)
    {
        using var coverStream = new MemoryStream(CoverBytes);
        var picture = TagLibHelper.CreateCoverPicture(coverStream);
        using var abstraction = new UwpStorageFileAbstraction(audio, audio, "fixture" + extension);
        using (var file = TagLibHelper.Create(abstraction, extension))
        {
            file.Tag.Title = "Cover regression";
            TagLibHelper.SetCover(file, picture);
            file.Save();
        }
        audio.Position = 0;
        using var reopened = TagLibHelper.Create(abstraction, extension);
        if (reopened.Tag.Pictures.Length != 1)
            throw new InvalidOperationException("Saved audio must contain one cover.");
        AssertCover(reopened.Tag.Pictures[0]);
        if (extension == ".mp3")
        {
            var id3 = (TagLib.Id3v2.Tag)reopened.GetTag(TagTypes.Id3v2);
            if (id3.Version != 3 || id3.GetFrames<TagLib.Id3v2.AttachmentFrame>().Count() != 1)
                throw new InvalidOperationException("MP3 cover must be a single ID3v2.3 APIC frame.");
        }
    }

    private static void AssertCover(IPicture picture)
    {
        if (!picture.Data.Data.SequenceEqual(CoverBytes) || picture.Type != PictureType.FrontCover ||
            picture.MimeType != "image/jpeg")
            throw new InvalidOperationException("Cover bytes/type/MIME changed.");
    }
}
