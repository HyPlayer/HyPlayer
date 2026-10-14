#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
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
    private const int DecodeAheadChunks = 10;
    private static readonly TimeSpan BufferLead = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(20);

    private static readonly Guid MajorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid SubtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid AudioMajorType = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid ChannelsKey = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid SampleRateKey = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid DurationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private static readonly Guid BufferByteAccessId = new("905a0fef-bc53-11df-8c49-001e4fc686da");

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
            if (await TryCreateLocalAsync(stream, true) is { } rechunked) return rechunked;
            stream.Dispose();
        }
        catch
        {
        }
        return MediaSource.CreateFromStorageFile(file);
    }

    public static async Task<MediaSource> FromStreamAsync(IRandomAccessStream stream, Func<MediaSource> fallback)
    {
        if (await TryCreateLocalAsync(stream, false) is { } rechunked) return rechunked;
        return fallback();
    }

    private static async Task<MediaSource?> TryCreateLocalAsync(IRandomAccessStream stream, bool ownsStream)
    {
        var locator = await FlacFrameLocator.OpenAsync(stream);
        if (locator is null) return null;
        var shortFrames = locator.MaxBlockSize / (double)locator.SampleRate < MinimumFrameSeconds;
        if (!shortFrames && locator.HasSeekTable) return null;
        return Create(stream, locator.HasSeekTable ? null : locator, ownsStream);
    }

    public static async Task<MediaSource> FromDownloadAsync(Windows.Networking.BackgroundTransfer.DownloadOperation operation)
    {
        if (operation.IsRandomAccessRequired && await TryCreateProgressiveAsync(operation) is { } rechunked) return rechunked;
        return MediaSource.CreateFromDownloadOperation(operation);
    }

    private static async Task<MediaSource?> TryCreateProgressiveAsync(Windows.Networking.BackgroundTransfer.DownloadOperation operation)
    {
        var opening = operation.GetResultRandomAccessStreamReference().OpenReadAsync().AsTask();
        Task<MediaSource?>? creating = null;
        Task<bool>? probing = null;
        try
        {
            var deadline = Task.Delay(HeaderTimeout);
            if (await Task.WhenAny(opening, deadline) != opening) throw new TimeoutException("the download stream did not open in time");
            var stream = await opening;
            probing = HasShortFramesAsync(stream);
            if (await Task.WhenAny(probing, deadline) != probing) throw new TimeoutException("the FLAC header did not arrive in time");
            if (await probing)
            {
                creating = Task.Run(() => Create(stream, null, true));
                if (await Task.WhenAny(creating, deadline) != creating) throw new TimeoutException("the FLAC metadata did not arrive in time");
                if (await creating is { } source) return source;
                throw new InvalidOperationException("the stream could not be opened for decoding");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Progressive FLAC setup fell back: {ex.Message}");
        }
        // Opening may already be complete while the decoder is still using its stream.
        // Retire the stream only after every operation using it has settled.
        new HyPlayer.Platform.Runtime.Background.BackgroundTaskRunner().Forget(
            DeferredStreamCleanup.ReleaseAsync(opening, probing, creating), "retire timed-out FLAC stream");
        return null;
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

    public static MediaSource? TryCreate(IRandomAccessStream stream, FlacFrameLocator? locator = null) =>
        Create(stream, locator, false);

    private static MediaSource? Create(IRandomAccessStream stream, FlacFrameLocator? locator, bool ownsStream)
    {
        if (!EnsureInitialized()) return null;
        var decoder = Decoder.TryOpen(stream, locator, ownsStream);
        if (decoder is null) return null;

        var properties = AudioEncodingProperties.CreatePcm(decoder.SampleRate, decoder.Channels, 32);
        properties.Subtype = MediaEncodingSubtypes.Float;
        var mss = new MediaStreamSource(new AudioStreamDescriptor(properties))
        {
            CanSeek = true,
            BufferTime = BufferLead,
            Duration = decoder.Duration
        };
        var chunkBytes = decoder.ChunkBytes;
        DecodeAheadPump pump = null!;
        pump = new DecodeAheadPump(decoder, DecodeAheadChunks, () => new SampleChunk(chunkBytes, pump));
        mss.Starting += (sender, args) =>
        {
            var request = args.Request;
            if (request.StartPosition is not { } position) return;
            var deferral = request.GetDeferral();
            pump.Seek(position, () =>
            {
                try
                {
                    request.SetActualStartPosition(position);
                }
                catch
                {
                }
                Complete(deferral.Complete);
            });
        };
        mss.SampleRequested += (sender, args) =>
        {
            var request = args.Request;
            if (pump.TryTake(out var ready))
            {
                Deliver(request, pump, ready);
                return;
            }
            var deferral = request.GetDeferral();
            pump.WhenReady(chunk =>
            {
                Deliver(request, pump, chunk);
                Complete(deferral.Complete);
            });
        };
        mss.Closed += (sender, args) => pump.Dispose();
        return MediaSource.CreateFromMediaStreamSource(mss);
    }

    private static void Deliver(MediaStreamSourceSampleRequest request, DecodeAheadPump pump, DecodedChunk? chunk)
    {
        try
        {
            if (chunk is SampleChunk sample) request.Sample = sample.ToSample();
        }
        catch
        {
        }
    }

    private static void Complete(Action complete)
    {
        try
        {
            complete();
        }
        catch
        {
        }
    }

    private sealed unsafe class SampleChunk : DecodedChunk
    {
        private readonly Windows.Storage.Streams.Buffer _buffer;
        private readonly TypedEventHandler<MediaStreamSample, object> _processed;
        private readonly int _pressure;
        private readonly DecodeAheadPump _pump;
        private MediaStreamSample? _sample;

        public SampleChunk(int capacity, DecodeAheadPump pump)
        {
            _buffer = new Windows.Storage.Streams.Buffer((uint)capacity);
            Interlocked.Increment(ref DecodeBufferStatistics.Created);
            GC.AddMemoryPressure(capacity);
            _pressure = capacity;
            _pump = pump;
            _processed = (sender, args) =>
            {
                _sample = null;
                pump.Return(this);
            };
            var unknown = WinRT.MarshalInspectable<object>.FromManaged(_buffer);
            try
            {
                var id = BufferByteAccessId;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in id, out var access));
                try
                {
                    byte* data = null;
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<void*, byte**, int>)VTable((void*)access)[3])((void*)access, &data));
                    Data = (nint)data;
                }
                finally
                {
                    Marshal.Release(access);
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        ~SampleChunk()
        {
            if (_pressure > 0) GC.RemoveMemoryPressure(_pressure);
        }

        public MediaStreamSample ToSample()
        {
            _buffer.Length = (uint)Length;
            var sample = MediaStreamSample.CreateFromBuffer(_buffer, Timestamp);
            sample.Duration = Duration;
            try
            {
                sample.Processed += _processed;
                _sample = sample;
                _pump.Lend(this);
            }
            catch
            {
            }
            return sample;
        }
    }

    private sealed unsafe class Decoder : IChunkDecoder
    {
        private void* _reader;
        private readonly IRandomAccessStream? _stream;
        private readonly FlacFrameLocator? _locator;
        private readonly int _bytesPerFrame;
        private byte[] _pending = [];
        private int _pendingLength;
        private TimeSpan _nextTimestamp;
        private bool _ended;
        private bool _started;
        private int _failure;

        public uint SampleRate { get; }
        public uint Channels { get; }
        public TimeSpan Duration { get; }
        public int ChunkBytes { get; }

        private Decoder(void* reader, IRandomAccessStream? stream, uint rate, uint channels, TimeSpan duration, FlacFrameLocator? locator)
        {
            _reader = reader;
            _stream = stream;
            _locator = locator is not null && locator.SampleRate == rate && duration > TimeSpan.Zero ? locator : null;
            SampleRate = rate;
            Channels = channels;
            Duration = duration;
            _bytesPerFrame = (int)channels * 4;
            ChunkBytes = (int)(rate * ChunkSeconds) * _bytesPerFrame;
            _pending = new byte[ChunkBytes * 2];
        }

        ~Decoder()
        {
            Release(_reader);
        }

        public static Decoder? TryOpen(IRandomAccessStream stream, FlacFrameLocator? locator, bool ownsStream)
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
                var decoder = new Decoder(reader, ownsStream ? stream : null, rate, channels, TimeSpan.FromTicks(duration.Value), locator);
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
            _failure = 0;
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

        public int ReadChunk(nint destination, out TimeSpan timestamp, out TimeSpan duration)
        {
            timestamp = default;
            duration = default;
            if (_reader is null) return 0;
            while (_pendingLength < ChunkBytes && !_ended) ReadSample();
            var take = Math.Min(ChunkBytes, _pendingLength);
            take -= take % _bytesPerFrame;
            if (take == 0)
            {
                if (_failure < 0) Marshal.ThrowExceptionForHR(_failure);
                return 0;
            }
            new ReadOnlySpan<byte>(_pending, 0, take).CopyTo(new Span<byte>((void*)destination, take));
            System.Buffer.BlockCopy(_pending, take, _pending, 0, _pendingLength - take);
            _pendingLength -= take;
            timestamp = _nextTimestamp;
            duration = TimeSpan.FromTicks((long)(take / (double)_bytesPerFrame / SampleRate * TimeSpan.TicksPerSecond));
            _nextTimestamp += duration;
            return take;
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
                if (hr < 0) _failure = hr;
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

        public void Interrupt()
        {
            try
            {
                _stream?.Dispose();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            Release(_reader);
            _reader = null;
            GC.SuppressFinalize(this);
            Interrupt();
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
