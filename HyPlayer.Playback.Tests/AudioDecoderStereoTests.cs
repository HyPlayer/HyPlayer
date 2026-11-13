using System.Runtime.InteropServices.WindowsRuntime;
using HyPlayer.Features.Playback.Analysis;
using TUnit.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace HyPlayer.Playback.Tests;

public sealed class AudioDecoderStereoTests
{
    [Test]
    [Arguments(48000u)]
    [Arguments(44100u)]
    [Arguments(32000u)]
    public async Task Mp3Decode_ShouldPreserveStereoWhenChangingSampleRate(uint sampleRate)
    {
        using var wav = new InMemoryRandomAccessStream();
        await wav.WriteAsync(CreateStereoWav().AsBuffer());
        wav.Seek(0);
        using var mp3 = new InMemoryRandomAccessStream();
        var profile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
        profile.Audio = AudioEncodingProperties.CreateMp3(44100, 2, 192000);
        var prepared = await new MediaTranscoder().PrepareStreamTranscodeAsync(wav, mp3, profile);
        Ensure(prepared.CanTranscode, "Cannot encode the synthetic MP3 fixture.");
        await prepared.TranscodeAsync();
        mp3.Seek(0);

        var pcm = await AudioDecoder.DecodeAsync(mp3, sampleRate, 2,
            TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.75), CancellationToken.None);

        Ensure(pcm.Channels == 2 && pcm.SampleRate == sampleRate, "Requested PCM format changed.");
        Ensure(pcm.Frames == (int)(sampleRate * 0.75), "Trimmed duration changed during resampling.");
        var leftHigh = TonePower(pcm, 0, 10000);
        var rightHigh = TonePower(pcm, 1, 10000);
        var leftLow = TonePower(pcm, 0, 700);
        var rightLow = TonePower(pcm, 1, 700);
        Ensure(leftHigh > 0.001 && leftHigh > rightHigh * 20,
            "The left high-frequency signal was lost or folded into the right channel.");
        Ensure(rightLow > 0.001 && rightLow > leftLow * 20,
            "The right signal was lost or folded into the left channel.");
    }

    private static double TonePower(PcmBuffer pcm, int channel, double frequency)
    {
        double real = 0, imaginary = 0;
        var from = pcm.SampleRate / 10;
        var count = pcm.Frames - from;
        for (var i = from; i < pcm.Frames; i++)
        {
            var angle = 2 * Math.PI * frequency * i / pcm.SampleRate;
            var sample = pcm.Samples[i * pcm.Channels + channel];
            real += sample * Math.Cos(angle);
            imaginary += sample * Math.Sin(angle);
        }
        return (real * real + imaginary * imaginary) / (count * (double)count);
    }

    private static byte[] CreateStereoWav()
    {
        const int rate = 44100;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + rate * 4); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate);
        writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(rate * 4);
        for (var i = 0; i < rate; i++)
        {
            writer.Write((short)(10000 * Math.Sin(2 * Math.PI * 10000 * i / rate)));
            writer.Write((short)(10000 * Math.Sin(2 * Math.PI * 700 * i / rate)));
        }
        return stream.ToArray();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
