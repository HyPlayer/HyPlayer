using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;

namespace HyPlayer.UWP.Chopin.Abstractions.Models
{
    public class AudioGraphPlaybackSource : IPlaybackSource, IDisposable
    {
        private bool disposedValue;

        public string Name { get; set; } = string.Empty;
        public MediaSource PlaybackSource { get; internal set; }
        public StorageFile LocalStorageFile { get; internal set; }
        public PlaybackSourceType PlaybackSourceType { get; internal set; }
        public Uri Path { get; set; }

        public async Task CreatePlaybackSource()
        {
            ThrowExceptionIfDisposed();
            if (LocalStorageFile != null)
            {
                CreatePlaybackSourceFormStorageFile();
            }
            else
            {
                await CreatePlaybackSourceFormUriAsync();
            }
        }
        private async Task CreatePlaybackSourceFormUriAsync()
        {
            if (string.IsNullOrEmpty(Path.Host))
            {
                LocalStorageFile = await StorageFile.GetFileFromPathAsync(Path.LocalPath);
                PlaybackSource = MediaSource.CreateFromStorageFile(LocalStorageFile);
                PlaybackSourceType = PlaybackSourceType.Local;
            }
            else
            {
                PlaybackSource = MediaSource.CreateFromUri(Path);
                PlaybackSourceType = PlaybackSourceType.Online;
            }
        }
        private void CreatePlaybackSourceFormStorageFile()
        {
            PlaybackSource = MediaSource.CreateFromStorageFile(LocalStorageFile);
            PlaybackSourceType = PlaybackSourceType.Local;
        }
        public AudioGraphPlaybackSource(Uri path)
        {
            Path = path;
        }
        public AudioGraphPlaybackSource(StorageFile path)
        {
            LocalStorageFile = path;
        }
        public AudioGraphPlaybackSource(MediaSource mediaSource)
        {
            PlaybackSource = mediaSource;
            PlaybackSourceType = PlaybackSourceType.MediaSource;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {

                }
                PlaybackSource?.Dispose();
                disposedValue = true;
            }
        }

        ~AudioGraphPlaybackSource()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        public void ThrowExceptionIfDisposed()
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(AudioGraphPlaybackSource));
        }
    }
}
