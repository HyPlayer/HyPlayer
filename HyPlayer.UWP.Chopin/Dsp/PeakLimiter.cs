using System;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public sealed class PeakLimiter
    {
        public const double LookaheadSeconds = 0.003;
        public const double ReleaseSeconds = 0.02;
        public const float Ceiling = 0.977f;

        private readonly int _delay;
        private readonly float[] _line;
        private readonly long[] _minIndex;
        private readonly float[] _minValue;
        private readonly float[] _box;
        private readonly float _release;
        private int _minHead;
        private int _minCount;
        private long _index;
        private int _linePosition;
        private int _boxPosition;
        private double _boxSum;
        private float _gain = 1;

        public PeakLimiter(int sampleRate)
        {
            _delay = Math.Max(1, (int)(sampleRate * LookaheadSeconds));
            _line = new float[_delay * 2];
            _minIndex = new long[_delay + 2];
            _minValue = new float[_delay + 2];
            _box = new float[_delay];
            _release = (float)(1 - Math.Exp(-1 / (ReleaseSeconds * Math.Max(1, sampleRate))));
            Reset();
        }

        public int DelayFrames => _delay;

        public float Level { get; set; } = Ceiling;

        public void Reset()
        {
            Array.Clear(_line);
            Array.Fill(_box, 1f);
            _boxSum = _delay;
            _boxPosition = 0;
            _linePosition = 0;
            _minHead = 0;
            _minCount = 0;
            _index = 0;
            _gain = 1;
        }

        public void Process(Span<float> interleavedStereo)
        {
            var frames = interleavedStereo.Length / 2;
            var capacity = _minIndex.Length;
            var level = Level;
            for (var i = 0; i < frames; i++)
            {
                var left = interleavedStereo[i * 2];
                var right = interleavedStereo[i * 2 + 1];
                var peak = Math.Max(Math.Abs(left), Math.Abs(right));
                var required = peak > level ? level / peak : 1f;

                while (_minCount > 0 && _minValue[(_minHead + _minCount - 1) % capacity] >= required) _minCount--;
                var slot = (_minHead + _minCount) % capacity;
                _minIndex[slot] = _index;
                _minValue[slot] = required;
                _minCount++;
                while (_minIndex[_minHead] < _index - _delay)
                {
                    _minHead = (_minHead + 1) % capacity;
                    _minCount--;
                }

                var windowMin = _minValue[_minHead];
                _boxSum += windowMin - _box[_boxPosition];
                _box[_boxPosition] = windowMin;
                _boxPosition = (_boxPosition + 1) % _delay;
                var average = (float)(_boxSum / _delay);
                _gain = average < _gain ? average : _gain + (average - _gain) * _release;
                if (_gain > 1) _gain = 1;

                var delayedLeft = _line[_linePosition * 2];
                var delayedRight = _line[_linePosition * 2 + 1];
                _line[_linePosition * 2] = left;
                _line[_linePosition * 2 + 1] = right;
                _linePosition = (_linePosition + 1) % _delay;
                interleavedStereo[i * 2] = delayedLeft * _gain;
                interleavedStereo[i * 2 + 1] = delayedRight * _gain;
                _index++;
            }
        }
    }
}
