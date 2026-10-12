#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace HyPlayer.Platform.Playback.MediaSources;

public static class RechunkedAudioSource
{
    private const uint MfVersion = 0x00020070;
    private const uint FirstAudioStream = 0xFFFFFFFD;
    private const uint MediaSourceIndex = 0xFFFFFFFF;
    private const uint EndOfStreamFlag = 0x2;
    private const ushort VtI8 = 20;
    private const double ChunkSeconds = 0.04;
    private const double MinimumFrameSeconds = 0.0105;

    private static readonly Guid MajorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid SubtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid AudioMajorType = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid ChannelsKey = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid SampleRateKey = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid DurationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public long Value;
    }

    private static unsafe delegate* unmanaged[Stdcall]<uint, uint, int> _startup;
    private static unsafe delegate* unmanaged[Stdcall]<void**, int> _createMediaType;
    private static unsafe delegate* unmanaged[Stdcall]<void*, void**, int> _byteStreamOnStream;
    private static unsafe delegate* unmanaged[Stdcall]<void*, void*, void**, int> _createReader;
    private static int _initialized;

    private static unsafe bool EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) != 0) return _initialized > 0;
        lock (typeof(RechunkedAudioSource))
        {
            if (_initialized != 0) return _initialized > 0;
            try
            {
                if (!NativeLibrary.TryLoad("mfplat.dll", out var plat) || !NativeLibrary.TryLoad("mfreadwrite.dll", out var readwrite))
                {
                    _initialized = -1;
                    return false;
                }
                _startup = (delegate* unmanaged[Stdcall]<uint, uint, int>)NativeLibrary.GetExport(plat, "MFStartup");
                _createMediaType = (delegate* unmanaged[Stdcall]<void**, int>)NativeLibrary.GetExport(plat, "MFCreateMediaType");
                _byteStreamOnStream = (delegate* unmanaged[Stdcall]<void*, void**, int>)NativeLibrary.GetExport(plat, "MFCreateMFByteStreamOnStreamEx");
                _createReader = (delegate* unmanaged[Stdcall]<void*, void*, void**, int>)NativeLibrary.GetExport(readwrite, "MFCreateSourceReaderFromByteStream");
                _initialized = _startup(MfVersion, 0) >= 0 ? 1 : -1;
            }
            catch
            {
                _initialized = -1;
            }
            return _initialized > 0;
        }
    }

    public static async Task<MediaSource> FromFileAsync(Windows.Storage.StorageFile file)
    {
        try
        {
            var stream = await file.OpenReadAsync();
            if (await TryCreateLocalAsync(stream) is { } rechunked) return rechunked;
            stream.Dispose();
        }
        catch
        {
        }
        return MediaSource.CreateFromStorageFile(file);
    }

    public static async Task<MediaSource> FromStreamAsync(IRandomAccessStream stream, Func<MediaSource> fallback)
    {
        if (await TryCreateLocalAsync(stream) is { } rechunked) return rechunked;
        return fallback();
    }

    private static async Task<MediaSource?> TryCreateLocalAsync(IRandomAccessStream stream)
    {
        var locator = await FlacFrameLocator.OpenAsync(stream);
        if (locator is null) return null;
        var shortFrames = locator.MaxBlockSize / (double)locator.SampleRate < MinimumFrameSeconds;
        if (!shortFrames && locator.HasSeekTable) return null;
        return TryCreate(stream, locator.HasSeekTable ? null : locator);
    }

    public static async Task<MediaSource> FromDownloadAsync(Windows.Networking.BackgroundTransfer.DownloadOperation operation)
    {
        if (operation.IsRandomAccessRequired)
        {
            try
            {
                var stream = await operation.GetResultRandomAccessStreamReference().OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                if (await HasShortFramesAsync(stream).WaitAsync(TimeSpan.FromSeconds(3)) && TryCreate(stream) is { } rechunked) return rechunked;
                stream.Dispose();
            }
            catch
            {
            }
        }
        return MediaSource.CreateFromDownloadOperation(operation);
    }

    public static async Task<bool> HasShortFramesAsync(IRandomAccessStream stream)
    {
        try
        {
            var header = new byte[42];
            using var input = stream.GetInputStreamAt(0);
            var buffer = await input.ReadAsync(header.AsBuffer(), (uint)header.Length, InputStreamOptions.None);
            if (buffer.Length < 42) return false;
            buffer.CopyTo(0, header, 0, 42);
            return HasShortFrames(header);
        }
        catch
        {
            return false;
        }
    }

    public static bool HasShortFrames(ReadOnlySpan<byte> header)
    {
        if (header.Length < 42 || header[0] != (byte)'f' || header[1] != (byte)'L' || header[2] != (byte)'a' || header[3] != (byte)'C') return false;
        if ((header[4] & 0x7F) != 0) return false;
        var info = header[8..];
        var maxBlock = (info[2] << 8) | info[3];
        var rate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
        return rate > 0 && maxBlock > 0 && maxBlock / (double)rate < MinimumFrameSeconds;
    }

    public static MediaSource? TryCreate(IRandomAccessStream stream, FlacFrameLocator? locator = null)
    {
        if (!EnsureInitialized()) return null;
        var decoder = Decoder.TryOpen(stream, locator);
        if (decoder is null) return null;

        var properties = AudioEncodingProperties.CreatePcm(decoder.SampleRate, decoder.Channels, 32);
        properties.Subtype = MediaEncodingSubtypes.Float;
        var mss = new MediaStreamSource(new AudioStreamDescriptor(properties))
        {
            CanSeek = true,
            BufferTime = TimeSpan.Zero,
            Duration = decoder.Duration
        };
        mss.Starting += (sender, args) =>
        {
            var start = args.Request.StartPosition;
            if (start is { } position)
            {
                decoder.Seek(position);
                args.Request.SetActualStartPosition(position);
            }
        };
        mss.SampleRequested += (sender, args) =>
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            _ = Task.Run(() =>
            {
                try
                {
                    request.Sample = decoder.ReadChunk();
                }
                catch
                {
                    request.Sample = null;
                }
                finally
                {
                    deferral.Complete();
                }
            });
        };
        mss.Closed += (sender, args) => decoder.Dispose();
        return MediaSource.CreateFromMediaStreamSource(mss);
    }

    private sealed unsafe class Decoder : IDisposable
    {
        private void* _reader;
        private readonly FlacFrameLocator? _locator;
        private readonly object _lock = new();
        private readonly int _bytesPerFrame;
        private readonly int _chunkBytes;
        private byte[] _pending = [];
        private int _pendingLength;
        private TimeSpan _nextTimestamp;
        private bool _ended;
        private bool _started;

        public uint SampleRate { get; }
        public uint Channels { get; }
        public TimeSpan Duration { get; }

        private Decoder(void* reader, uint rate, uint channels, TimeSpan duration, FlacFrameLocator? locator)
        {
            _reader = reader;
            _locator = locator is not null && locator.SampleRate == rate && duration > TimeSpan.Zero ? locator : null;
            SampleRate = rate;
            Channels = channels;
            Duration = duration;
            _bytesPerFrame = (int)channels * 4;
            _chunkBytes = (int)(rate * ChunkSeconds) * _bytesPerFrame;
            _pending = new byte[_chunkBytes * 2];
        }

        public static Decoder? TryOpen(IRandomAccessStream stream, FlacFrameLocator? locator)
        {
            void* unknown = null, byteStream = null, reader = null, type = null, current = null;
            try
            {
                unknown = (void*)WinRT.MarshalInspectable<object>.FromManaged(stream);
                if (_byteStreamOnStream(unknown, &byteStream) < 0 || byteStream is null) return null;
                if (_createReader(byteStream, null, &reader) < 0 || reader is null) return null;
                if (_createMediaType(&type) < 0 || type is null) return null;
                var major = AudioMajorType;
                var sub = FloatSubtype;
                var majorKey = MajorTypeKey;
                var subKey = SubtypeKey;
                SetGuid(type, &majorKey, &major);
                SetGuid(type, &subKey, &sub);
                if (Call(reader, 7, FirstAudioStream, null, type) < 0) return null;
                if (((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)VTable(reader)[6])(reader, FirstAudioStream, &current) < 0 || current is null) return null;
                uint rate = 0, channels = 0;
                var rateKey = SampleRateKey;
                var channelsKey = ChannelsKey;
                ((delegate* unmanaged[Stdcall]<void*, Guid*, uint*, int>)VTable(current)[7])(current, &rateKey, &rate);
                ((delegate* unmanaged[Stdcall]<void*, Guid*, uint*, int>)VTable(current)[7])(current, &channelsKey, &channels);
                if (rate == 0 || channels == 0) return null;

                PropVariant duration = default;
                var durationKey = DurationKey;
                ((delegate* unmanaged[Stdcall]<void*, uint, Guid*, PropVariant*, int>)VTable(reader)[12])(reader, MediaSourceIndex, &durationKey, &duration);
                var decoder = new Decoder(reader, rate, channels, TimeSpan.FromTicks(duration.Value), locator);
                reader = null;
                return decoder;
            }
            catch
            {
                return null;
            }
            finally
            {
                Release(current);
                Release(type);
                Release(reader);
                Release(byteStream);
                if (unknown is not null) Marshal.Release((nint)unknown);
            }
        }

        public void Seek(TimeSpan position)
        {
            lock (_lock)
            {
                if (_reader is null) return;
                if (!_started) ReadSample();
                var request = position;
                long skip = 0;
                if (_locator is not null)
                {
                    var target = (long)(position.TotalSeconds * SampleRate);
                    if (_locator.TryLocate(target, out var frameSample, out var frameOffset))
                    {
                        var span = (double)(_locator.Size - _locator.AudioStart);
                        request = TimeSpan.FromTicks((long)(Math.Max(0, frameOffset - 8 - _locator.AudioStart) / span * Duration.Ticks));
                        skip = Math.Max(0, target - frameSample);
                    }
                }
                var variant = new PropVariant { Type = VtI8, Value = request.Ticks };
                var empty = Guid.Empty;
                ((delegate* unmanaged[Stdcall]<void*, Guid*, PropVariant*, int>)VTable(_reader)[8])(_reader, &empty, &variant);
                _pendingLength = 0;
                _ended = false;
                while (skip > 0 && !_ended)
                {
                    var available = _pendingLength / _bytesPerFrame;
                    if (available == 0)
                    {
                        ReadSample();
                        continue;
                    }
                    var drop = (int)Math.Min(available, skip);
                    var bytes = drop * _bytesPerFrame;
                    System.Buffer.BlockCopy(_pending, bytes, _pending, 0, _pendingLength - bytes);
                    _pendingLength -= bytes;
                    skip -= drop;
                }
                _nextTimestamp = position;
            }
        }

        public MediaStreamSample? ReadChunk()
        {
            lock (_lock)
            {
                if (_reader is null) return null;
                while (_pendingLength < _chunkBytes && !_ended) ReadSample();
                if (_pendingLength == 0) return null;
                var take = Math.Min(_chunkBytes, _pendingLength);
                take -= take % _bytesPerFrame;
                var chunk = new byte[take];
                System.Buffer.BlockCopy(_pending, 0, chunk, 0, take);
                System.Buffer.BlockCopy(_pending, take, _pending, 0, _pendingLength - take);
                _pendingLength -= take;
                var duration = TimeSpan.FromTicks((long)(take / (double)_bytesPerFrame / SampleRate * TimeSpan.TicksPerSecond));
                var sample = MediaStreamSample.CreateFromBuffer(chunk.AsBuffer(), _nextTimestamp);
                sample.Duration = duration;
                _nextTimestamp += duration;
                return sample;
            }
        }

        private void ReadSample()
        {
            uint actual = 0, flags = 0;
            long timestamp = 0;
            void* sample = null;
            _started = true;
            var hr = ((delegate* unmanaged[Stdcall]<void*, uint, uint, uint*, uint*, long*, void**, int>)VTable(_reader)[9])(
                _reader, FirstAudioStream, 0, &actual, &flags, &timestamp, &sample);
            if (hr < 0 || (flags & EndOfStreamFlag) != 0)
            {
                _ended = true;
                Release(sample);
                return;
            }
            if (sample is null) return;
            void* buffer = null;
            try
            {
                if (((delegate* unmanaged[Stdcall]<void*, void**, int>)VTable(sample)[41])(sample, &buffer) < 0 || buffer is null) return;
                byte* data = null;
                uint max = 0, length = 0;
                if (((delegate* unmanaged[Stdcall]<void*, byte**, uint*, uint*, int>)VTable(buffer)[3])(buffer, &data, &max, &length) < 0) return;
                try
                {
                    if (_pendingLength == 0 && _locator is null) _nextTimestamp = TimeSpan.FromTicks(timestamp);
                    if (_pendingLength + (int)length > _pending.Length) Array.Resize(ref _pending, Math.Max(_pending.Length * 2, _pendingLength + (int)length));
                    new ReadOnlySpan<byte>(data, (int)length).CopyTo(_pending.AsSpan(_pendingLength));
                    _pendingLength += (int)length;
                }
                finally
                {
                    ((delegate* unmanaged[Stdcall]<void*, int>)VTable(buffer)[4])(buffer);
                }
            }
            finally
            {
                Release(buffer);
                Release(sample);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Release(_reader);
                _reader = null;
            }
        }
    }

    private static unsafe void** VTable(void* instance) => *(void***)instance;

    private static unsafe int Call(void* instance, int slot, uint stream, void* reserved, void* type) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void*, void*, int>)VTable(instance)[slot])(instance, stream, reserved, type);

    private static unsafe void SetGuid(void* attributes, Guid* key, Guid* value) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, Guid*, int>)VTable(attributes)[24])(attributes, key, value);

    private static unsafe void Release(void* instance)
    {
        if (instance is not null) ((delegate* unmanaged[Stdcall]<void*, uint>)VTable(instance)[2])(instance);
    }
}
