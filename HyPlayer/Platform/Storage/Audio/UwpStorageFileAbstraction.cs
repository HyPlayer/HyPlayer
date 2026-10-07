using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using File = TagLib.File;

namespace HyPlayer.Platform.Storage.Audio;

public sealed partial class UwpStorageFileAbstraction : File.IFileAbstraction, IDisposable
{
    private bool _disposed;

    public static async Task<UwpStorageFileAbstraction> OpenAsync(IStorageFile file, bool writable = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        // One handle avoids opening a writer while a separate read handle is still open.
        // Buffer TagLib's small synchronous reads; callers parse/save on a worker thread.
        var randomAccessStream = await file.OpenAsync(writable ? FileAccessMode.ReadWrite : FileAccessMode.Read);
        try
        {
            var stream = writable
                ? randomAccessStream.AsStream(64 * 1024)
                : randomAccessStream.AsStreamForRead(64 * 1024);
            return new UwpStorageFileAbstraction(stream, stream, file.Name);
        }
        catch
        {
            randomAccessStream.Dispose();
            throw;
        }
    }

    public UwpStorageFileAbstraction(Stream readStream, Stream writeStream, string name = "HyPlayer Music")
    {
        ReadStream = readStream;
        WriteStream = writeStream;
        Name = name;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public string Name { get; }

    public Stream ReadStream { get; }

    public Stream WriteStream { get; }

    public void CloseStream(Stream stream)
    {
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            ReadStream?.Dispose();
            if (!ReferenceEquals(ReadStream, WriteStream))
                WriteStream?.Dispose();
        }

        _disposed = true;
    }

    ~UwpStorageFileAbstraction()
    {
        Dispose(false);
    }
}
