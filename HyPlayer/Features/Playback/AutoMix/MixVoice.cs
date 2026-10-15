using System;
using HyPlayer.UWP.Chopin.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public sealed class MixRegion
{
    public required float[] Samples { get; init; }
    public required int SampleRate { get; init; }
    public required double StartSeconds { get; init; }
    public int Frames => Samples.Length / 2;
    public double EndSeconds => StartSeconds + Frames / (double)SampleRate;
    public long IndexOf(double contentSeconds) => (long)Math.Round((contentSeconds - StartSeconds) * SampleRate);
}

public sealed class MixVoice : IDisposable
{
    private readonly MixRegion _region;
    private readonly int _sampleRate;
    private TimeStretcher? _stretcher;
    private long _feedIndex;
    private double _rateCarry;
    private float[] _input = new float[8192];
    private float[] _stretched = new float[8192];
    private float[] _raw = new float[8192];
    private readonly StereoBiquad _highPass = new();
    private readonly StereoBiquad _lowShelf = new();
    private double _gain;
    private double _send;
    private double _wet = 1;
    private MixSend? _sendPath;
    private int _rawBlendFrames;
    private int _rawBlendDone = -1;
    private bool _idle;
    private int _latency;
    private int _latencyOut;
    private long _unity;
    private long _engageIndex;
    private int _engageFrames;
    private int _engageDone;

    public MixVoice(MixRegion region, double startContentSeconds, bool useStretcher, double seekRate = 1)
    {
        _region = region;
        _sampleRate = region.SampleRate;
        ContentIndex = region.IndexOf(startContentSeconds);
        if (useStretcher && TimeStretcher.IsAvailable)
        {
            _stretcher = new TimeStretcher(2, _sampleRate);
            Seek(seekRate);
        }
    }

    public double ContentIndex { get; private set; }
    public double ContentSeconds => _region.StartSeconds + ContentIndex / _sampleRate;
    public bool IsStretching => _stretcher is not null && !_idle && _rawBlendDone < 0;
    public int StretchLatency => _latencyOut;
    public bool Settled => _stretcher is not null && !_idle && _unity >= _latencyOut;
    public double Rate { get; set; } = 1;
    public double TargetGain { get; set; }
    public double HighPassHz { get; set; }
    public double LowShelfDb { get; set; }
    public double SendGain { get; set; }
    public double WetGain { get; set; } = 1;
    public bool Finished => ContentIndex >= _region.Frames - 1;
    public bool TailActive => _sendPath is { Active: true };

    public void EnableSend(MixSendSettings settings, double wetGain)
    {
        _sendPath = new MixSend(_sampleRate, settings);
        _wet = wetGain;
        WetGain = wetGain;
    }

    public void BeginTail(long maxFrames)
    {
        _sendPath?.Release(maxFrames);
    }

    public void RenderTail(Span<float> mix, int frames)
    {
        _sendPath?.Process(default, frames, 0, 0, mix, _wet, WetGain);
        _wet = WetGain;
    }

    public void SetGainImmediate(double gain)
    {
        _gain = gain;
        TargetGain = gain;
    }

    public void PrepareStretch()
    {
        if (_stretcher is not null || !TimeStretcher.IsAvailable) return;
        _stretcher = new TimeStretcher(2, _sampleRate);
        _idle = true;
        _latency = _stretcher.OutputSeekLength(1);
        _latencyOut = _latency - _stretcher.OutputSeekLength(0);
    }

    public bool BeginStretch(double blendSeconds)
    {
        if (_stretcher is null || !_idle) return false;
        ContentIndex = Math.Round(ContentIndex);
        Seek(1);
        _rateCarry = 0;
        _unity = 0;
        _idle = false;
        _engageIndex = (long)ContentIndex;
        _engageFrames = Math.Max(1, (int)(blendSeconds * _sampleRate));
        _engageDone = 0;
        return true;
    }

    private void Seek(double rate)
    {
        var seek = _stretcher!.OutputSeekLength(rate);
        var start = (long)Math.Clamp(ContentIndex, 0, _region.Frames);
        var length = (int)Math.Min(seek, _region.Frames - start);
        if (length > 0) _stretcher.OutputSeek(new ReadOnlySpan<float>(_region.Samples, (int)start * 2, length * 2));
        _feedIndex = start + length;
        _latency = _stretcher.OutputSeekLength(1);
        _latencyOut = _latency - _stretcher.OutputSeekLength(0);
    }

    public void BeginRawBlend(double seconds, bool settled = false)
    {
        if (_stretcher is null || _rawBlendDone >= 0) return;
        if (settled && Settled) ContentIndex = _feedIndex - _latency;
        _rawBlendFrames = Math.Max(1, (int)(seconds * _sampleRate));
        _rawBlendDone = 0;
        Rate = 1;
    }

    public void Render(Span<float> mix, int frames, ReadOnlySpan<float> envelope = default)
    {
        if (frames <= 0) return;
        EnsureCapacity(frames);
        var dry = new Span<float>(_raw, 0, frames * 2);

        if (IsStretching || _rawBlendDone >= 0 && _rawBlendDone < _rawBlendFrames && _stretcher is not null)
        {
            var stretched = new Span<float>(_stretched, 0, frames * 2);
            var wanted = frames * Rate + _rateCarry;
            var inputFrames = (int)Math.Floor(wanted);
            _rateCarry = wanted - inputFrames;
            var available = (int)Math.Clamp(_region.Frames - _feedIndex, 0, inputFrames);
            var input = new Span<float>(_input, 0, inputFrames * 2);
            input.Clear();
            if (available > 0) new ReadOnlySpan<float>(_region.Samples, (int)_feedIndex * 2, available * 2).CopyTo(input);
            _stretcher!.Process(input, stretched);
            _feedIndex += inputFrames;
            _unity = Rate == 1 ? _unity + frames : 0;

            if (_rawBlendDone >= 0)
            {
                ReadRaw(dry, frames, ContentIndex);
                for (var i = 0; i < frames; i++)
                {
                    var w = Math.Min(1.0, (_rawBlendDone + i) / (double)_rawBlendFrames);
                    dry[i * 2] = (float)(stretched[i * 2] * (1 - w) + dry[i * 2] * w);
                    dry[i * 2 + 1] = (float)(stretched[i * 2 + 1] * (1 - w) + dry[i * 2 + 1] * w);
                }
                _rawBlendDone += frames;
                ContentIndex += frames;
                if (_rawBlendDone >= _rawBlendFrames)
                {
                    _stretcher.Dispose();
                    _stretcher = null;
                }
            }
            else if (_engageDone < _engageFrames)
            {
                ReadRaw(dry, frames, _engageIndex + _engageDone);
                for (var i = 0; i < frames; i++)
                {
                    var w = Math.Min(1.0, (_engageDone + i + 1) / (double)_engageFrames);
                    dry[i * 2] = (float)(dry[i * 2] * (1 - w) + stretched[i * 2] * w);
                    dry[i * 2 + 1] = (float)(dry[i * 2 + 1] * (1 - w) + stretched[i * 2 + 1] * w);
                }
                _engageDone += frames;
                ContentIndex += frames * Rate;
            }
            else
            {
                stretched.CopyTo(dry);
                ContentIndex += frames * Rate;
            }
        }
        else
        {
            ReadRaw(dry, frames, ContentIndex);
            ContentIndex += frames;
        }

        _highPass.SetHighPass(HighPassHz, _sampleRate);
        if (HighPassHz <= 20) _highPass.SetBypass();
        _highPass.Process(dry, frames);
        _lowShelf.SetLowShelf(180, LowShelfDb, _sampleRate);
        _lowShelf.Process(dry, frames);
        if (_sendPath is { } send)
        {
            send.Process(dry, frames, _send, SendGain, mix, _wet, WetGain);
            _send = SendGain;
            _wet = WetGain;
        }

        var startGain = _gain;
        var endGain = TargetGain;
        var shaped = envelope.Length >= frames;
        for (var i = 0; i < frames; i++)
        {
            var g = startGain + (endGain - startGain) * Math.Min(1, (i + 1) / (double)frames);
            if (shaped) g *= envelope[i];
            mix[i * 2] += (float)(dry[i * 2] * g);
            mix[i * 2 + 1] += (float)(dry[i * 2 + 1] * g);
        }
        _gain = endGain;
    }

    private void ReadRaw(Span<float> destination, int frames, double index)
    {
        var start = (long)Math.Round(index);
        for (var i = 0; i < frames; i++)
        {
            var src = start + i;
            if (src < 0 || src >= _region.Frames)
            {
                destination[i * 2] = 0;
                destination[i * 2 + 1] = 0;
                continue;
            }
            destination[i * 2] = _region.Samples[src * 2];
            destination[i * 2 + 1] = _region.Samples[src * 2 + 1];
        }
    }

    private void EnsureCapacity(int frames)
    {
        var needed = (int)(frames * 2 * Math.Max(2, Rate + 1));
        if (_input.Length < needed) _input = new float[needed];
        if (_stretched.Length < frames * 2) _stretched = new float[frames * 4];
        if (_raw.Length < frames * 2) _raw = new float[frames * 4];
    }

    public void Dispose()
    {
        _stretcher?.Dispose();
        _stretcher = null;
    }
}
