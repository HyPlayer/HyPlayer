using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.UWP.Chopin.Utils
{
    public class FFTProcessor : IAudioQuantumProcessor
    {
        
        public const int FftSize = 2048;           // FFT 窗口大小，必须是2的幂
        public const int DisplayBandCount = 256;    // 最终显示的柱子数量
        public const float SmoothingFactor = 0.8f; // 平滑系数 (0-1)，越高越平滑
        
        
        // --- 数据缓冲区 (全部预分配，避免GC) ---
        // 1. 用于 FFT 计算的复数缓冲区
        private Complex[] _fftBuffer = new Complex[FftSize];
        // 2. 存储计算完成后的线性频率数据 (FftSize / 2)
        private float[] _linearMagnitudes = new float[FftSize / 2];
        // 3. 存储经过对数合并和平滑处理后，供 UI 显示的数据
        public float[] DisplayData = new float[DisplayBandCount];
        // 4. 上一帧的显示数据，用于平滑计算
        private float[] _previousDisplayData = new float[DisplayBandCount];
        
        private readonly object _bufferLock = new object();

        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }

        public unsafe void ProcessFFT(AudioFrame frame)
        {
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Read))
            using (var reference = buffer.CreateReference())
            {
                // ReSharper disable once SuspiciousTypeConversion.Global
                ((IMemoryBufferByteAccess)reference).GetBuffer(out var dataInBytes, out var capacity);
                float* dataInFloat = (float*)dataInBytes;

                // 核心修正：根据实际拿到的内存大小决定处理多少数据
                int totalSamples = (int)(capacity / sizeof(float));
                int channels = 2; // 假设是立体声
                int availablePairs = totalSamples / channels;

                // 取 FftSize 和 实际可用数据量 的最小值，防止越界
                int safeProcessingLimit = Math.Min(FftSize, availablePairs);

                // 如果连 128 个点都凑不够，这一帧就跳过
                if (safeProcessingLimit < 128) return;

                for (int i = 0; i < safeProcessingLimit; i++)
                {
                    float sample = dataInFloat[i * 2];

                    // 鲁棒性检查：防止音频流输入 NaN 或 Inf
                    if (float.IsInfinity(sample)) sample = 0;

                    float window = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / (safeProcessingLimit - 1)));
                    _fftBuffer[i] = new Complex(sample * window, 0);
                }

                // 清空缓冲区剩余部分（Zero Padding），防止旧数据干扰
                for (int i = safeProcessingLimit; i < FftSize; i++)
                {
                    _fftBuffer[i] = Complex.Zero;
                }

                // 2. 执行高性能原地 FFT
                InPlaceFFT.Transform(_fftBuffer);

                // 3. 计算幅值并转换为分贝 (仅取前半部分有效数据)
                for (var i = 0; i < FftSize / 2; i++)
                {
                    var magnitude = (float)_fftBuffer[i].Magnitude;

                    // 核心防护：
                    // 使用 1e-9 避免负无穷
                    // 使用 Math.Clamp 限制视觉表现范围
                    float db = 20 * MathF.Log10(magnitude + 1e-9f) + 20;

                    if (float.IsNaN(db) || float.IsNegativeInfinity(db))
                        _linearMagnitudes[i] = 0;
                    else if (float.IsPositiveInfinity(db))
                        _linearMagnitudes[i] = 100; // 给一个视觉上限
                    else
                        _linearMagnitudes[i] = MathF.Max(0, db);
                }

                // 4. 将线性频率数据合并为较少的显示频段 (对数映射)
                // 并应用时间平滑。
                lock (_bufferLock) // 加锁快速写入显示缓冲区
                {
                    ProcessBandsLogarithmically();
                }
            }
        }
        
        private void ProcessBandsLogarithmically()
        {
            double logBase = Math.Pow(FftSize / 2.0, 1.0 / DisplayBandCount);
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
                if (i < j) { (data[i], data[j]) = (data[j], data[i]);
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