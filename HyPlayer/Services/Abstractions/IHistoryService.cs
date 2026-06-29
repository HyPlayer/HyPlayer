using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.History;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface IHistoryService
{
    void InitializeHistoryTrack();
    void AddNCSongHistory(string songId);
    void AddSearchHistory(string text);
    void AddSonglistHistory(string playlistId);
    Task SetCurrentPlayingListHistoryAsync(List<string> songIds, int currentIndex);
    Task ClearHistoryAsync();
    Task<List<SingleSongBase>> GetSongHistoryAsync();
    List<string> GetSearchHistory();
    Task<List<SingleSongBase>> GetCurrentPlayingListHistoryAsync();
    Task<CurPlayingListHistoryResult> GetCurrentPlayingListHistoryStateAsync();
    Task ClearCurrentPlayingListHistoryAsync();
}
