using System;

namespace HyPlayer.Features.Playback.AutoMix;

public readonly record struct MixSendSettings(
    double DelaySeconds,
    double Feedback,
    double FeedbackLowPassHz,
    double Direct,
    double ReverbMix,
    double ReverbSeconds,
    double WetHighPassHz,
    double WetLowPassHz,
    double WetGain)
{
    public const double NeutralDelaySeconds = 0.19;

    public static MixSendSettings ReverbTail(double delaySeconds) =>
        new(delaySeconds > 0 ? delaySeconds : NeutralDelaySeconds, 0.3, 2500, 1, 1, 2.8, 180, 7000, 0.5);

    public static MixSendSettings EchoOut(double beatSeconds) =>
        new(beatSeconds, 0.5, 3000, 0, 0.25, 1.4, 250, 6000, 0.6);
}

public sealed class MixSend
{
    public const double MaxDelaySeconds = 1;
    public const double MaxFeedback = 0.85;
    public const double ReleaseFadeSeconds = 0.3;
    public const double QuietLevel = 1e-5;
    public const double InputFloor = 1e-9;
    private const double HighDecayRatio = 0.35;
    private const double DiffusionGain = 0.6;
    private const double InputGain = 0.5;
    private const int Lines = 16;
    private const int PositionMask = 0x3FFFFFFF;
    private static readonly int[] LineLengths = [1433, 1549, 1669, 1801, 1949, 2099, 2267, 2447, 2647, 2857, 3083, 3329, 3593, 3881, 4201, 4547];
    private static readonly int[] DiffuserLengths = [151, 197, 367, 421];

    private readonly int _sampleRate;
    private readonly float[] _delayL;
    private readonly float[] _delayR;
    private readonly int _delayMask;
    private readonly int _delayFrames;
    private readonly double _feedback;
    private readonly double _feedbackCoefficient;
    private readonly double _direct;
    private readonly double _reverbMix;
    private readonly double _reverbScale;
    private readonly double _wetGain;
    private readonly float[][] _lines = new float[Lines][];
    private readonly int[] _lineFrames = new int[Lines];
    private readonly int[] _lineMasks = new int[Lines];
    private readonly double[] _lineGains = new double[Lines];
    private readonly double[] _lineDamping = new double[Lines];
    private readonly double[] _lineStates = new double[Lines];
    private readonly double[] _mixed = new double[Lines];
    private readonly float[][] _diffusers = new float[4][];
    private readonly int[] _diffuserFrames = new int[4];
    private readonly int[] _diffuserMasks = new int[4];
    private readonly StereoBiquad _highPass = new();
    private readonly StereoBiquad _lowPass = new();
    private readonly int _quietLimit;
    private readonly int _releaseFadeFrames;
    private float[] _wet = new float[8192];
    private double _feedbackStateL;
    private double _feedbackStateR;
    private int _position;
    private int _quietFrames;
    private long _releaseLeft = -1;
    private bool _released;

    public MixSend(int sampleRate, MixSendSettings settings)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        var scale = _sampleRate / 44100.0;
        _delayFrames = (int)Math.Round(Math.Clamp(settings.DelaySeconds, 0.02, MaxDelaySeconds) * _sampleRate);
        var delaySize = PowerOfTwo(_delayFrames + 1);
        _delayL = new float[delaySize];
        _delayR = new float[delaySize];
        _delayMask = delaySize - 1;
        _feedback = Math.Clamp(settings.Feedback, 0, MaxFeedback);
        _feedbackCoefficient = OnePole(settings.FeedbackLowPassHz);
        _direct = Math.Clamp(settings.Direct, 0, 1);
        _reverbMix = Math.Clamp(settings.ReverbMix, 0, 1);
        _wetGain = Math.Max(0, settings.WetGain);

        var decay = Math.Clamp(settings.ReverbSeconds, 0.2, 6) * _sampleRate;
        double energy = 0;
        var longest = 0;
        for (var i = 0; i < Lines; i++)
        {
            var frames = NextPrime((int)Math.Round(LineLengths[i] * scale));
            var size = PowerOfTwo(frames + 1);
            _lines[i] = new float[size];
            _lineFrames[i] = frames;
            _lineMasks[i] = size - 1;
            _lineGains[i] = Math.Pow(10, -3.0 * frames / decay);
            _lineDamping[i] = 2 / (1 + Math.Pow(_lineGains[i], 1 - 1 / HighDecayRatio));
            if (i % 2 == 0) energy += InputGain * InputGain / (1 - _lineGains[i] * _lineGains[i]);
            longest = Math.Max(longest, frames);
        }

        _reverbScale = 1 / Math.Sqrt(energy);
        for (var i = 0; i < 4; i++)
        {
            var frames = NextPrime((int)Math.Round(DiffuserLengths[i] * scale));
            var size = PowerOfTwo(frames + 1);
            _diffusers[i] = new float[size];
            _diffuserFrames[i] = frames;
            _diffuserMasks[i] = size - 1;
        }

        _highPass.SetHighPass(settings.WetHighPassHz, _sampleRate);
        _lowPass.SetLowPass(settings.WetLowPassHz, _sampleRate);
        _quietLimit = _delayFrames + longest + _sampleRate / 20;
        _releaseFadeFrames = Math.Max(1, (int)(ReleaseFadeSeconds * _sampleRate));
    }

    public bool Active { get; private set; }
    public bool Released => _released;
    public int DelayFrames => _delayFrames;

    public void Release(long maxFrames)
    {
        if (_released) return;
        _released = true;
        if (Active) _releaseLeft = Math.Max(1, maxFrames);
    }

    public void Process(ReadOnlySpan<float> input, int frames, double sendStart, double sendEnd, Span<float> mix, double gainStart, double gainEnd)
    {
        if (frames <= 0) return;
        if (_released)
        {
            sendStart = 0;
            sendEnd = 0;
        }

        var silent = input.Length < frames * 2 || sendStart <= 0 && sendEnd <= 0;
        if (silent && !Active) return;
        if (_wet.Length < frames * 2) _wet = new float[frames * 4];

        var delayL = _delayL;
        var delayR = _delayR;
        var delayMask = _delayMask;
        var delayFrames = _delayFrames;
        var lines = _lines;
        var lineFrames = _lineFrames;
        var lineMasks = _lineMasks;
        var lineGains = _lineGains;
        var lineDamping = _lineDamping;
        var states = _lineStates;
        var mixed = _mixed;
        var feedbackL = _feedbackStateL;
        var feedbackR = _feedbackStateR;
        var position = _position;
        var wet = _wet;
        var inputPeak = 0d;
        var mixScale = 1 / Math.Sqrt(Lines);

        for (var i = 0; i < frames; i++)
        {
            double inL = 0, inR = 0;
            if (!silent)
            {
                var send = sendStart + (sendEnd - sendStart) * ((i + 1) / (double)frames);
                inL = input[i * 2] * send;
                inR = input[i * 2 + 1] * send;
                var peak = Math.Max(Math.Abs(inL), Math.Abs(inR));
                if (peak < InputFloor)
                {
                    inL = 0;
                    inR = 0;
                }
                else if (peak > inputPeak)
                {
                    inputPeak = peak;
                }
            }

            var read = (position - delayFrames) & delayMask;
            double echoL = delayL[read];
            double echoR = delayR[read];
            feedbackL += _feedbackCoefficient * (echoL - feedbackL);
            feedbackR += _feedbackCoefficient * (echoR - feedbackR);
            delayL[position & delayMask] = (float)(inL + _feedback * feedbackL);
            delayR[position & delayMask] = (float)(inR + _feedback * feedbackR);
            var stageL = _direct * inL + echoL;
            var stageR = _direct * inR + echoR;

            var roomL = Diffuse(_diffusers[0], _diffuserFrames[0], _diffuserMasks[0], position, stageL);
            roomL = Diffuse(_diffusers[2], _diffuserFrames[2], _diffuserMasks[2], position, roomL) * InputGain;
            var roomR = Diffuse(_diffusers[1], _diffuserFrames[1], _diffuserMasks[1], position, stageR);
            roomR = Diffuse(_diffusers[3], _diffuserFrames[3], _diffuserMasks[3], position, roomR) * InputGain;

            double reverbL = 0, reverbR = 0;
            for (var n = 0; n < Lines; n += 2)
            {
                double even = lines[n][(position - lineFrames[n]) & lineMasks[n]];
                double odd = lines[n + 1][(position - lineFrames[n + 1]) & lineMasks[n + 1]];
                reverbL += even;
                reverbR += odd;
                states[n] += lineDamping[n] * (even - states[n]);
                states[n + 1] += lineDamping[n + 1] * (odd - states[n + 1]);
                mixed[n] = states[n] * lineGains[n];
                mixed[n + 1] = states[n + 1] * lineGains[n + 1];
            }

            for (var half = 1; half < Lines; half <<= 1)
            for (var group = 0; group < Lines; group += half << 1)
            for (var k = group; k < group + half; k++)
            {
                var x = mixed[k];
                var y = mixed[k + half];
                mixed[k] = x + y;
                mixed[k + half] = x - y;
            }

            for (var n = 0; n < Lines; n += 2)
            {
                lines[n][position & lineMasks[n]] = (float)(mixed[n] * mixScale + roomL);
                lines[n + 1][position & lineMasks[n + 1]] = (float)(mixed[n + 1] * mixScale + roomR);
            }

            reverbL *= _reverbScale;
            reverbR *= _reverbScale;
            wet[i * 2] = (float)(stageL + (reverbL - stageL) * _reverbMix);
            wet[i * 2 + 1] = (float)(stageR + (reverbR - stageR) * _reverbMix);
            position = (position + 1) & PositionMask;
        }

        _feedbackStateL = feedbackL;
        _feedbackStateR = feedbackR;
        _position = position;
        if (inputPeak > 0) Active = true;

        var block = new Span<float>(wet, 0, frames * 2);
        _highPass.Process(block, frames);
        _lowPass.Process(block, frames);
        var wetPeak = 0f;
        var finished = false;
        for (var i = 0; i < frames; i++)
        {
            var left = block[i * 2];
            var right = block[i * 2 + 1];
            var peak = Math.Max(Math.Abs(left), Math.Abs(right));
            if (peak > wetPeak) wetPeak = peak;
            var g = (gainStart + (gainEnd - gainStart) * ((i + 1) / (double)frames)) * _wetGain;
            if (_releaseLeft >= 0)
            {
                if (_releaseLeft == 0)
                {
                    finished = true;
                    break;
                }

                if (_releaseLeft < _releaseFadeFrames) g *= _releaseLeft / (double)_releaseFadeFrames;
                _releaseLeft--;
            }

            mix[i * 2] += (float)(left * g);
            mix[i * 2 + 1] += (float)(right * g);
        }

        if (inputPeak > 0 || wetPeak >= QuietLevel) _quietFrames = 0;
        else _quietFrames += frames;
        if (finished || _releaseLeft == 0 || _quietFrames >= _quietLimit) Clear();
    }

    public void Clear()
    {
        Array.Clear(_delayL);
        Array.Clear(_delayR);
        foreach (var line in _lines) Array.Clear(line);
        foreach (var diffuser in _diffusers) Array.Clear(diffuser);
        Array.Clear(_lineStates);
        Array.Clear(_mixed);
        _feedbackStateL = 0;
        _feedbackStateR = 0;
        _highPass.Reset();
        _lowPass.Reset();
        _quietFrames = 0;
        _releaseLeft = -1;
        Active = false;
    }

    private static double Diffuse(float[] buffer, int frames, int mask, int position, double input)
    {
        double delayed = buffer[(position - frames) & mask];
        var output = delayed - DiffusionGain * input;
        buffer[position & mask] = (float)(input + DiffusionGain * output);
        return output;
    }

    private double OnePole(double frequency)
    {
        return 1 - Math.Exp(-2 * Math.PI * Math.Clamp(frequency, 20, _sampleRate * 0.45) / _sampleRate);
    }

    private static int PowerOfTwo(int value)
    {
        var size = 16;
        while (size < value) size <<= 1;
        return size;
    }

    private static int NextPrime(int value)
    {
        var candidate = Math.Max(3, value | 1);
        while (true)
        {
            var prime = true;
            for (var divisor = 3; divisor * divisor <= candidate; divisor += 2)
            {
                if (candidate % divisor != 0) continue;
                prime = false;
                break;
            }

            if (prime) return candidate;
            candidate += 2;
        }
    }
}
