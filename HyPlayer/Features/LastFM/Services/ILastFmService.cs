using System;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.LastFM.Services;

public interface ILastFmService
{
    Uri CreateLoginUri();
    Task CompleteBrowserLoginAsync(string token);
    Task UpdateNowPlayingAsync(SingleSongBase item);
    Task ScrobbleAsync(SingleSongBase item);
}