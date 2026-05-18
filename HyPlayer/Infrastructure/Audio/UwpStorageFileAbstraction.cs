using System;
using System.IO;
using Windows.Storage;
using File = TagLib.File;

namespace HyPlayer.Classes;

public sealed partial class UwpStorageFileAbstraction : File.IFileAbstraction, IDisposable
{
    private bool _disposed;

    public UwpStorageFileAbstraction(IStorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Name = file.Name;
        ReadStream = file.OpenStreamForReadAsync().GetAwaiter().GetResult();
        WriteStream = file.OpenStreamForWriteAsync().GetAwaiter().GetResult();
    }

    public UwpStorageFileAbstraction(Stream readStream, Stream writeStream, string name = "HyPlayer Music")
    {
        ReadStream = readStream;
        WriteStream = writeStream;
        Name = name;
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
            WriteStream?.Dispose();
        }

        _disposed = true;
    }

    ~UwpStorageFileAbstraction()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
