using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HyPlayer.UWP.Chopin.Dsp
{
    public sealed unsafe class TimeStretcher : IDisposable
    {
        private static readonly object LoadLock = new();
        private static volatile bool _loadAttempted;
        private static delegate* unmanaged[Cdecl]<int, float, int, nint> _create;
        private static delegate* unmanaged[Cdecl]<nint, void> _destroy;
        private static delegate* unmanaged[Cdecl]<nint, void> _reset;
        private static delegate* unmanaged[Cdecl]<nint, float, int> _outputSeekLength;
        private static delegate* unmanaged[Cdecl]<nint, float*, int, void> _outputSeek;
        private static delegate* unmanaged[Cdecl]<nint, float*, int, float*, int, void> _process;
        private static delegate* unmanaged[Cdecl]<nint, float, void> _setTranspose;
        private static delegate* unmanaged[Cdecl]<int, int, int, nint> _createSized;

        private nint _handle;
        private readonly int _channels;

        public static bool IsAvailable
        {
            get
            {
                EnsureLoaded();
                return _create != null;
            }
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted) return;
            lock (LoadLock)
            {
                if (_loadAttempted) return;
                try
                {
                    Load();
                }
                finally
                {
                    _loadAttempted = true;
                }
            }
        }

        private static void Load()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "hyp_stretch.dll"),
                Path.Combine(Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar)) ?? baseDir, "hyp_stretch.dll"),
                "hyp_stretch.dll"
            };
            foreach (var candidate in candidates)
            {
                if (!NativeLibrary.TryLoad(candidate, out var library)) continue;
                if (!NativeLibrary.TryGetExport(library, "hyp_stretch_create", out var create)) continue;
                _destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "hyp_stretch_destroy");
                _reset = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "hyp_stretch_reset");
                _outputSeekLength = (delegate* unmanaged[Cdecl]<nint, float, int>)NativeLibrary.GetExport(library, "hyp_stretch_output_seek_length");
                _outputSeek = (delegate* unmanaged[Cdecl]<nint, float*, int, void>)NativeLibrary.GetExport(library, "hyp_stretch_output_seek");
                _process = (delegate* unmanaged[Cdecl]<nint, float*, int, float*, int, void>)NativeLibrary.GetExport(library, "hyp_stretch_process");
                if (NativeLibrary.TryGetExport(library, "hyp_stretch_set_transpose", out var transpose))
                    _setTranspose = (delegate* unmanaged[Cdecl]<nint, float, void>)transpose;
                if (NativeLibrary.TryGetExport(library, "hyp_stretch_create_sized", out var sized))
                    _createSized = (delegate* unmanaged[Cdecl]<int, int, int, nint>)sized;
                _create = (delegate* unmanaged[Cdecl]<int, float, int, nint>)create;
                return;
            }
        }

        public TimeStretcher(int channels, int sampleRate)
        {
            EnsureLoaded();
            if (_create == null) throw new InvalidOperationException("Time stretch library is not available");
            _channels = channels;
            _handle = _create(channels, sampleRate, 0);
        }

        public TimeStretcher(int channels, int sampleRate, double blockSeconds, double intervalSeconds)
        {
            EnsureLoaded();
            if (_create == null) throw new InvalidOperationException("Time stretch library is not available");
            _channels = channels;
            var block = (int)(sampleRate * blockSeconds);
            var interval = (int)(sampleRate * intervalSeconds);
            _handle = _createSized != null && block > 0 && interval > 0
                ? _createSized(channels, block, interval)
                : _create(channels, sampleRate, 0);
        }

        public int OutputSeekLength(double rate) => _outputSeekLength(_handle, (float)rate);

        public void OutputSeek(ReadOnlySpan<float> interleaved)
        {
            fixed (float* input = interleaved)
                _outputSeek(_handle, input, interleaved.Length / _channels);
        }

        public void Process(ReadOnlySpan<float> input, Span<float> output)
        {
            fixed (float* inPtr = input)
            fixed (float* outPtr = output)
                _process(_handle, inPtr, input.Length / _channels, outPtr, output.Length / _channels);
        }

        public void Reset() => _reset(_handle);

        public static bool CanTranspose
        {
            get
            {
                EnsureLoaded();
                return _create != null && _setTranspose != null;
            }
        }

        public void SetTranspose(double factor)
        {
            if (_setTranspose != null) _setTranspose(_handle, (float)factor);
        }

        public void Dispose()
        {
            if (_handle == 0) return;
            _destroy(_handle);
            _handle = 0;
        }
    }
}
