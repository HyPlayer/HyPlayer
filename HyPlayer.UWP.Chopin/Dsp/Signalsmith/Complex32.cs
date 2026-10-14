// Managed port of Signalsmith Stretch/Linear. See LICENSE.txt and README.md.
using System;

namespace HyPlayer.UWP.Chopin.Dsp.Signalsmith;

internal readonly struct Complex32(float real, float imaginary)
{
    public readonly float Real = real;
    public readonly float Imaginary = imaginary;
    public float Norm => Real * Real + Imaginary * Imaginary;
    public Complex32 Conjugate => new(Real, -Imaginary);
    public static Complex32 Polar(double phase) => new((float)Math.Cos(phase), (float)Math.Sin(phase));
    public static Complex32 operator +(Complex32 a, Complex32 b) => new(a.Real + b.Real, a.Imaginary + b.Imaginary);
    public static Complex32 operator -(Complex32 a, Complex32 b) => new(a.Real - b.Real, a.Imaginary - b.Imaginary);
    public static Complex32 operator *(Complex32 a, Complex32 b) => new(a.Real * b.Real - a.Imaginary * b.Imaginary, a.Imaginary * b.Real + a.Real * b.Imaginary);
    public static Complex32 operator *(Complex32 a, float b) => new(a.Real * b, a.Imaginary * b);
    public static Complex32 operator /(Complex32 a, float b) => new(a.Real / b, a.Imaginary / b);
}
