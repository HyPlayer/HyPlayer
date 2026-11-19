using System;

namespace HyPlayer.Features.Playback.Analysis.Dsp;

public sealed class BluesteinFft
{
    private readonly int _n;
    private readonly int _m;
    private readonly Fft _fft;
    private readonly float[] _wRe;
    private readonly float[] _wIm;
    private readonly float[] _bRe;
    private readonly float[] _bIm;
    private readonly float[] _aRe;
    private readonly float[] _aIm;

    public BluesteinFft(int n)
    {
        _n = n;
        _m = 1;
        while (_m < 2 * n - 1) _m <<= 1;
        _fft = new Fft(_m);
        _wRe = new float[n];
        _wIm = new float[n];
        for (var i = 0; i < n; i++)
        {
            var angle = Math.PI * ((long)i * i % (2L * n)) / n;
            _wRe[i] = (float)Math.Cos(angle);
            _wIm[i] = (float)-Math.Sin(angle);
        }
        _bRe = new float[_m];
        _bIm = new float[_m];
        _bRe[0] = _wRe[0];
        _bIm[0] = -_wIm[0];
        for (var i = 1; i < n; i++)
        {
            _bRe[i] = _bRe[_m - i] = _wRe[i];
            _bIm[i] = _bIm[_m - i] = -_wIm[i];
        }
        _fft.Forward(_bRe, _bIm);
        _aRe = new float[_m];
        _aIm = new float[_m];
    }

    public int Size => _n;

    public void Forward(ReadOnlySpan<float> input, Span<float> outRe, Span<float> outIm, int bins)
    {
        Array.Clear(_aRe);
        Array.Clear(_aIm);
        for (var i = 0; i < _n; i++)
        {
            _aRe[i] = input[i] * _wRe[i];
            _aIm[i] = input[i] * _wIm[i];
        }
        _fft.Forward(_aRe, _aIm);
        for (var i = 0; i < _m; i++)
        {
            var re = _aRe[i] * _bRe[i] - _aIm[i] * _bIm[i];
            var im = _aRe[i] * _bIm[i] + _aIm[i] * _bRe[i];
            _aRe[i] = re;
            _aIm[i] = -im;
        }
        _fft.Forward(_aRe, _aIm);
        var scale = 1f / _m;
        for (var k = 0; k < bins; k++)
        {
            var re = _aRe[k] * scale;
            var im = -_aIm[k] * scale;
            outRe[k] = re * _wRe[k] - im * _wIm[k];
            outIm[k] = re * _wIm[k] + im * _wRe[k];
        }
    }
}
