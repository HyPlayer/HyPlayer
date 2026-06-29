using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface ILastFmService
{
    Uri CreateLoginUri();
    Task CompleteBrowserLoginAsync(string token);
    Task UpdateNowPlayingAsync(SingleSongBase item);
    Task ScrobbleAsync(SingleSongBase item);
}
