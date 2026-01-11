using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Classes;

public static class UpdateManager
{
    public enum UpdateSource
    {
        MicrosoftStore,
        AppCenter,
        AppCenterCanary,
        GitHub,
        Release,
        Canary,
        Dogfood
    }

    public class RemoteVersionResult
    {
        public UpdateSource UpdateSource { get; set; }
        public bool IsMandatory { get; set; }
#nullable enable
        public Version? Version { get; set; }
        public string? UpdateLog { get; set; }
#nullable restore
        public string DownloadLink { get; set; }
    }

    public static async Task<RemoteVersionResult> GetVersionFromStore()
    {
        var storeContext = StoreContext.GetDefault();
        var packageUpdates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
        var update = packageUpdates.FirstOrDefault();
        return new RemoteVersionResult
        {
            UpdateSource = UpdateSource.MicrosoftStore,
            IsMandatory = update?.Mandatory ?? false,
            Version = update == null
                ? null
                : new Version(update.Package.Id.Version.Major, update.Package.Id.Version.Minor,
                    update.Package.Id.Version.Build, update.Package.Id.Version.Revision),
            UpdateLog = update?.Package.Description,
            DownloadLink = "ms-windows-store://pdp/?productid=9N5TD916686K"
        };
    }

    class LatestApplicationUpdate
    {
        public string Version { get; set; }
        public DateTime Date { get; set; }
        public bool Mandatory { get; set; }
        public string DownloadUrl { get; set; }
        public string UpdateLog { get; set; }
        public int Size { get; set; }
    }

    public static async Task<RemoteVersionResult> GetVersionFromAppCenter(bool isCanary)
    {
        throw new NotSupportedException();
    }

    public static async Task<RemoteVersionResult> GetVersionFromSelfhost(UpdateSource source)
    {
        using var versionsResponse = await Common.HttpClient.GetAsync(
            new Uri($"https://hyplayer.kengwang.com.cn/Channel/{(source switch
            {
                UpdateSource.Canary => 5,
                UpdateSource.Release => 4,
                UpdateSource.Dogfood => 6,
                _ => 4,
            })}/latest"));
        if (!versionsResponse.IsSuccessStatusCode)
        {
            Common.AddToTeachingTipLists("获取更新失败", $"HTTP状态码:{versionsResponse.StatusCode}");
            throw new Exception("获取更新失败");
        }

        var versionResp =
            JsonSerializer.Deserialize<LatestApplicationUpdate>(await versionsResponse.Content.ReadAsStringAsync(), Common.DefaultOptions);
        return new RemoteVersionResult
        {
            UpdateSource = source,
            IsMandatory = versionResp?.Mandatory ?? false,
            Version = Version.Parse(versionResp?.Version ?? ""),
            DownloadLink = versionResp?.DownloadUrl,
            UpdateLog = versionResp?.UpdateLog ?? ""
        };
    }

    public static async Task<RemoteVersionResult> GetVersionFromGitHub()
    {
        throw new NotSupportedException();
    }

    public static async Task<RemoteVersionResult> GetRemoteVersion(UpdateSource updateSource)
    {
        return updateSource switch
        {
            UpdateSource.MicrosoftStore => await GetVersionFromStore(),
            _ => await GetVersionFromSelfhost(updateSource)
        };
    }

    public static Task PopupVersionCheck(bool isStartup = false)
    {
        return Task.Run(async () =>
        {
            var remoteResult = await GetRemoteVersion((UpdateSource)Common.Setting.UpdateSource);
            var localVersion = new Version(Package.Current.Id.Version.Major, Package.Current.Id.Version.Minor,
                Package.Current.Id.Version.Build, Package.Current.Id.Version.Revision);
            var title = "发现新版本";
            if (remoteResult.Version == null || remoteResult.Version <= localVersion)
            {
                if (isStartup) return;
                title = "你已是最新版";
            }

            var message = remoteResult.UpdateLog + "\r\n最新版本: " + remoteResult.Version + "\r\n当前版本: " +
                          localVersion + (remoteResult.IsMandatory ? "\r\n此版本为重要更新, 建议更新" : "");
            if (isStartup)
            {
                Common.AddToTeachingTipLists(title, message);
            }
            else
            {
                _ = Common.Invoke(async () =>
                {
                    ContentDialog contentDialog = new ContentDialog();
                    contentDialog.Title = title;
                    contentDialog.Content = message;
                    contentDialog.PrimaryButtonText = "更新";
                    contentDialog.PrimaryButtonClick += async (_, _) =>
                        await Windows.System.Launcher.LaunchUriAsync(
                            new Uri(remoteResult.DownloadLink));
                    contentDialog.CloseButtonText = "取消";
                    await contentDialog.ShowAsync();
                });
            }
        });

    }

    public static async Task GetUserCanaryChannelAvailability(string userEmail)
    {
        var userResp = await Common.HttpClient.GetAsync(new Uri($"https://hyplayer.kengwang.com.cn/user/email/{userEmail}"));
        if (userResp.IsSuccessStatusCode)
        {
            Common.AddToTeachingTipLists("Canary版本已解锁", "感谢您参加HyPlayer测试\nCanary版本现已解锁\n请到“关于”页面检测更新");
            Common.Setting.canaryChannelAvailability = true;
        }
        else
        {
            Common.Setting.canaryChannelAvailability = false;
            Common.AddToTeachingTipLists("未搜索到邮箱", "未搜索到此邮箱,请检查此邮箱是否是申请内测通道所使用的邮箱。\nCanary通道未能解锁");
            if (Common.Setting.UpdateSource == 2) Common.Setting.UpdateSource = 1;
        }
    }
    public class GitHubVersion
    {

    }
}