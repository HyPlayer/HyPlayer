using System.Runtime.InteropServices;

namespace HyPlayer.Playback.Tests;

/// <summary>Activates the deployed Win2D DLL for the unpackaged test executable.</summary>
internal static class Win2DTestHost
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static nint _library;
    private static GetFactory? _getFactory;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFactory(nint name, out nint factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint value);

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;
            // Unpackaged tests do not inherit the MSIX VCLibs dependency graph.
            var package = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("")
                .First(p => p.Id.Name == "Microsoft.VCLibs.140.00" &&
                    p.Id.Architecture == (RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                        ? Windows.System.ProcessorArchitecture.Arm64 : Windows.System.ProcessorArchitecture.X64));
            foreach (var name in new[] { "vcruntime140_app.dll", "vcruntime140_1_app.dll", "msvcp140_app.dll" })
                NativeLibrary.Load(Path.Combine(package.InstalledLocation.Path, name));
            _library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Microsoft.Graphics.Canvas.dll"));
            _getFactory = Marshal.GetDelegateForFunctionPointer<GetFactory>(NativeLibrary.GetExport(_library, "DllGetActivationFactory"));
            var previous = WinRT.ActivationFactory.ActivationHandler;
            WinRT.ActivationFactory.ActivationHandler = (name, iid) =>
            {
                if (!name.StartsWith("Microsoft.Graphics.Canvas.", StringComparison.Ordinal))
                    return previous?.Invoke(name, iid) ?? 0;
                Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var text));
                nint factory = 0;
                try
                {
                    Marshal.ThrowExceptionForHR(_getFactory(text, out factory));
                    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory, in iid, out var result));
                    return result;
                }
                finally
                {
                    WindowsDeleteString(text);
                    if (factory != 0) Marshal.Release(factory);
                }
            };
            _initialized = true;
        }
    }
}
