#nullable enable
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class FlacFrameLocator
{
    private const int Window = 262144;
    private const int MaxHeaderBytes = 17;
    private static readonly byte[] Crc8 = BuildCrc8();

    private readonly IRandomAccessStream _stream;
    private readonly byte[] _buffer = new byte[Window];
    private readonly int _fixedBlockSize;

    private FlacFrameLocator(IRandomAccessStream stream, long audioStart, long size, int sampleRate, int minBlock, int maxBlock, long totalSamples, bool hasSeekTable)
    {
        _stream = stream;
        AudioStart = audioStart;
        Size = size;
        SampleRate = sampleRate;
        MaxBlockSize = maxBlock;
        TotalSamples = totalSamples;
        HasSeekTable = hasSeekTable;
        _fixedBlockSize = minBlock == maxBlock ? maxBlock : 0;
    }

    public long AudioStart { get; }
    public long Size { get; }
    public int SampleRate { get; }
    public int MaxBlockSize { get; }
    public long TotalSamples { get; }
    public bool HasSeekTable { get; }

    public static async Task<FlacFrameLocator?> OpenAsync(IRandomAccessStream stream)
    {
        try
        {
            var size = (long)stream.Size;
            var head = await ReadAsync(stream, 0, 42);
            if (head.Length < 42 || head[0] != (byte)'f' || head[1] != (byte)'L' || head[2] != (byte)'a' || head[3] != (byte)'C') return null;
            if ((head[4] & 0x7F) != 0) return null;
            var info = head.AsSpan(8);
            var minBlock = (info[0] << 8) | info[1];
            var maxBlock = (info[2] << 8) | info[3];
            var rate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
            var total = ((long)(info[13] & 0x0F) << 32) | ((long)info[14] << 24) | ((long)info[15] << 16) | ((long)info[16] << 8) | info[17];
            if (rate <= 0 || maxBlock <= 0) return null;

            var hasSeekTable = false;
            long offset = 4;
            for (var block = 0; block < 128; block++)
            {
                var header = block == 0 ? head.AsSpan(4, 4).ToArray() : await ReadAsync(stream, offset, 4);
                if (header.Length < 4) return null;
                if ((header[0] & 0x7F) == 3) hasSeekTable = true;
                offset += 4 + ((header[1] << 16) | (header[2] << 8) | header[3]);
                if (offset >= size) return null;
                if ((header[0] & 0x80) != 0) return new FlacFrameLocator(stream, offset, size, rate, minBlock, maxBlock, total, hasSeekTable);
            }
        }
        catch
        {
        }
        return null;
    }

    public bool TryLocate(long targetSample, out long frameSample, out long frameOffset)
    {
        frameSample = 0;
        frameOffset = AudioStart;
        if (TotalSamples <= 0 || Size <= AudioStart) return false;
        targetSample = Math.Clamp(targetSample, 0, TotalSamples - 1);
        long lowOffset = AudioStart, lowSample = 0, highOffset = Size, highSample = TotalSamples;
        try
        {
            for (var step = 0; step < 48; step++)
            {
                var estimate = lowOffset + (long)((targetSample - lowSample) / (double)(highSample - lowSample) * (highOffset - lowOffset));
                var guess = estimate - Window / 4;
                if (highOffset - lowOffset <= Window || guess <= lowOffset)
                {
                    if (Walk(lowOffset, lowSample, targetSample, out frameSample, out frameOffset)) return true;
                    if (frameOffset == lowOffset) return false;
                    lowOffset = frameOffset;
                    lowSample = frameSample;
                    continue;
                }
                guess = Math.Min(guess, highOffset - 1);
                var length = Read(guess, Window);
                if (!FindFrame(length, guess + length >= Size, lowSample, highSample, out var position, out var sample))
                {
                    highOffset = guess;
                    continue;
                }
                if (sample <= targetSample)
                {
                    lowOffset = guess + position;
                    lowSample = sample;
                }
                else
                {
                    highOffset = guess + position;
                    highSample = sample;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private bool Walk(long startOffset, long startSample, long targetSample, out long frameSample, out long frameOffset)
    {
        frameSample = startSample;
        frameOffset = startOffset;
        var length = Read(startOffset, Window);
        if (!TryParseHeader(length, 0, out var sample, out var blockSize) || sample != startSample) return false;
        var position = 0;
        while (true)
        {
            var next = -1;
            for (var candidate = position + MaxHeaderBytes; candidate + MaxHeaderBytes <= length; candidate++)
            {
                if (_buffer[candidate] != 0xFF || (_buffer[candidate + 1] & 0xFE) != 0xF8) continue;
                if (!TryParseHeader(length, candidate, out var nextSample, out var nextBlock) || nextSample != sample + blockSize) continue;
                next = candidate;
                if (nextSample > targetSample) return true;
                sample = nextSample;
                blockSize = nextBlock;
                break;
            }
            if (next < 0) return startOffset + length >= Size;
            position = next;
            frameSample = sample;
            frameOffset = startOffset + position;
        }
    }

    private bool FindFrame(int length, bool atEnd, long lowSample, long highSample, out int position, out long sample)
    {
        position = 0;
        sample = 0;
        for (var candidate = 0; candidate + MaxHeaderBytes <= length; candidate++)
        {
            if (_buffer[candidate] != 0xFF || (_buffer[candidate + 1] & 0xFE) != 0xF8) continue;
            if (!TryParseHeader(length, candidate, out var first, out var blockSize)) continue;
            if (first <= lowSample || first >= highSample) continue;
            var confirmed = false;
            var reachedEnd = true;
            for (var following = candidate + MaxHeaderBytes; following + MaxHeaderBytes <= length; following++)
            {
                if (_buffer[following] != 0xFF || (_buffer[following + 1] & 0xFE) != 0xF8) continue;
                if (!TryParseHeader(length, following, out var second, out _)) continue;
                reachedEnd = false;
                confirmed = second == first + blockSize;
                break;
            }
            if (!confirmed && !(reachedEnd && atEnd)) continue;
            position = candidate;
            sample = first;
            return true;
        }
        return false;
    }

    private bool TryParseHeader(int length, int position, out long firstSample, out int blockSize)
    {
        firstSample = 0;
        blockSize = 0;
        if (position < 0 || position + MaxHeaderBytes > length) return false;
        var data = _buffer;
        if (data[position] != 0xFF || (data[position + 1] & 0xFE) != 0xF8) return false;
        var variable = (data[position + 1] & 1) != 0;
        var blockCode = data[position + 2] >> 4;
        var rateCode = data[position + 2] & 0x0F;
        var channels = data[position + 3] >> 4;
        var depthCode = (data[position + 3] >> 1) & 7;
        if (blockCode == 0 || rateCode == 0x0F || channels > 10 || depthCode == 3 || depthCode == 7 || (data[position + 3] & 1) != 0) return false;

        var cursor = position + 4;
        long number = data[cursor];
        var extra = 0;
        if (number >= 0x80)
        {
            var mask = 0x80;
            var bytes = 0;
            while ((number & mask) != 0)
            {
                bytes++;
                mask >>= 1;
            }
            if (bytes < 2 || bytes > 7) return false;
            number &= mask - 1;
            extra = bytes - 1;
            for (var i = 1; i <= extra; i++)
            {
                var next = data[cursor + i];
                if ((next & 0xC0) != 0x80) return false;
                number = (number << 6) | (uint)(next & 0x3F);
            }
        }
        cursor += 1 + extra;

        switch (blockCode)
        {
            case 1: blockSize = 192; break;
            case <= 5: blockSize = 576 << (blockCode - 2); break;
            case 6: blockSize = data[cursor] + 1; cursor += 1; break;
            case 7: blockSize = ((data[cursor] << 8) | data[cursor + 1]) + 1; cursor += 2; break;
            default: blockSize = 256 << (blockCode - 8); break;
        }
        if (rateCode == 12) cursor += 1;
        else if (rateCode is 13 or 14) cursor += 2;
        if (cursor >= length) return false;

        byte crc = 0;
        for (var i = position; i < cursor; i++) crc = Crc8[crc ^ data[i]];
        if (crc != data[cursor]) return false;

        if (variable)
        {
            firstSample = number;
            return true;
        }
        if (_fixedBlockSize == 0) return false;
        firstSample = number * _fixedBlockSize;
        return true;
    }

    private int Read(long offset, int count)
    {
        count = (int)Math.Min(count, Size - offset);
        var filled = 0;
        while (filled < count)
        {
            using var input = _stream.GetInputStreamAt((ulong)(offset + filled));
            var result = input.ReadAsync(_buffer.AsBuffer(filled, count - filled), (uint)(count - filled), InputStreamOptions.None).AsTask().GetAwaiter().GetResult();
            var read = (int)result.Length;
            if (read <= 0) break;
            result.CopyTo(0, _buffer, filled, read);
            filled += read;
        }
        return filled;
    }

    private static async Task<byte[]> ReadAsync(IRandomAccessStream stream, long offset, int count)
    {
        var bytes = new byte[count];
        using var input = stream.GetInputStreamAt((ulong)offset);
        var result = await input.ReadAsync(bytes.AsBuffer(), (uint)count, InputStreamOptions.None);
        var read = (int)result.Length;
        if (read > 0) result.CopyTo(0, bytes, 0, read);
        return read == count ? bytes : bytes.AsSpan(0, read).ToArray();
    }

    private static byte[] BuildCrc8()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 0x80) != 0 ? ((value << 1) ^ 0x07) & 0xFF : (value << 1) & 0xFF;
            table[i] = (byte)value;
        }
        return table;
    }
}
