// Port of DynamicSTFT<float, false, STFT_SPECTRUM_MODIFIED> and its Kaiser window.
// Copyright Signalsmith Audio; MIT, see LICENSE.txt.
using System;

namespace HyPlayer.UWP.Chopin.Dsp.Signalsmith;

internal sealed class StretchStft
{
    private const float AlmostZero = 1e-30f;
    public readonly int Channels, Block, Hop, FftSize;
    public int Bands => FftSize / 2;
    public int InputLatency => Block - _windowOffset;
    public int OutputLatency => _windowOffset;
    private readonly int _historyLength;
    private readonly float[] _input, _output, _products, _window;
    private readonly float[] _time;
    private readonly ModifiedRealFft _fft;
    private readonly int _windowOffset;
    private sealed class WindowPlan
    {
        public readonly float[] Window;
        public readonly int Offset;
        public WindowPlan(int block, int hop)
        {
            Window = CreateWindow(block, hop);
            Offset = block / 2;
            for (var i = 0; i < block; i++) if (Window[i] > Window[Offset]) Offset = i;
        }
    }
    private static readonly BoundedPlanCache<(int Block, int Hop), WindowPlan> Windows =
        new(static key => new WindowPlan(key.Block, key.Hop), static plan => 64 + plan.Window.LongLength * 4, 512 * 1024);
    public static (int Input, int Output) Latencies(int block, int hop)
    {
        if (block < 4 || hop < 1 || hop > block) throw new ArgumentOutOfRangeException(nameof(hop));
        var plan = Windows.Get((block, hop));
        return (block - plan.Offset, plan.Offset);
    }
    private int _inputPosition, _outputPosition;

    public StretchStft(int channels, int block, int hop)
    {
        Channels = channels; Block = block; Hop = hop;
        FftSize = MixedRadixFft.StftSize(block);
        _historyLength = block + hop + 1;
        _input = new float[checked(channels * _historyLength)];
        _output = new float[checked(channels * block)];
        _products = new float[block];
        var plan = Windows.Get((block, hop));
        _window = plan.Window; _windowOffset = plan.Offset;
        _time = new float[FftSize];
        _fft = new ModifiedRealFft(FftSize);
        Reset();
    }
    public void Reset()
    {
        Array.Clear(_input); Array.Clear(_output); Array.Clear(_products);
        _inputPosition = Block; _outputPosition = 0;
        AddWindowProduct();
        for (var i = Block - Hop - 1; i >= 0; i--) _products[i] += _products[i + Hop];
        for (var i = 0; i < Block; i++) _products[i] = _products[i] * 0.1f + AlmostZero;
        for (var i = 0; i < Hop; i++) MoveOutput();
    }
    public void WriteInput(ReadOnlySpan<float> input, int start, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            var position = (_inputPosition + i) % _historyLength;
            for (var c = 0; c < Channels; c++) _input[c * _historyLength + position] = input[(start + i) * Channels + c];
        }
        _inputPosition = (_inputPosition + frames) % _historyLength;
    }
    public void Seek(ReadOnlySpan<float> input)
    {
        var inputFrames = input.Length / Channels;
        var length = Block + Hop;
        var start = Math.Max(0, inputFrames - length);
        var padding = length + start - inputFrames;
        for (var i = 0; i < length; i++)
        {
            var position = (_inputPosition + i) % _historyLength;
            for (var c = 0; c < Channels; c++)
                _input[c * _historyLength + position] = i < padding ? 0 : input[(i - padding + start) * Channels + c];
        }
        _inputPosition = (_inputPosition + length) % _historyLength;
    }
    public void Analyse(int channel, Span<Complex32> spectrum, int past = 0)
    {
        Array.Clear(_time);
        var position = (_historyLength * 2 + _inputPosition - Block - past) % _historyLength;
        for (var i = 0; i < Block; i++)
        {
            var ti = (i + FftSize - _windowOffset) % FftSize;
            var w = i < _windowOffset ? -_window[i] : _window[i];
            _time[ti] = _input[channel * _historyLength + position] * w;
            if (++position == _historyLength) position = 0;
        }
        _fft.Forward(_time, spectrum);
    }
    public void Synthesise(int channel, ReadOnlySpan<Complex32> spectrum)
    {
        if (channel == 0) AddWindowProduct();
        _fft.Inverse(spectrum, _time);
        for (var i = 0; i < Block; i++)
        {
            var ti = (i + FftSize - _windowOffset) % FftSize;
            var sample = _time[ti];
            var w = i < _windowOffset ? -_window[i] : _window[i];
            _output[channel * Block + (_outputPosition + i) % Block] += sample * w;
        }
    }
    public float ReadOutput(int channel) => _output[channel * Block + _outputPosition] / _products[_outputPosition];
    public void MoveOutput()
    {
        for (var c = 0; c < Channels; c++) _output[c * Block + _outputPosition] = 0;
        _products[_outputPosition] = AlmostZero;
        if (++_outputPosition == Block) _outputPosition = 0;
    }
    public void AddOutput(ReadOnlySpan<float> interleaved)
    {
        var frames = Math.Min(Block, interleaved.Length / Channels);
        for (var i = 0; i < frames; i++)
        {
            var position = (_outputPosition + i) % Block;
            for (var c = 0; c < Channels; c++) _output[c * Block + position] += interleaved[i * Channels + c] * _products[position];
        }
    }
    private void AddWindowProduct()
    {
        for (var i = 0; i < Block; i++) _products[(_outputPosition + i) % Block] += _window[i] * _window[i] * FftSize;
    }
    private static float[] CreateWindow(int size, int hop)
    {
        var bandwidth = (double)size / hop;
        bandwidth += 8 / ((bandwidth + 3) * (bandwidth + 3)) + 0.25 * Math.Max(3 - bandwidth, 0);
        bandwidth = Math.Max(bandwidth, 2);
        var beta = Math.Sqrt(bandwidth * bandwidth * 0.25 - 1) * Math.PI;
        var invBessel = 1 / Bessel0(beta);
        var window = new float[size];
        var offset = (size & 1) != 0 ? 1 : 0;
        for (var i = 0; i < size; i++)
        {
            var r = (2 * i + offset) / (double)size - 1;
            window[i] = (float)(Bessel0(beta * Math.Sqrt(1 - r * r)) * invBessel);
        }
        for (var i = 0; i < hop; i++)
        {
            double sum = 0;
            for (var j = i; j < size; j += hop) sum += window[j] * window[j];
            var factor = 1 / Math.Sqrt(sum);
            for (var j = i; j < size; j += hop) window[j] = (float)(window[j] * factor);
        }
        return window;
    }
    private static double Bessel0(double x)
    {
        double result = 0, term = 1, m = 0;
        while (term > 1e-4) { result += term; m++; term *= x * x / (4 * m * m); }
        return result;
    }
}
