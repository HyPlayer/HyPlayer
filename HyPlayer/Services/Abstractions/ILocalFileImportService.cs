using HyPlayer.Services.Playback.LocalProvider;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// Handles local file picking and metadata extraction for playlist imports.
/// </summary>
public interface ILocalFileImportService
{
    /// <summary>Shows the local audio file picker and returns playable items.</summary>
    Task<IList<LocalSong>> PickLocalFilesAsync();

    /// <summary>Loads a single storage file as a playable item.</summary>
    Task<LocalSong> LoadStorageFileAsync(StorageFile file, bool nocheck163 = false);

    /// <summary>Adds the file or its parent folder to the future access list.</summary>
    Task RegisterFutureAccessAsync(StorageFile file);
}
