#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricRenderer.Pipeline;

namespace HyPlayer.Features.Lyrics.Effects;

public sealed class LyricEffectProfileChangedEventArgs : EventArgs
{
    public required CompiledLyricEffectProfile Profile { get; init; }

    public required bool IsPreview { get; init; }
}

public interface ILyricEffectProfileService
{
    LyricEffectProfileDocument CommittedDocument { get; }

    CompiledLyricEffectProfile EffectiveProfile { get; }

    IReadOnlyList<LyricRenderOperationDescriptor> Descriptors { get; }
    event EventHandler<LyricEffectProfileChangedEventArgs>? ProfileChanged;

    Task InitializeAsync();

    LyricEffectProfileDocument CreateDraft();

    LyricProfileCompileResult Preview(LyricEffectProfileDocument document);

    void CancelPreview();

    Task<LyricProfileCompileResult> CommitAsync(LyricEffectProfileDocument document);

    Task<LyricEffectProfileDocument> ImportAsync(StorageFile file);

    LyricEffectProfileDocument Import(string json);

    string Export(LyricEffectProfileDocument document);
}
