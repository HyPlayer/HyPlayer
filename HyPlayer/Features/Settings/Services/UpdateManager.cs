using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Serialization;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
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
using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Features.Settings.Services;

public static class UpdateManager
{
    public class RemoteVersionResult
    {
        public UpdateSource UpdateSource { get; set; }
        public bool IsMandatory { get; set; }
#nullable enable
        public Version? Version { get; set; }
        public string? UpdateLog { get; set; }
        public DateTime? UpdateTime { get; set; }
#nullable restore
        public string DownloadLink { get; set; }
    }

    public static async Task<RemoteVersionResult> GetVersionFromStore()
    {
        var storeContext = StoreContext.GetDefault();
        var packageUpdates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
        var update = packageUpdates[0];
        return new RemoteVersionResult
        {
            UpdateSource = UpdateSource.MicrosoftStore,
            IsMandatory = update?.Mandatory ?? false,
            Version = update == null
                ? null
                : new Version(update.Package.Id.Version.Major, update.Package.Id.Version.Minor,
                    update.Package.Id.Version.Build, update.Package.Id.Version.Revision),
            UpdateLog = update?.Package.Description,
            DownloadLink = "ms-windows-store://pdp/?productid=9N5TD916686K",
        };
    }

    public class LatestApplicationUpdate
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("date")]
        public DateTime Date { get; set; }
        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; set; }
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; }
        [JsonPropertyName("updateLog")]
        public string UpdateLog { get; set; }
        [JsonPropertyName("size")]
        public int Size { get; set; }
    }
    public class GitHubReleaseResponse
    {
        [JsonPropertyName("html_url")]
        public string Url { get; set; }
        [JsonPropertyName("tag_name")]
        public string Tag { get; set; }
        [JsonPropertyName("updated_at")]
        public DateTime UpdateTime { get; set; }
        [JsonPropertyName("body")]
        public string Body { get; set; }
    }

    public static async Task<RemoteVersionResult> GetVersionFromGitHub(UpdateSource source)
    {
        using var message = new HttpRequestMessage()
        {
            RequestUri = new Uri($"https://api.github.com/repos/HyPlayer/HyPlayer/releases/{source switch
            {
                UpdateSource.GitHub => "latest",
                UpdateSource.CI => "tags/actions-build",
                _ => "latest"
            }}"),
            Method = HttpMethod.Get
        };
        message.Headers.Add("User-Agent", "HyPlayer-Update-Client");
        using var versionsResponse = await Ioc.Default.GetRequiredService<HttpClient>().SendAsync(message);
        if (!versionsResponse.IsSuccessStatusCode)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("获取更新失败", $"HTTP状态码:{versionsResponse.StatusCode}");
        }
        var resp = await versionsResponse.Content.ReadAsStringAsync();
        var versionResp =
            JsonSerializer.Deserialize<GitHubReleaseResponse>(resp, JsonDefaults.Options);
        var version = Version.TryParse(versionResp?.Tag, out var versionResult);
        return new RemoteVersionResult
        {
            UpdateSource = source,
            IsMandatory = false,
            Version = versionResult,
            DownloadLink = versionResp?.Url,
            UpdateLog = versionResp?.Body ?? "",
            UpdateTime = versionResp?.UpdateTime
        };
    }

    public static async Task<RemoteVersionResult> GetVersionFromSelfhost(UpdateSource source)
    {
        using var versionsResponse = await Ioc.Default.GetRequiredService<HttpClient>().GetAsync(
            new Uri($"https://hyplayer.kengwang.com.cn/Channel/{(source switch
            {
                UpdateSource.Release => 3,
                UpdateSource.Canary => 2,
                _ => 3,
            })}/latest"));
        if (!versionsResponse.IsSuccessStatusCode)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("获取更新失败", $"HTTP状态码:{versionsResponse.StatusCode}");
        }
        var resp = await versionsResponse.Content.ReadAsStringAsync();
        var versionResp =
            JsonSerializer.Deserialize<LatestApplicationUpdate>(resp, JsonDefaults.Options);
        return new RemoteVersionResult
        {
            UpdateSource = source,
            IsMandatory = versionResp?.Mandatory ?? false,
            Version = Version.Parse(versionResp?.Version ?? ""),
            DownloadLink = versionResp?.DownloadUrl,
            UpdateLog = versionResp?.UpdateLog ?? "",
            UpdateTime = versionResp.Date
        };
    }

    public static async Task<RemoteVersionResult> GetRemoteVersion(UpdateSource updateSource)
    {
        return updateSource switch
        {
            UpdateSource.MicrosoftStore => await GetVersionFromStore(),
            UpdateSource.CI => await GetVersionFromGitHub(updateSource),
            UpdateSource.GitHub => await GetVersionFromGitHub(updateSource),
            _ => await GetVersionFromSelfhost(updateSource)
        };
    }

    public static Task PopupVersionCheck(bool isStartup = false)
    {
        return Task.Run((Func<Task?>)(async () =>
        {
            var remoteResult = await GetRemoteVersion((UpdateSource)Ioc.Default.GetRequiredService<Setting>().UpdateSource);
            var localVersion = new Version(Package.Current.Id.Version.Major, Package.Current.Id.Version.Minor,
                Package.Current.Id.Version.Build, Package.Current.Id.Version.Revision);
            var title = "发现新版本";
            if (remoteResult.Version == null || remoteResult.Version <= localVersion)
            {
                if (isStartup) return;
                title = "你已是最新版";
            }

            var message = remoteResult.UpdateLog +
                          "\r\n最新版本: " + remoteResult.Version +
                          "\r\n更新时间: " + remoteResult.UpdateTime.ToString() +
                          "\r\n当前版本: " + localVersion +
                          (remoteResult.IsMandatory ? "\r\n此版本为重要更新, 建议更新" : "");
            if (isStartup)
            {
                Ioc.Default.GetRequiredService<INotificationService>().ShowMessage(title, message);
            }
            else
            {
                _ = Ioc.Default.GetRequiredService<INotificationService>().InvokeOnUIThread(async () =>
                {
                    ContentDialog contentDialog = new()
                    {
                        Title = title,
                        Content = message,
                        PrimaryButtonText = "更新"
                    };
                    contentDialog.PrimaryButtonClick += async (_, _) =>
                        await Windows.System.Launcher.LaunchUriAsync(
                            new Uri(remoteResult.DownloadLink));
                    contentDialog.CloseButtonText = "取消";
                    await contentDialog.ShowAsync();
                });
            }
        }));

    }

    public static async Task GetUserCanaryChannelAvailability(string userEmail)
    {
        var httpClient = Ioc.Default.GetRequiredService<HttpClient>();
        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        var setting = Ioc.Default.GetRequiredService<Setting>();
        var userResp = await httpClient.GetAsync(new Uri($"https://hyplayer.kengwang.com.cn/user/email/{userEmail}"));
        if (userResp.IsSuccessStatusCode)
        {
            notification.ShowMessage("Canary版本已解锁", "感谢您参加HyPlayer测试\nCanary版本现已解锁\n请到“关于”页面检测更新");
            setting.canaryChannelAvailability = true;
        }
        else
        {
            setting.canaryChannelAvailability = false;
            notification.ShowMessage("未搜索到邮箱", "未搜索到此邮箱,请检查此邮箱是否是申请内测通道所使用的邮箱。\nCanary通道未能解锁");
            if (setting.UpdateSource == UpdateSource.Canary) setting.UpdateSource = UpdateSource.Release;
        }
    }
}
