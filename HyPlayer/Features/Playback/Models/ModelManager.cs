using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Settings;
using Microsoft.ML.OnnxRuntime;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI.Core;

namespace HyPlayer.Features.Playback.Models;

public enum ModelState
{
    NotInstalled,
    Downloading,
    Installed,
    Failed
}

public sealed partial class ModelEntry : ObservableObject
{
    private readonly AutoMixSettings _settings;

    public ModelEntry(ModelDescriptor descriptor, AutoMixSettings settings)
    {
        _settings = settings;
        Descriptor = descriptor;
        IsEnabled = settings.IsModelEnabled(descriptor.Id);
    }

    public ModelDescriptor Descriptor { get; }
    public string Name => Descriptor.Name;
    public string Details => $"{Descriptor.Description}\n{FormatSize(Descriptor.TotalSize)} · {Descriptor.License} 许可 · {Descriptor.Homepage}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(IsDownloading), nameof(CanDownload), nameof(StatusText))]
    public partial ModelState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public bool IsInstalled => State == ModelState.Installed;
    public bool IsDownloading => State == ModelState.Downloading;
    public bool CanDownload => State is ModelState.NotInstalled or ModelState.Failed;

    public string StatusText => State switch
    {
        ModelState.Downloading => Speed > 0 ? $"下载中 {Progress:0}% · {FormatSpeed(Speed)}" : $"下载中 {Progress:0}%",
        ModelState.Installed => "已安装",
        ModelState.Failed => $"失败：{Error}",
        _ => "未下载"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial double Speed { get; set; }

    internal CancellationTokenSource? DownloadCts { get; set; }

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond >= 1024 * 1024 ? $"{bytesPerSecond / 1024 / 1024:0.0} MB/s" : $"{bytesPerSecond / 1024:0} KB/s";

    partial void OnIsEnabledChanged(bool value) => _settings.SetModelEnabled(Descriptor.Id, value);

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : $"{bytes / 1024d:0} KB";
}

public sealed partial class ModelManager : ObservableObject
{
    public const string FolderToken = "modelFolder";
    private static readonly TimeSpan SessionIdleRelease = TimeSpan.FromSeconds(60);

    private readonly AutoMixSettings _settings;
    private readonly Dictionary<string, SessionSlot> _sessions = new();
    private readonly Lock _sessionLock = new();
    private Task? _refresh;

    public ModelManager(AutoMixSettings settings)
    {
        _settings = settings;
        Entries = new System.Collections.ObjectModel.ObservableCollection<ModelEntry>(ModelCatalog.All.Select(d => new ModelEntry(d, settings)));
        FolderPath = settings.ModelFolder;
        Mirror = settings.ModelMirror;
        UseGpu = settings.UseGpu;
        var savedAdapter = settings.GpuAdapter;
        var savedIndex = Adapters.ToList().FindIndex(a => a.Name == savedAdapter);
        GpuChoiceIndex = savedIndex < 0 ? 0 : savedIndex + 1;
        foreach (var entry in Entries) entry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(ModelEntry.State) or nameof(ModelEntry.IsEnabled))) return;
            OnPropertyChanged(nameof(AnalyzerSummary));
        };
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AutoMixSettings.ModelsSuspended))
                Post(() => OnPropertyChanged(nameof(AnalyzerSummary)));
        };
    }

    public string AnalyzerSummary =>
        $"节拍：{(IsUsable(ModelCatalog.BeatThisId) ? "Beat This!" : "内置算法")}　调性：{(IsUsable(ModelCatalog.SKeyId) ? "S-KEY" : "内置算法")}　人声：{(IsUsable(ModelCatalog.VocalId) ? "歌词时间轴 + MDX-Net" : "歌词时间轴 + 中频活动检测（内置）")}";

    public System.Collections.ObjectModel.ObservableCollection<ModelEntry> Entries { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderDisplayPath), nameof(IsCustomFolder))]
    public partial string FolderPath { get; set; }

    [ObservableProperty]
    public partial string Mirror { get; set; }

    [ObservableProperty]
    public partial bool UseGpu { get; set; }

    public IReadOnlyList<GpuAdapter> Adapters { get; } = GpuAdapters.Enumerate();

    public List<string> GpuChoices { get; } = ["自动", .. GpuAdapters.Enumerate().Select(a => a.Name)];

    [ObservableProperty]
    public partial int GpuChoiceIndex { get; set; }

    public bool IsCustomFolder => !string.IsNullOrEmpty(FolderPath);
    public bool IsCustomFolderAccessible => IsCustomFolder && StorageApplicationPermissions.FutureAccessList.ContainsItem(FolderToken);
    public string FolderDisplayPath => IsCustomFolderAccessible ? FolderPath : Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Models");

    partial void OnFolderPathChanged(string value) => _settings.ModelFolder = value;

    partial void OnMirrorChanged(string value) => _settings.ModelMirror = value;

    partial void OnGpuChoiceIndexChanged(int value)
    {
        var name = value > 0 && value <= Adapters.Count ? Adapters[value - 1].Name : string.Empty;
        if (_settings.GpuAdapter == name) return;
        _settings.GpuAdapter = name;
        DisposeAllSessions();
    }

    private (int Index, string Name) ResolveAdapter()
    {
        var saved = _settings.GpuAdapter;
        if (string.IsNullOrEmpty(saved)) return (0, "自动");
        foreach (var adapter in GpuAdapters.Enumerate())
            if (adapter.Name == saved) return (adapter.Index, adapter.Name);
        return (0, "自动");
    }

    partial void OnUseGpuChanged(bool value)
    {
        _settings.UseGpu = value;
        DisposeAllSessions();
    }

    public ModelEntry? Find(string id) => Entries.FirstOrDefault(e => e.Descriptor.Id == id);

    public bool IsUsable(string id) => !_settings.ModelsSuspended && Find(id) is { IsInstalled: true, IsEnabled: true };

    public bool HasUsableModel
    {
        get
        {
            foreach (var entry in Entries)
                if (entry is { IsInstalled: true, IsEnabled: true }) return true;
            return false;
        }
    }

    public Task EnsureRefreshedAsync() => _refresh ??= RefreshAsync();

    public async Task RefreshAsync()
    {
        StorageFolder folder;
        try
        {
            folder = await GetFolderAsync();
        }
        catch
        {
            foreach (var entry in Entries)
                if (!entry.IsDownloading) Post(() => entry.State = ModelState.NotInstalled);
            return;
        }
        foreach (var entry in Entries)
        {
            if (entry.IsDownloading) continue;
            var installed = await IsInstalledAsync(folder, entry.Descriptor);
            Post(() =>
            {
                entry.Error = null;
                entry.State = installed ? ModelState.Installed : ModelState.NotInstalled;
            });
        }
    }

    public async Task<StorageFolder> GetFolderAsync()
    {
        if (IsCustomFolder && StorageApplicationPermissions.FutureAccessList.ContainsItem(FolderToken))
            return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(FolderToken);
        return await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Models", CreationCollisionOption.OpenIfExists);
    }

    public static async Task<StorageFolder> GetDefaultFolderAsync() =>
        await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Models", CreationCollisionOption.OpenIfExists);

    public static async Task<IReadOnlyList<StorageFile>> GetModelFilesAsync(StorageFolder folder)
    {
        var files = new List<StorageFile>();
        foreach (var descriptor in ModelCatalog.All)
            foreach (var file in descriptor.Files)
                if (await folder.TryGetItemAsync(file.Name) is StorageFile stored) files.Add(stored);
        return files;
    }

    public async Task MigrateAsync(StorageFolder from, StorageFolder to, bool deleteOriginal)
    {
        DisposeAllSessions();
        var files = await GetModelFilesAsync(from);
        foreach (var file in files)
            await file.CopyAsync(to, file.Name, NameCollisionOption.ReplaceExisting);
        if (!deleteOriginal) return;
        foreach (var file in files)
        {
            try
            {
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch
            {
            }
        }
    }

    public async Task SetFolderAsync(StorageFolder? folder)
    {
        DisposeAllSessions();
        if (folder is null)
        {
            StorageApplicationPermissions.FutureAccessList.Remove(FolderToken);
            FolderPath = string.Empty;
        }
        else
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace(FolderToken, folder);
            FolderPath = folder.Path;
        }
        await RefreshAsync();
    }

    private async Task<bool> IsInstalledAsync(StorageFolder folder, ModelDescriptor descriptor)
    {
        foreach (var file in descriptor.Files)
        {
            if (await folder.TryGetItemAsync(file.Name) is not StorageFile stored) return false;
            var properties = await stored.GetBasicPropertiesAsync();
            var importedSize = file.IsModel ? _settings.GetImportedModelSize(descriptor.Id) : 0L;
            if ((long)properties.Size != file.Size && (long)properties.Size != importedSize) return false;
        }
        return true;
    }

    public async Task DownloadAsync(ModelEntry entry)
    {
        if (entry.IsDownloading) return;
        var cts = new CancellationTokenSource();
        entry.DownloadCts = cts;
        entry.Error = null;
        entry.Progress = 0;
        entry.State = ModelState.Downloading;
        try
        {
            var folder = await GetFolderAsync();
            await DownloadFilesAsync(folder, entry, entry.Descriptor.Files, cts.Token);
            _settings.SetImportedModelSize(entry.Descriptor.Id, 0L);
            Post(() =>
            {
                entry.Speed = 0;
                entry.State = ModelState.Installed;
            });
        }
        catch (OperationCanceledException)
        {
            Post(() =>
            {
                entry.Speed = 0;
                entry.State = ModelState.NotInstalled;
            });
        }
        catch (Exception ex)
        {
            HyPlayer.Features.Playback.AutoMix.AutoMixLog.Write($"model download {entry.Descriptor.Id} failed: {ex}");
            Post(() =>
            {
                entry.Speed = 0;
                entry.Error = ex.Message;
                entry.State = ModelState.Failed;
            });
        }
        finally
        {
            entry.DownloadCts = null;
            cts.Dispose();
        }
    }

    public void CancelDownload(ModelEntry entry) => entry.DownloadCts?.Cancel();

    private async Task DownloadFilesAsync(StorageFolder folder, ModelEntry entry, IEnumerable<ModelFile> files, CancellationToken ct)
    {
        var pending = new List<ModelFile>();
        foreach (var file in files)
        {
            if (await folder.TryGetItemAsync(file.Name) is StorageFile existing &&
                (long)(await existing.GetBasicPropertiesAsync()).Size == file.Size)
                continue;
            pending.Add(file);
        }
        var total = Math.Max(1, pending.Sum(f => f.Size));
        long done = 0;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var file in pending)
        {
            var url = $"{Mirror.Trim().TrimEnd('/')}/{file.Repository}/resolve/main/{file.RemotePath}";
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var part = await folder.CreateFileAsync(file.Name + ".part", CreationCollisionOption.ReplaceExisting);
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long written = 0;
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = await part.OpenStreamForWriteAsync())
                {
                    output.SetLength(0);
                    var buffer = new byte[1 << 20];
                    var lastReport = 0d;
                    var speedWatch = System.Diagnostics.Stopwatch.StartNew();
                    long windowBytes = 0;
                    var speed = 0d;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        written += read;
                        windowBytes += read;
                        var progress = (done + written) * 100d / total;
                        var elapsed = speedWatch.Elapsed.TotalSeconds;
                        if (elapsed >= 0.5)
                        {
                            var instant = windowBytes / elapsed;
                            speed = speed <= 0 ? instant : speed * 0.6 + instant * 0.4;
                            windowBytes = 0;
                            speedWatch.Restart();
                            var reported = speed;
                            Post(() => entry.Speed = reported);
                        }
                        if (progress - lastReport >= 0.5)
                        {
                            lastReport = progress;
                            Post(() => entry.Progress = progress);
                        }
                    }
                }
                var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (written != file.Size) throw new InvalidDataException($"{file.Name} 大小不符");
                if (digest != file.Sha256) throw new InvalidDataException($"{file.Name} 校验失败");
                await part.RenameAsync(file.Name, NameCollisionOption.ReplaceExisting);
                done += file.Size;
            }
            catch
            {
                try
                {
                    if (part.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                        await part.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch
                {
                }
                throw;
            }
        }
    }

    public async Task<string?> ImportAsync(ModelEntry entry, StorageFile picked)
    {
        var descriptor = entry.Descriptor;
        var modelFile = descriptor.ModelFile;
        try
        {
            DisposeSession(descriptor.Id);
            var folder = await GetFolderAsync();
            var copied = await picked.CopyAsync(folder, modelFile.Name + ".import", NameCollisionOption.ReplaceExisting);
            var size = (long)(await copied.GetBasicPropertiesAsync()).Size;
            var error = await Task.Run(async () =>
            {
                try
                {
                    using var session = await CreateSessionAsync(copied, descriptor);
                    return ValidateShape(session, descriptor);
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            });
            if (error is not null)
            {
                await copied.DeleteAsync(StorageDeleteOption.PermanentDelete);
                return error;
            }
            await copied.RenameAsync(modelFile.Name, NameCollisionOption.ReplaceExisting);
            _settings.SetImportedModelSize(descriptor.Id, size == modelFile.Size ? 0L : size);
            var auxiliary = descriptor.Files.Where(f => !f.IsModel).ToArray();
            if (auxiliary.Length > 0)
            {
                Post(() => entry.State = ModelState.Downloading);
                await DownloadFilesAsync(folder, entry, auxiliary, CancellationToken.None);
            }
            Post(() =>
            {
                entry.Error = null;
                entry.State = ModelState.Installed;
            });
            return null;
        }
        catch (Exception ex)
        {
            Post(() => entry.State = ModelState.NotInstalled);
            return ex.Message;
        }
    }

    public async Task DeleteAsync(ModelEntry entry)
    {
        DisposeSession(entry.Descriptor.Id);
        var folder = await GetFolderAsync();
        foreach (var file in entry.Descriptor.Files)
        {
            try
            {
                if (await folder.TryGetItemAsync(file.Name) is StorageFile stored)
                    await stored.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch
            {
            }
        }
        _settings.SetImportedModelSize(entry.Descriptor.Id, 0L);
        Post(() => entry.State = ModelState.NotInstalled);
    }

    public async Task<byte[]> ReadFileAsync(string id, string name)
    {
        var folder = await GetFolderAsync();
        var file = await folder.GetFileAsync(name);
        return await ReadAllBytesAsync(file);
    }

    public async Task<ModelLease> AcquireSessionAsync(string id)
    {
        var descriptor = ModelCatalog.Find(id) ?? throw new ArgumentException(id);
        SessionSlot slot;
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(id, out slot!))
            {
                slot = new SessionSlot();
                _sessions[id] = slot;
            }
            slot.Users++;
            slot.IdleTimer?.Dispose();
            slot.IdleTimer = null;
        }
        try
        {
            await slot.Gate.WaitAsync();
            try
            {
                if (slot.Session is null)
                {
                    var folder = await GetFolderAsync();
                    var file = await folder.GetFileAsync(descriptor.ModelFile.Name);
                    slot.Session = await Task.Run(() => CreateSessionAsync(file, descriptor));
                    var error = ValidateShape(slot.Session, descriptor);
                    if (error is not null)
                    {
                        slot.Session.Dispose();
                        slot.Session = null;
                        throw new InvalidDataException(error);
                    }
                }
            }
            finally
            {
                slot.Gate.Release();
            }
            return new ModelLease(this, id, slot.Session);
        }
        catch
        {
            Release(id);
            throw;
        }
    }

    internal void Release(string id)
    {
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(id, out var slot)) return;
            if (--slot.Users > 0) return;
            slot.IdleTimer?.Dispose();
            slot.IdleTimer = new Timer(_ => DisposeSession(id, onlyIdle: true), null, SessionIdleRelease, Timeout.InfiniteTimeSpan);
        }
    }

    private void DisposeSession(string id, bool onlyIdle = false)
    {
        InferenceSession? session = null;
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(id, out var slot)) return;
            if (onlyIdle && slot.Users > 0) return;
            slot.IdleTimer?.Dispose();
            slot.IdleTimer = null;
            if (slot.Users == 0)
            {
                session = slot.Session;
                _sessions.Remove(id);
            }
        }
        session?.Dispose();
    }

    public bool IsBusy
    {
        get
        {
            lock (_sessionLock)
                foreach (var slot in _sessions.Values)
                    if (slot.Users > 0) return true;
            return false;
        }
    }

    public void ReleaseIdleSessions()
    {
        string[] ids;
        lock (_sessionLock) ids = _sessions.Keys.ToArray();
        foreach (var id in ids) DisposeSession(id, onlyIdle: true);
    }

    private void DisposeAllSessions()
    {
        string[] ids;
        lock (_sessionLock) ids = _sessions.Keys.ToArray();
        foreach (var id in ids) DisposeSession(id);
    }

    private async Task<InferenceSession> CreateSessionAsync(StorageFile file, ModelDescriptor descriptor)
    {
        var label = descriptor.Id;
        var bytes = file.Path.StartsWith(ApplicationData.Current.LocalCacheFolder.Path, StringComparison.OrdinalIgnoreCase)
            ? null
            : await ReadAllBytesAsync(file);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (_settings.UseGpu)
        {
            try
            {
                var adapter = ResolveAdapter();
                using var gpuOptions = CreateOptions(true, descriptor, adapter.Index);
                var gpuSession = bytes is null ? new InferenceSession(file.Path, gpuOptions) : new InferenceSession(bytes, gpuOptions);
                HyPlayer.Features.Playback.AutoMix.AutoMixLog.Write($"model {label}: DirectML session on {adapter.Name} (#{adapter.Index}) in {watch.ElapsedMilliseconds} ms");
                return gpuSession;
            }
            catch (Exception ex)
            {
                HyPlayer.Features.Playback.AutoMix.AutoMixLog.Write($"model {label}: DirectML unavailable, using CPU ({ex.Message})");
                watch.Restart();
            }
        }
        using var cpuOptions = CreateOptions(false, descriptor, 0);
        var cpuSession = bytes is null ? new InferenceSession(file.Path, cpuOptions) : new InferenceSession(bytes, cpuOptions);
        HyPlayer.Features.Playback.AutoMix.AutoMixLog.Write($"model {label}: CPU session in {watch.ElapsedMilliseconds} ms");
        return cpuSession;
    }

    private static SessionOptions CreateOptions(bool gpu, ModelDescriptor descriptor, int adapterIndex)
    {
        var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 8, 1, 2),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            EnableCpuMemArena = false,
            EnableMemoryPattern = false
        };
        if (gpu)
        {
            foreach (var (name, value) in descriptor.FixedDimensions) options.AddFreeDimensionOverrideByName(name, value);
            if (descriptor.DisableGpuGraphOptimization) options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
            options.AppendExecutionProvider_DML(adapterIndex);
        }
        return options;
    }

    private static string? ValidateShape(InferenceSession session, ModelDescriptor descriptor)
    {
        if (!session.InputMetadata.TryGetValue(descriptor.InputName, out var input))
            return $"模型缺少输入 {descriptor.InputName}";
        var dims = input.Dimensions;
        if (dims.Length != descriptor.InputShape.Length) return "模型输入维度不符";
        for (var i = 0; i < dims.Length; i++)
            if (descriptor.InputShape[i] >= 0 && dims[i] >= 0 && dims[i] != descriptor.InputShape[i])
                return $"模型输入形状不符 [{string.Join(",", dims)}]";
        foreach (var output in descriptor.OutputNames)
            if (!session.OutputMetadata.ContainsKey(output)) return $"模型缺少输出 {output}";
        return null;
    }

    private static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
    {
        var size = (long)(await file.GetBasicPropertiesAsync()).Size;
        var bytes = new byte[size];
        await using var stream = await file.OpenStreamForReadAsync();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset));
            if (read == 0) break;
            offset += read;
        }
        return bytes;
    }

    private static void Post(Action action)
    {
        try
        {
            var dispatcher = CoreApplication.MainView.Dispatcher;
            if (dispatcher.HasThreadAccess) action();
            else _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }
        catch
        {
            action();
        }
    }

    private sealed class SessionSlot
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public InferenceSession? Session;
        public int Users;
        public Timer? IdleTimer;
    }
}

public sealed class ModelLease : IDisposable
{
    private readonly ModelManager _owner;
    private readonly string _id;
    private int _disposed;

    internal ModelLease(ModelManager owner, string id, InferenceSession session)
    {
        _owner = owner;
        _id = id;
        Session = session;
    }

    public InferenceSession Session { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _owner.Release(_id);
    }
}
