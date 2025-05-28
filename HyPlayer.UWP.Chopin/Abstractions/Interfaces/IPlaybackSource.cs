using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Threading.Tasks;

namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface IPlaybackSource
    {
        string Name { get; set; }
        PlaybackSourceType PlaybackSourceType { get; }
        Uri Path { get; set; }
        PlaybackStatus PlaybackStatus { get; }
        Task CreatePlaybackSource();
    }
}
