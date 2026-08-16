using System.Collections.Generic;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using ObservableCollections;

namespace HyPlayer.Features.Downloads.Services;

public interface IDownloadService
{
    ObservableList<DownloadObject> Downloads { get; }

    Task AddAsync(SingleSongBase song);
    Task AddAsync(IEnumerable<SingleSongBase> songs);
    void Pause(DownloadObject download);
    void Resume(DownloadObject download);
    void Retry(DownloadObject download);
    void Remove(DownloadObject download);
    void ClearCompleted();
    void PauseAll();
    void ResumeAll();
}
