using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain;
using HyPlayer.Platform.Diagnostics;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.LyricEffects.Models;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using LiteFM.Abstractions;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static HyPlayer.Features.Settings.Services.UpdateManager;

namespace HyPlayer.Classes
{
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(The163KeyClass))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(CurPlayingListHistoryState))]
    [JsonSerializable(typeof(PlaybackMemoryState))]
    [JsonSerializable(typeof(PlaybackItemIdentity))]
    [JsonSerializable(typeof(List<PlaybackItemIdentity>))]
    [JsonSerializable(typeof(HyLyricInfo))]
    [JsonSerializable(typeof(HyALRCLyricInfo))]
    [JsonSerializable(typeof(LastFMSession))]
    [JsonSerializable(typeof(DumpInfo))]
    [JsonSerializable(typeof(PlaybackCurrentItemSnapshot))]
    [JsonSerializable(typeof(CommentUserInfo))]
    [JsonSerializable(typeof(LatestApplicationUpdate))]
    [JsonSerializable(typeof(GitHubReleaseResponse))]
    [JsonSerializable(typeof(LyricEffectProfileDocument))]
    [JsonSerializable(typeof(LyricRenderOperationDefinition))]
    [JsonSerializable(typeof(LyricOperationParameterDefinition))]
    [JsonSerializable(typeof(LyricTransitionDefinition))]
    [JsonSerializable(typeof(List<LyricRenderOperationDefinition>))]
    public partial class JsonDefaultContext : JsonSerializerContext
    {
    }
}