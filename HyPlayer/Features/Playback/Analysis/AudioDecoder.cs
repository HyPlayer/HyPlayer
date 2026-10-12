using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class PcmBuffer
{
    public required float[] Samples { get; set; }
    public required int Channels { get; init; }
    public required int SampleRate { get; init; }
    public TimeSpan Start { get; init; }
    public int Frames => Samples.Length / Channels;
    public double Seconds => Frames / (double)SampleRate;
}

public static class AudioDecoder
{
    public static async Task<uint> GetNativeSampleRateAsync(IRandomAccessStream source)
    {
        var profile = await MediaEncodingProfile.CreateFromStreamAsync(source.CloneStream());
        return profile.Audio?.SampleRate ?? 44100;
    }

    public static async Task<PcmBuffer> DecodeAsync(IRandomAccessStream source, uint sampleRate, uint channels, TimeSpan start, TimeSpan length, CancellationToken ct)
    {
        if (sampleRate == 0) sampleRate = await GetNativeSampleRateAsync(source);
        var transcoder = new MediaTranscoder { TrimStartTime = start, HardwareAccelerationEnabled = false };
        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
        profile.Audio = AudioEncodingProperties.CreatePcm(sampleRate, channels, 32);
        profile.Audio.Subtype = MediaEncodingSubtypes.Float;
        profile.Video = null;
        using var output = new InMemoryRandomAccessStream();
        var input = source.CloneStream();
        var prepared = await transcoder.PrepareStreamTranscodeAsync(input, output, profile).AsTask(ct);
        if (!prepared.CanTranscode) throw new InvalidOperationException("Cannot decode audio: " + prepared.FailureReason);
        await prepared.TranscodeAsync().AsTask(ct);
        output.Seek(0);
        var header = new byte[(int)Math.Min(output.Size, 65536)];
        await ReadExactAsync(output, header, header.Length, ct);
        var (decodedChannels, rate, dataOffset, dataSize) = ParseWavHeader(header, output.Size);
        var count = dataSize / 4;
        if (length > TimeSpan.Zero) count = Math.Min(count, (long)(length.TotalSeconds * rate) * decodedChannels);
        var samples = new float[count];
        output.Seek((ulong)dataOffset);
        var totalBytes = samples.Length * sizeof(float);
        var chunk = new byte[1 << 20];
        var filled = 0;
        while (filled < totalBytes)
        {
            var read = await ReadExactAsync(output, chunk, Math.Min(chunk.Length, totalBytes - filled), ct);
            if (read == 0) break;
            chunk.AsSpan(0, read).CopyTo(MemoryMarshal.AsBytes(samples.AsSpan())[filled..]);
            filled += read;
        }
        return new PcmBuffer { Samples = samples, Channels = decodedChannels, SampleRate = rate, Start = start };
    }

    private static async Task<int> ReadExactAsync(IRandomAccessStream stream, byte[] destination, int count, CancellationToken ct)
    {
        var result = await stream.ReadAsync(destination.AsBuffer(0, count), (uint)count, InputStreamOptions.None).AsTask(ct);
        var read = (int)result.Length;
        if (read > 0) result.CopyTo(0, destination, 0, read);
        return read;
    }

    private static (int Channels, int Rate, long DataOffset, long DataSize) ParseWavHeader(byte[] bytes, ulong totalSize)
    {
        int pos = 12, channels = 0, rate = 0, bits = 0;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToUInt16(bytes, pos + 10);
                rate = BitConverter.ToInt32(bytes, pos + 12);
                bits = BitConverter.ToUInt16(bytes, pos + 22);
            }
            else if (id == "data")
            {
                if (bits != 32) throw new InvalidOperationException("Unexpected decoded sample format");
                var remaining = (long)totalSize - pos - 8;
                var available = size <= 0 || size > remaining ? remaining : size;
                return (channels, rate, pos + 8, available);
            }
            pos += 8 + Math.Max(0, size) + (size & 1);
        }
        throw new InvalidOperationException("Decoded audio has no data chunk");
    }
}
