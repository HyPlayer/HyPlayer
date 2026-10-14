// Managed transform backend for Signalsmith's half-bin STFT. See LICENSE.txt.
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HyPlayer.UWP.Chopin.Dsp.Signalsmith;

/// <summary>Unnormalised, allocation-free FFT for upstream 2/3/5 fast sizes.</summary>
internal sealed class MixedRadixFft
{
    private readonly int _inner, _outer;
    private readonly int[] _permutation;
    private readonly Complex32[] _roots, _scratch;
    private readonly float[][] _stageRoots;

    internal sealed class Plan
    {
        public readonly int Inner, Outer, Size;
        public readonly int[] Permutation;
        public readonly Complex32[] Roots;
        public readonly float[][] StageRoots;
        public readonly long Bytes;

        public Plan(int size)
        {
            Size = size; Inner = 1; Outer = size;
            while ((Outer & 1) == 0) { Inner *= 2; Outer /= 2; }
            if (Outer != 1 && Outer != 3 && Outer != 5) throw new ArgumentException("Unsupported FFT size.");
            Permutation = new int[Inner];
            for (var i = 1; i < Inner; i++) Permutation[i] = (Permutation[i >> 1] >> 1) | ((i & 1) * (Inner >> 1));
            var roots = new Complex32[size];
            for (var i = 0; i < size; i++) roots[i] = Complex32.Polar(-2 * Math.PI * i / size);
            StageRoots = new float[Math.Max(0, BitOperations.Log2((uint)Inner) - 2)][];
            Bytes = 128 + Permutation.LongLength * 4;
            for (var length = 8; length <= Inner; length *= 2)
            {
                var stage = new float[length];
                var step = size / length;
                for (var k = 0; k < length / 2; k++)
                {
                    stage[2 * k] = roots[k * step].Real;
                    stage[2 * k + 1] = roots[k * step].Imaginary;
                }
                StageRoots[BitOperations.Log2((uint)length) - 3] = stage;
                Bytes += 32 + stage.LongLength * 4;
            }
            Roots = Outer == 1 ? Array.Empty<Complex32>() : roots;
            Bytes += Roots.LongLength * 8;
        }
    }

    public MixedRadixFft(Plan plan)
    {
        _inner = plan.Inner; _outer = plan.Outer;
        _permutation = plan.Permutation; _roots = plan.Roots; _stageRoots = plan.StageRoots;
        _scratch = _outer == 1 ? Array.Empty<Complex32>() : new Complex32[plan.Size];
    }

    // DynamicSTFT uses RealFFT.fastSizeAbove((block + 1)/2)*2.
    public static int StftSize(int block)
    {
        var size = (((block + 1) / 2) + 1) / 2;
        var pow2 = 1;
        while (pow2 < 16 && pow2 < size) pow2 *= 2;
        while ((long)pow2 * 8 < size) pow2 = checked(pow2 * 2);
        var multiple = (size + pow2 - 1) / pow2;
        if (multiple == 7) multiple++;
        return checked(multiple * pow2 * 4);
    }

    public void Forward(Complex32[] input, Complex32[] output)
    {
        // Gather each strided power-of-two transform directly in bit-reversed order.
        for (var r = 0; r < _outer; r++)
        {
            var start = r * _inner;
            for (var i = 0; i < _inner; i++) output[start + _permutation[i]] = input[r + i * _outer];
            if (_inner == 2)
            {
                var a = output[start]; var b = output[start + 1];
                output[start] = a + b; output[start + 1] = a - b;
            }
            // Fuse the first two stages; all twiddles are exactly 1 or -i.
            for (var offset = 0; offset + 3 < _inner; offset += 4)
            {
                var index = start + offset;
                var a = output[index]; var b = output[index + 1];
                var c = output[index + 2]; var d = output[index + 3];
                var even = a + b; var odd = c + d;
                var difference = c - d;
                var twist = new Complex32(difference.Imaginary, -difference.Real);
                output[index] = even + odd; output[index + 2] = even - odd;
                output[index + 1] = a - b + twist; output[index + 3] = a - b - twist;
            }
            var values = MemoryMarshal.Cast<Complex32, float>(output.AsSpan());
            for (var length = 8; length <= _inner; length *= 2)
            {
                var roots = _stageRoots[BitOperations.Log2((uint)length) - 3];
                for (var offset = 0; offset < _inner; offset += length)
                    Butterfly(values, roots, (start + offset) * 2, length);
            }
        }
        if (_outer == 1) return;
        // Factor the small DFT instead of repeating complex twiddles and modulo per output bin.
        for (var k = 0; k < _inner; k++)
        {
            var a = output[k];
            var b = output[_inner + k] * _roots[k];
            var c = output[2 * _inner + k] * _roots[2 * k];
            if (_outer == 3)
            {
                var sum = b + c;
                var difference = b - c;
                var centre = a - sum * 0.5f;
                var rotation = new Complex32(difference.Imaginary * 0.8660254f, -difference.Real * 0.8660254f);
                _scratch[k] = a + sum;
                _scratch[_inner + k] = centre + rotation;
                _scratch[2 * _inner + k] = centre - rotation;
            }
            else
            {
                var d = output[3 * _inner + k] * _roots[3 * k];
                var e = output[4 * _inner + k] * _roots[4 * k];
                var sum1 = b + e; var sum2 = c + d;
                var diff1 = b - e; var diff2 = c - d;
                var centre1 = a + sum1 * 0.309016994f - sum2 * 0.809016994f;
                var centre2 = a - sum1 * 0.809016994f + sum2 * 0.309016994f;
                var twist1 = diff1 * 0.951056516f + diff2 * 0.587785252f;
                var twist2 = diff1 * 0.587785252f - diff2 * 0.951056516f;
                var rotation1 = new Complex32(twist1.Imaginary, -twist1.Real);
                var rotation2 = new Complex32(twist2.Imaginary, -twist2.Real);
                _scratch[k] = a + sum1 + sum2;
                _scratch[_inner + k] = centre1 + rotation1; _scratch[4 * _inner + k] = centre1 - rotation1;
                _scratch[2 * _inner + k] = centre2 + rotation2; _scratch[3 * _inner + k] = centre2 - rotation2;
            }
        }
        _scratch.CopyTo(output, 0);
    }

    private static void Butterfly(Span<float> values, float[] roots, int start, int halfFloats)
    {
        var k = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var data = ref MemoryMarshal.GetReference(values);
            ref var twiddles = ref MemoryMarshal.GetArrayDataReference(roots);
            var realIndices = Vector128.Create(0, 0, 2, 2);
            var imaginaryIndices = Vector128.Create(1, 1, 3, 3);
            var swappedIndices = Vector128.Create(1, 0, 3, 2);
            var signs = Vector128.Create(-1f, 1f, -1f, 1f);
            for (; k + 3 < halfFloats; k += 4)
            {
                var a = Vector128.LoadUnsafe(ref data, (nuint)(start + k));
                var b = Vector128.LoadUnsafe(ref data, (nuint)(start + halfFloats + k));
                var root = Vector128.LoadUnsafe(ref twiddles, (nuint)k);
                var real = Vector128.Shuffle(b, realIndices);
                var imaginary = Vector128.Shuffle(b, imaginaryIndices);
                var product = real * root + imaginary * Vector128.Shuffle(root, swappedIndices) * signs;
                (a + product).StoreUnsafe(ref data, (nuint)(start + k));
                (a - product).StoreUnsafe(ref data, (nuint)(start + halfFloats + k));
            }
        }
        for (; k < halfFloats; k += 2)
        {
            var a = new Complex32(values[start + k], values[start + k + 1]);
            var b = new Complex32(values[start + halfFloats + k], values[start + halfFloats + k + 1]) * new Complex32(roots[k], roots[k + 1]);
            var sum = a + b; var difference = a - b;
            values[start + k] = sum.Real; values[start + k + 1] = sum.Imaginary;
            values[start + halfFloats + k] = difference.Real; values[start + halfFloats + k + 1] = difference.Imaginary;
        }
    }

    public void Inverse(Complex32[] input, Complex32[] output)
    {
        for (var i = 0; i < input.Length; i++) input[i] = input[i].Conjugate;
        Forward(input, output);
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = input[i].Conjugate;
            output[i] = output[i].Conjugate;
        }
    }
}
