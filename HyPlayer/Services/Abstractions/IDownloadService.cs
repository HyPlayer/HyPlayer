using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Downloads;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface IDownloadService
{
    ObservableCollection<DownloadObject> Downloads { get; }

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
