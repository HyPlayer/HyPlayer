// C# port of Signalsmith Stretch 1.3.2, limited to the operations exposed by HyPlayer.
// Copyright (c) 2022 Geraint Luff / Signalsmith Audio Ltd. MIT: see LICENSE.txt.
using System;

namespace HyPlayer.UWP.Chopin.Dsp.Signalsmith;

internal sealed class SignalsmithStretch
{
    private const float NoiseFloor = 1e-15f;
    private struct Band
    {
        public Complex32 Input, Previous, Output;
        public float Energy;
    }
    private readonly StretchStft _stft;
    private readonly Band[] _bins;
    private readonly float[] _predictionEnergy;
    private readonly Complex32[] _spectrum;
    private readonly float[] _preRoll;
    private sealed class PitchWorkspace(int channels, int bands)
    {
        public readonly float[] Energy = new float[bands], Smoothed = new float[bands];
        public readonly (float Input, float Output)[] Peaks = new (float, float)[bands];
        public readonly (float Input, float Gradient)[] Map = new (float, float)[bands];
        public readonly Complex32[] PredictionInput = new Complex32[checked(channels * bands)];
    }
    private PitchWorkspace _pitch;
    // Worker-owned PRNG state avoids Random's lazy per-thread state on the audio worker.
    private uint _randomState = (uint)Random.Shared.NextInt64(1, (long)uint.MaxValue + 1);
    private int _peakCount;
    private int _sinceLast = int.MaxValue, _previousInputOffset = -1, _silenceCounter;
    private bool _silenceFirst = true, _didSeek;
    private float _seekTimeFactor = 1;
    private float _transpose = 1;
    public float Transpose
    {
        get => _transpose;
        set
        {
            // Reserve optional pitch-mapping storage at configuration time, never in Process.
            if (value != 1 && _pitch is null) _pitch = new PitchWorkspace(Channels, Bands);
            _transpose = value;
        }
    }
    public int InputLatency => _stft.InputLatency;
    public int OutputLatency => _stft.OutputLatency;
    private int Channels => _stft.Channels;
    private int Bands => _stft.Bands;

    public SignalsmithStretch(int channels, int block, int hop)
    {
        _stft = new StretchStft(channels, block, hop);
        _bins = new Band[checked(channels * Bands)];
        _predictionEnergy = new float[_bins.Length];
        _spectrum = new Complex32[Bands];
        _preRoll = new float[checked(channels * OutputLatency)];
    }

    public void Reset()
    {
        _stft.Reset(); Array.Clear(_bins);
        _sinceLast = int.MaxValue; _previousInputOffset = -1;
        _silenceCounter = 0; _didSeek = false;
    }

    public void OutputSeek(ReadOnlySpan<float> input)
    {
        ValidateFrames(input.Length);
        Reset();
        var frames = input.Length / Channels;
        var surplus = Math.Max(frames - InputLatency, 0);
        var rate = surplus / (float)OutputLatency;
        var seekFrames = frames - surplus;
        _stft.Seek(input[..(seekFrames * Channels)]);
        _didSeek = true;
        _seekTimeFactor = rate * _stft.Hop > 1 ? 1 / rate : _stft.Hop;
        Process(input[(seekFrames * Channels)..], _preRoll);
        for (var i = 0; i < OutputLatency / 2; i++)
        {
            for (var c = 0; c < Channels; c++)
            {
                var a = i * Channels + c;
                var b = (OutputLatency - 1 - i) * Channels + c;
                (_preRoll[a], _preRoll[b]) = (-_preRoll[b], -_preRoll[a]);
            }
        }
        if ((OutputLatency & 1) != 0)
            for (var c = 0; c < Channels; c++) _preRoll[OutputLatency / 2 * Channels + c] *= -1;
        _stft.AddOutput(_preRoll);
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        ValidateFrames(input.Length); ValidateFrames(output.Length);
        if (input.Overlaps(output)) throw new ArgumentException("Input and output PCM buffers must not overlap.");
        var inputFrames = input.Length / Channels;
        var outputFrames = output.Length / Channels;
        var copied = 0;
        float totalEnergy = 0;
        // Preserve upstream channel-first accumulation and silence behaviour.
        for (var c = 0; c < Channels; c++)
            for (var i = c; i < input.Length; i += Channels) totalEnergy += input[i] * input[i];
        if (totalEnergy < NoiseFloor)
        {
            if (_silenceCounter >= 2 * _stft.Block)
            {
                if (_silenceFirst) { _silenceFirst = false; _sinceLast = int.MaxValue; Array.Clear(_bins); }
                for (var i = 0; i < outputFrames; i++)
                    for (var c = 0; c < Channels; c++) output[i * Channels + c] = inputFrames == 0 ? 0 : input[(i % inputFrames) * Channels + c];
                CopyInput(input, inputFrames, ref copied);
                return;
            }
            _silenceCounter = (int)Math.Min((long)_silenceCounter + inputFrames, int.MaxValue);
        }
        else { _silenceCounter = 0; _silenceFirst = true; }

        for (var i = 0; i < outputFrames; i++)
        {
            if (_sinceLast >= _stft.Hop)
            {
                _sinceLast = 0;
                var offset = (int)MathF.Round(i * (float)inputFrames / outputFrames, MidpointRounding.AwayFromZero);
                var interval = offset - _previousInputOffset;
                _previousInputOffset = offset;
                CopyInput(input, offset, ref copied);
                var newSpectrum = _didSeek || interval > 0;
                var timeFactor = _didSeek ? _seekTimeFactor : _stft.Hop / (float)Math.Max(1, interval);
                if (newSpectrum)
                {
                    if (_didSeek || Math.Abs(interval - _stft.Hop) > 1) Analyse(true, _stft.Hop);
                    Analyse(false, 0);
                }
                _didSeek = false;
                ProcessSpectrum(newSpectrum, timeFactor);
                for (var c = 0; c < Channels; c++)
                {
                    for (var b = 0; b < Bands; b++) _spectrum[b] = _bins[c * Bands + b].Output;
                    _stft.Synthesise(c, _spectrum);
                }
            }
            _sinceLast++;
            for (var c = 0; c < Channels; c++) output[i * Channels + c] = _stft.ReadOutput(c);
            _stft.MoveOutput();
        }
        CopyInput(input, inputFrames, ref copied);
        _previousInputOffset -= inputFrames;
    }

    private void ValidateFrames(int samples)
    {
        if (samples % Channels != 0) throw new ArgumentException("PCM buffers must contain complete interleaved frames.");
    }
    private void CopyInput(ReadOnlySpan<float> input, int to, ref int copied)
    {
        var length = Math.Min(_stft.Block + _stft.Hop, to - copied);
        _stft.WriteInput(input, to - length, length);
        copied = to;
    }
    private void Analyse(bool previous, int past)
    {
        for (var c = 0; c < Channels; c++)
        {
            _stft.Analyse(c, _spectrum, past);
            for (var b = 0; b < Bands; b++)
            {
                if (previous) _bins[c * Bands + b].Previous = _spectrum[b];
                else _bins[c * Bands + b].Input = _spectrum[b];
            }
        }
    }
    private Complex32 GetBand(int channel, int bin, bool previous = false)
    {
        if ((uint)bin >= (uint)Bands) return default;
        return previous ? _bins[channel * Bands + bin].Previous : _bins[channel * Bands + bin].Input;
    }
    private Complex32 Fractional(int channel, float index, bool previous = false)
    {
        var low = (int)MathF.Floor(index);
        var a = GetBand(channel, low, previous); var b = GetBand(channel, low + 1, previous);
        return a + (b - a) * (index - low);
    }
    private float EnergyAt(int channel, int bin) => (uint)bin < (uint)Bands ? _bins[channel * Bands + bin].Energy : 0;

    private void ProcessSpectrum(bool newSpectrum, float timeFactor)
    {
        var smoothingBins = _stft.FftSize / (float)_stft.Hop;
        var longStep = (int)MathF.Round(smoothingBins, MidpointRounding.AwayFromZero);
        timeFactor = Math.Max(timeFactor, 0.5f);
        if (newSpectrum)
        {
            var rotation = Complex32.Polar((float)(0.5f / _stft.FftSize * _stft.Hop * (float)(2 * Math.PI)));
            var rotationStep = Complex32.Polar((float)((1.5f / _stft.FftSize - 0.5f / _stft.FftSize) * _stft.Hop * (float)(2 * Math.PI)));
            for (var c = 0; c < Channels; c++)
            {
                var rot = rotation;
                for (var b = 0; b < Bands; b++)
                {
                    ref var bin = ref _bins[c * Bands + b];
                    bin.Output *= rot; bin.Previous *= rot;
                    rot *= rotationStep;
                }
            }
        }
        if (Transpose != 1) UpdatePitchMap(smoothingBins);
        else
        {
            for (var i = 0; i < _bins.Length; i++) _bins[i].Energy = _bins[i].Input.Norm;
        }
        for (var c = 0; c < Channels; c++)
        {
            for (var b = 0; b < Bands; b++)
            {
                ref var bin = ref _bins[c * Bands + b];
                var index = c * Bands + b;
                var previousEnergy = _predictionEnergy[index];
                Complex32 input, previous;
                float energy;
                if (Transpose == 1)
                {
                    // Most playback/mixing preserves pitch: the map is exactly the identity.
                    // Avoid floor, neighbour reads and three redundant interpolations per bin.
                    energy = bin.Energy;
                    input = bin.Input;
                    previous = bin.Previous;
                }
                else
                {
                    var map = _pitch.Map[b];
                    var low = (int)MathF.Floor(map.Input);
                    var fraction = map.Input - low;
                    var e0 = EnergyAt(c, low); var e1 = EnergyAt(c, low + 1);
                    energy = (e0 + (e1 - e0) * fraction) * Math.Max(0, map.Gradient);
                    input = Fractional(c, map.Input);
                    _pitch.PredictionInput[index] = input;
                    previous = Fractional(c, map.Input, true);
                }
                _predictionEnergy[index] = energy;
                var twist = input * previous.Conjugate;
                bin.Output = bin.Output * twist / (Math.Max(previousEnergy, energy) + NoiseFloor);
            }
        }
        // Sequential frequency traversal is intentional: lower bins contain the newly predicted phase.
        for (var b = 0; b < Bands; b++)
        {
            var channel = 0;
            var maxEnergy = _predictionEnergy[b];
            for (var c = 1; c < Channels; c++)
            {
                var energy = _predictionEnergy[c * Bands + b];
                if (energy > maxEnergy) { channel = c; maxEnergy = energy; }
            }
            var baseIndex = channel * Bands;
            var predictionInput = PredictionInput(baseIndex + b);
            var map = MapPoint(b);
            var phase = default(Complex32);
            if (b > 0)
            {
                var factor = RandomTimeFactor(timeFactor);
                var twist = predictionInput * Fractional(channel, map.Input - factor).Conjugate;
                phase += _bins[baseIndex + b - 1].Output * twist;
                if (b >= longStep)
                {
                    twist = predictionInput * Fractional(channel, map.Input - longStep * factor).Conjugate;
                    phase += _bins[baseIndex + b - longStep].Output * twist;
                }
            }
            if (b < Bands - 1)
            {
                var factor = RandomTimeFactor(timeFactor);
                var up = PredictionInput(baseIndex + b + 1);
                var twist = up * Fractional(channel, MapPoint(b + 1).Input - factor).Conjugate;
                phase += _bins[baseIndex + b + 1].Output * twist.Conjugate;
                if (b < Bands - longStep)
                {
                    up = PredictionInput(baseIndex + b + longStep);
                    twist = up * Fractional(channel, MapPoint(b + longStep).Input - longStep * factor).Conjugate;
                    phase += _bins[baseIndex + b + longStep].Output * twist.Conjugate;
                }
            }
            var result = MakeOutput(maxEnergy, predictionInput, phase);
            _bins[baseIndex + b].Output = result;
            for (var c = 0; c < Channels; c++)
            {
                if (c == channel) continue;
                var index = c * Bands + b;
                var otherInput = PredictionInput(index);
                var twist = otherInput * predictionInput.Conjugate;
                _bins[index].Output = MakeOutput(_predictionEnergy[index], otherInput, result * twist);
            }
        }
        if (newSpectrum) for (var i = 0; i < _bins.Length; i++) _bins[i].Previous = _bins[i].Input;
    }
    private float RandomTimeFactor(float factor)
    {
        if (factor <= 2) return factor;
        var state = _randomState;
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        _randomState = state;
        var uniform = (state >> 8) * (1f / 16777216);
        return 4 - factor + uniform * (2 * factor - 4);
    }

    private void UpdatePitchMap(float smoothingBins)
    {
        var _energy = _pitch.Energy;
        var _smoothed = _pitch.Smoothed;
        var _map = _pitch.Map;
        var _peaks = _pitch.Peaks;
        Array.Clear(_energy);
        for (var c = 0; c < Channels; c++)
            for (var b = 0; b < Bands; b++)
            {
                var e = _bins[c * Bands + b].Input.Norm;
                _bins[c * Bands + b].Energy = e; _energy[b] += e;
            }
        _energy.CopyTo(_smoothed, 0);
        var slew = 1 / (1 + smoothingBins * 0.5f);
        float state = 0;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            for (var b = Bands - 1; b >= 0; b--) { state += (_smoothed[b] - state) * slew; _smoothed[b] = state; }
            for (var b = 0; b < Bands; b++) { state += (_smoothed[b] - state) * slew; _smoothed[b] = state; }
        }
        _peakCount = 0;
        var start = 0;
        while (start < Bands)
        {
            if (_energy[start] > _smoothed[start])
            {
                var end = start;
                float weighted = 0, total = 0;
                while (end < Bands && _energy[end] > _smoothed[end]) { weighted += end * _energy[end]; total += _energy[end++]; }
                var bin = weighted / total;
                // setTransposeFactor() removes the tonality limit in the exposed API.
                _peaks[_peakCount++] = (bin, ((bin + 0.5f) / _stft.FftSize) * Transpose * _stft.FftSize - 0.5f);
                start = end;
            }
            start++;
        }
        if (_peakCount == 0) { for (var b = 0; b < Bands; b++) _map[b] = (b, 1); return; }
        var bottom = _peaks[0];
        var bottomOffset = bottom.Input - bottom.Output;
        for (var b = 0; b < Math.Min(Bands, (int)MathF.Ceiling(bottom.Output)); b++) _map[b] = (b + bottomOffset, 1);
        for (var p = 1; p < _peakCount; p++)
        {
            var previous = _peaks[p - 1]; var next = _peaks[p];
            var scale = 1 / (next.Output - previous.Output);
            var offset = previous.Input - previous.Output;
            var delta = next.Input - next.Output - previous.Input + previous.Output;
            var gradient = delta * scale;
            var first = Math.Max(0, (int)MathF.Ceiling(previous.Output));
            var last = Math.Min(Bands, (int)MathF.Ceiling(next.Output));
            for (var b = first; b < last; b++)
            {
                var r = (b - previous.Output) * scale;
                _map[b] = (b + offset + r * r * (3 - 2 * r) * delta, 1 + 6 * r * (1 - r) * gradient);
            }
        }
        var top = _peaks[_peakCount - 1];
        var topOffset = top.Input - top.Output;
        for (var b = Math.Max(0, (int)top.Output); b < Bands; b++) _map[b] = (b + topOffset, 1);
    }
    private (float Input, float Gradient) MapPoint(int bin) => Transpose == 1 ? (bin, 1) : _pitch.Map[bin];
    private Complex32 PredictionInput(int index) => Transpose == 1 ? _bins[index].Input : _pitch.PredictionInput[index];
    private static Complex32 MakeOutput(float energy, Complex32 input, Complex32 phase)
    {
        var norm = phase.Norm;
        if (norm <= NoiseFloor) { phase = input; norm = input.Norm + NoiseFloor; }
        return phase * MathF.Sqrt(energy / norm);
    }
}
