using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Media;

namespace HyPlayer.UWP.Chopin.Utils
{
    public class FFTProcessor
    {

        public const int FftSize = 1024;           // FFT 窗口大小，必须是2的幂
        public int CurrentFftSize = 0;
        public const int DisplayBandCount = 80;    // 最终显示的柱子数量
        public const int SpectrumBinCount = FftSize / 2;
        public const float SmoothingFactor = 0.85f; // 平滑系数 (0-1)，越高越平滑


        // --- 数据缓冲区 (全部预分配，避免GC) ---
        // 1. 用于 FFT 计算的复数缓冲区
        private Complex[] _fftBuffer = new Complex[FftSize];
        private readonly float[] _sampleBuffer = new float[FftSize];
        // 2. 存储计算完成后的线性频率数据 (FftSize / 2)
        private float[] _linearMagnitudes = new float[FftSize / 2];
        // 归一化的线性 FFT 幅值。供可视化、着色器和其他音频信息消费者共享。
        private readonly float[] _fftMagnitudes = new float[SpectrumBinCount];
        // 3. 存储经过对数合并和平滑处理后，供 UI 显示的数据
        public float[] DisplayData = new float[DisplayBandCount];
        // 4. 上一帧的显示数据，用于平滑计算
        private float[] _previousDisplayData = new float[DisplayBandCount];

        private readonly Lock _bufferLock = new Lock();
        private int _sampleRate = 48000;
        private long _version;

        public unsafe void ProcessFFT(AudioFrame frame, int sampleRate = 48000)
        {
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Read))
            using (var reference = buffer.CreateReference())
            {
                // ReSharper disable once SuspiciousTypeConversion.Global
                reference.GetBuffer(out var dataInBytes, out var capacity);
                int totalSamples = (int)(capacity / sizeof(float));

                int safeProcessingLimit = Math.Min(FftSize, totalSamples);
                CurrentFftSize = safeProcessingLimit;
                if (CurrentFftSize < 128) return;

                Marshal.Copy((IntPtr)dataInBytes, _sampleBuffer, 0, safeProcessingLimit);

                for (int i = 0; i < CurrentFftSize; i++)
                {
                    float sample = _sampleBuffer[i];
                    if (!float.IsFinite(sample)) sample = 0f;

                    float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (CurrentFftSize - 1)));
                    _fftBuffer[i] = new Complex(sample * window, 0d);
                }

                for (int i = CurrentFftSize; i < FftSize; i++)
                {
                    _fftBuffer[i] = Complex.Zero;
                }

                InPlaceFFT.Transform(_fftBuffer);

                lock (_bufferLock)
                {
                    _sampleRate = Math.Max(1, sampleRate);
                    for (var i = 0; i < SpectrumBinCount; i++)
                    {
                        var magnitude = (float)_fftBuffer[i].Magnitude;
                        _fftMagnitudes[i] = magnitude / CurrentFftSize;

                        float db = 20 * MathF.Log10(magnitude + 1e-9f) + 20;
                        _linearMagnitudes[i] = float.IsFinite(db) ? MathF.Max(0, db) : 0f;
                    }

                    ProcessBandsLogarithmically();
                    _version++;
                }
            }
        }

        /// <summary>
        /// 将最新的归一化线性 FFT 幅值复制给调用方。第 i 个频点对应
        /// i * sampleRate / FftSize Hz；返回 false 表示尚未产生有效频域数据。
        /// version 仅在产生新频域帧时递增，消费者可据此跳过重复数据。
        /// </summary>
        public bool TryCopyFftMagnitudes(float[] destination, out int sampleRate, out long version)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (destination.Length < SpectrumBinCount)
                throw new ArgumentException($"FFT output needs at least {SpectrumBinCount} elements.", nameof(destination));

            lock (_bufferLock)
            {
                sampleRate = _sampleRate;
                version = _version;
                if (_version == 0) return false;
                Array.Copy(_fftMagnitudes, destination, SpectrumBinCount);
                return true;
            }
        }

        public bool TryCopyDisplayData(float[] destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (destination.Length < DisplayBandCount)
                throw new ArgumentException($"Spectrum output needs at least {DisplayBandCount} elements.", nameof(destination));

            lock (_bufferLock)
            {
                if (_version == 0) return false;
                Array.Copy(DisplayData, destination, DisplayBandCount);
                return true;
            }
        }

        private void ProcessBandsLogarithmically()
        {
            double logBase = Math.Pow(SpectrumBinCount, 1.0 / DisplayBandCount);
            int fftIndex = 1;

            for (int i = 0; i < DisplayBandCount; i++)
            {
                int nextFftIndex = (int)Math.Pow(logBase, i + 1);
                nextFftIndex = Math.Clamp(nextFftIndex, fftIndex + 1, _linearMagnitudes.Length);

                float maxMagnitudeInBand = 0;
                // 找出当前频段区间内的最大值
                for (int j = fftIndex; j < nextFftIndex; j++)
                {
                    if (_linearMagnitudes[j] > maxMagnitudeInBand)
                        maxMagnitudeInBand = _linearMagnitudes[j];
                }
                fftIndex = nextFftIndex;

                // 应用时间平滑 (让跳动不那么剧烈)
                DisplayData[i] = _previousDisplayData[i] * SmoothingFactor +
                                  maxMagnitudeInBand * (1.0f - SmoothingFactor);
                _previousDisplayData[i] = DisplayData[i];
            }
        }
    }

    public static class InPlaceFFT
    {
        public static void Transform(Complex[] data)
        {
            var n = data.Length;
            // 位反转置换 (Bit-reversal permutation)
            var j = 0;
            for (var i = 0; i < n - 1; i++)
            {
                if (i < j)
                {
                    (data[i], data[j]) = (data[j], data[i]);
                }
                var m = n / 2;
                while (m >= 1 && j >= m) { j -= m; m /= 2; }
                j += m;
            }
            // 蝶形运算 (Butterfly updates)
            for (var m = 2; m <= n; m <<= 1)
            {
                var wM = Complex.FromPolarCoordinates(1, -2.0 * Math.PI / m);
                for (var k = 0; k < n; k += m)
                {
                    var w = Complex.One;
                    for (var x = 0; x < m / 2; x++)
                    {
                        var t = w * data[k + x + m / 2];
                        var u = data[k + x];
                        data[k + x] = u + t;
                        data[k + x + m / 2] = u - t;
                        w *= wM;
                    }
                }
            }
        }
    }
}
