using System;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public sealed class PitchPreservingSpeed : IDisposable
    {
        public const double CompressBlockSeconds = TimeStretcher.CompactBlockSeconds;
        public const double CompressIntervalSeconds = TimeStretcher.CompactIntervalSeconds;

        private readonly int _sampleRate;
        private readonly SpeedResampler _resampler;
        private readonly float[] _content;
        private TimeStretcher _stretcher;

        public PitchPreservingSpeed(int sampleRate, int maxBlockFrames, double speed)
        {
            _sampleRate = sampleRate;
            _resampler = new SpeedResampler(maxBlockFrames);
            _content = new float[_resampler.MaxOutputFrames(maxBlockFrames) * 2];
            Compressing = speed > 1;
            _stretcher = CreateStretcher(Compressing);
            Apply(speed);
        }

        public bool Compressing { get; private set; }

        public void SetSpeed(double speed)
        {
            var compress = speed > 1;
            if (compress != Compressing)
            {
                var retired = _stretcher;
                _stretcher = CreateStretcher(compress);
                Compressing = compress;
                _resampler.Reset();
                retired.Dispose();
            }

            Apply(speed);
        }

        public void Process(ReadOnlySpan<float> input, Span<float> output)
        {
            if (!Compressing)
            {
                _stretcher.Process(input, output);
                return;
            }

            var frames = _resampler.Process(input, _content);
            _stretcher.Process(_content.AsSpan(0, frames * 2), output);
        }

        public void Dispose() => _stretcher.Dispose();

        private TimeStretcher CreateStretcher(bool compress) =>
            compress
                ? new TimeStretcher(2, _sampleRate, CompressBlockSeconds, CompressIntervalSeconds)
                : new TimeStretcher(2, _sampleRate);

        private void Apply(double speed)
        {
            if (Compressing) _resampler.SetSpeed(speed);
            else _stretcher.SetTranspose(1 / Math.Max(0.1, speed));
        }
    }
}
