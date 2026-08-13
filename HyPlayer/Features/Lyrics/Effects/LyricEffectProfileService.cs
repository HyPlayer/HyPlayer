#nullable enable

using HyPlayer.Classes;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Features.Lyrics.Effects;

public sealed class LyricEffectProfileChangedEventArgs : EventArgs
{
    public required CompiledLyricEffectProfile Profile { get; init; }

    public required bool IsPreview { get; init; }
}

public sealed class LyricEffectProfileFormatException : Exception
{
    public LyricEffectProfileFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface ILyricEffectProfileService
{
    event EventHandler<LyricEffectProfileChangedEventArgs>? ProfileChanged;

    LyricEffectProfileDocument CommittedDocument { get; }

    CompiledLyricEffectProfile EffectiveProfile { get; }

    IReadOnlyList<LyricRenderOperationDescriptor> Descriptors { get; }

    IReadOnlyList<FocusedTextOperationDescriptor> FocusedTextDescriptors { get; }

    Task InitializeAsync();

    LyricEffectProfileDocument CreateDraft();

    LyricProfileCompileResult Preview(LyricEffectProfileDocument document);

    void CancelPreview();

    Task<LyricProfileCompileResult> CommitAsync(LyricEffectProfileDocument document);

    Task<LyricEffectProfileDocument> ImportAsync(StorageFile file);

    LyricEffectProfileDocument Import(string json);

    string Export(LyricEffectProfileDocument document);
}

public sealed class LyricEffectProfileService : ILyricEffectProfileService
{
    public const string FolderName = "LyricEffects";
    public const string ActiveFileName = "active.hylfx";
    public const string BackupFileName = "active.backup.hylfx";
    private const string TemporaryFileName = "active.tmp";

    private readonly ILyricRenderOperationRegistry _registry;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly object _stateLock = new();
    private LyricEffectProfileDocument _committedDocument;
    private CompiledLyricEffectProfile _committedProfile;
    private CompiledLyricEffectProfile _effectiveProfile;
    private bool _initialized;

    public LyricEffectProfileService(ILyricRenderOperationRegistry registry)
    {
        _registry = registry;
        _committedDocument = LyricEffectPresets.CreateDefaultProfile();
        _committedProfile = CompileOrThrow(_committedDocument);
        _effectiveProfile = _committedProfile;
    }

    public event EventHandler<LyricEffectProfileChangedEventArgs>? ProfileChanged;

    public LyricEffectProfileDocument CommittedDocument
    {
        get
        {
            lock (_stateLock) return LyricEffectPresets.CloneProfile(_committedDocument);
        }
    }

    public CompiledLyricEffectProfile EffectiveProfile => Volatile.Read(ref _effectiveProfile);

    public IReadOnlyList<LyricRenderOperationDescriptor> Descriptors => _registry.Descriptors;

    public IReadOnlyList<FocusedTextOperationDescriptor> FocusedTextDescriptors =>
        FocusedTextEffectCompiler.Descriptors;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initializeLock.WaitAsync();
        try
        {
            if (_initialized) return;
            var localFolder = ApplicationData.Current.LocalFolder;
            var folder = await localFolder.CreateFolderAsync(FolderName, CreationCollisionOption.OpenIfExists);
            var activeItem = await folder.TryGetItemAsync(ActiveFileName);
            LyricEffectProfileDocument? document = null;
            var recoveredFromBackup = false;

            if (activeItem is StorageFile activeFile)
                document = await TryReadAsync(activeFile);

            if (document is null && activeItem is not null)
            {
                if (await folder.TryGetItemAsync(BackupFileName) is StorageFile backupFile)
                {
                    var backupDocument = await TryReadAsync(backupFile);
                    if (backupDocument is not null)
                    {
                        document = backupDocument;
                        recoveredFromBackup = true;
                    }
                }
            }

            if (document is null)
            {
                document = LyricEffectPresets.CreateDefaultProfile();
                await PersistAsync(folder, document, keepBackup: false);
            }
            else if (recoveredFromBackup)
            {
                await PersistAsync(folder, document, keepBackup: false);
            }

            var compiled = CompileOrThrow(document);
            lock (_stateLock)
            {
                _committedDocument = LyricEffectPresets.CloneProfile(document);
                _committedProfile = compiled;
                Volatile.Write(ref _effectiveProfile, compiled);
                _initialized = true;
            }

            Publish(compiled, false);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public LyricEffectProfileDocument CreateDraft() => CommittedDocument;

    public LyricProfileCompileResult Preview(LyricEffectProfileDocument document)
    {
        var result = Compile(document);
        if (!result.IsSuccess) return result;
        Volatile.Write(ref _effectiveProfile, result.Profile!);
        Publish(result.Profile!, true);
        return result;
    }

    public void CancelPreview()
    {
        CompiledLyricEffectProfile committed;
        lock (_stateLock) committed = _committedProfile;
        Volatile.Write(ref _effectiveProfile, committed);
        Publish(committed, false);
    }

    public async Task<LyricProfileCompileResult> CommitAsync(LyricEffectProfileDocument document)
    {
        var result = Compile(document);
        if (!result.IsSuccess) return result;

        var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
            FolderName,
            CreationCollisionOption.OpenIfExists);
        await PersistAsync(folder, result.Profile!.Document, keepBackup: true);

        lock (_stateLock)
        {
            _committedDocument = LyricEffectPresets.CloneProfile(result.Profile.Document);
            _committedProfile = result.Profile;
            Volatile.Write(ref _effectiveProfile, result.Profile);
        }

        Publish(result.Profile, false);
        return result;
    }

    public async Task<LyricEffectProfileDocument> ImportAsync(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > LyricEffectProfileValidation.MaximumFileBytes)
            throw new LyricEffectProfileFormatException("歌词特效文件不能超过 1 MiB。");
        return Import(await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8));
    }

    public LyricEffectProfileDocument Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > LyricEffectProfileValidation.MaximumFileBytes)
            throw new LyricEffectProfileFormatException("歌词特效文件不能超过 1 MiB。");

        try
        {
            var document = JsonSerializer.Deserialize(json, JsonDefaultContext.Default.LyricEffectProfileDocument)
                           ?? throw new LyricEffectProfileFormatException("歌词特效文件内容为空。");
            document = LyricEffectProfileValidation.MigrateToCurrent(document);
            EnsureValid(document);
            return document;
        }
        catch (LyricEffectProfileFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new LyricEffectProfileFormatException(exception.Message, exception);
        }
    }

    public string Export(LyricEffectProfileDocument document)
    {
        var result = Compile(document);
        if (!result.IsSuccess)
            throw new LyricEffectProfileFormatException(FormatDiagnostics(result.Diagnostics));
        return JsonSerializer.Serialize(result.Profile!.Document, JsonDefaultContext.Default.LyricEffectProfileDocument);
    }

    private LyricProfileCompileResult Compile(LyricEffectProfileDocument document)
    {
        var validationErrors = LyricEffectProfileValidation.Validate(document);
        if (validationErrors.Count == 0) return _registry.Compile(document);

        return new LyricProfileCompileResult
        {
            Diagnostics = validationErrors.Select(error => new LyricProfileDiagnostic(
                LyricProfileDiagnosticSeverity.Error,
                error.Message,
                error.InstanceId,
                error.Property)).ToList()
        };
    }

    private CompiledLyricEffectProfile CompileOrThrow(LyricEffectProfileDocument document)
    {
        var result = Compile(document);
        return result.Profile ?? throw new LyricEffectProfileFormatException(FormatDiagnostics(result.Diagnostics));
    }

    private void EnsureValid(LyricEffectProfileDocument document)
    {
        var result = Compile(document);
        if (!result.IsSuccess)
            throw new LyricEffectProfileFormatException(FormatDiagnostics(result.Diagnostics));
    }

    private async Task<LyricEffectProfileDocument?> TryReadAsync(StorageFile file)
    {
        try
        {
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > LyricEffectProfileValidation.MaximumFileBytes) return null;
            return Import(await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8));
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistAsync(StorageFolder folder, LyricEffectProfileDocument document, bool keepBackup)
    {
        var json = JsonSerializer.Serialize(document, JsonDefaultContext.Default.LyricEffectProfileDocument);
        if (Encoding.UTF8.GetByteCount(json) > LyricEffectProfileValidation.MaximumFileBytes)
            throw new LyricEffectProfileFormatException("歌词特效文件不能超过 1 MiB。");

        if (keepBackup && await folder.TryGetItemAsync(ActiveFileName) is StorageFile current)
            await current.CopyAsync(folder, BackupFileName, NameCollisionOption.ReplaceExisting);

        var temporary = await folder.CreateFileAsync(TemporaryFileName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(temporary, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        var target = await folder.CreateFileAsync(ActiveFileName, CreationCollisionOption.OpenIfExists);
        await temporary.MoveAndReplaceAsync(target);
    }

    private void Publish(CompiledLyricEffectProfile profile, bool isPreview) =>
        ProfileChanged?.Invoke(this, new LyricEffectProfileChangedEventArgs
        {
            Profile = profile,
            IsPreview = isPreview
        });

    private static string FormatDiagnostics(IReadOnlyList<LyricProfileDiagnostic> diagnostics) =>
        diagnostics.Count == 0
            ? "歌词特效配置无效。"
            : string.Join(Environment.NewLine, diagnostics.Select(item => item.Message));
}
