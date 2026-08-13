using System.Collections.Generic;
using System.Text.Json.Serialization;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.LyricEffects.Models;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Platform.Diagnostics;
using LiteFM.Abstractions;
using static HyPlayer.Features.Settings.Services.UpdateManager;

namespace HyPlayer.Classes;

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