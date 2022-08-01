using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.UI.Xaml.Controls;
using Newtonsoft.Json.Linq;

namespace HyPlayer.Classes;

public static class UpdateManager
{
    public enum UpdateSource
    {
        MicrosoftStore,
        AppCenter,
        AppCenterCanary
    }
    public class RemoteVersionResult
    {
        public UpdateSource UpdateSource { get; set; }
        public bool IsMandatory { get; set; }
        public Version? Version { get; set; }
        public string? UpdateLog { get; set; }
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

    public static async Task<RemoteVersionResult> GetVersionFromAppCenter(bool isCanary)
    {
        var versionsGetter = new WebClient();
        versionsGetter.Headers.Add("X-API-Token", "50f1aa0749d70814b0e91493444759885119a58d");
        var versions =
            JObject.Parse(
                await versionsGetter.DownloadStringTaskAsync(
                    $"https://api.appcenter.ms/v0.1/apps/kengwang/hyplayer/distribution_groups/{(isCanary ? "canary" : "release")}/releases/latest"));
        if (versions?["version"] == null) return new RemoteVersionResult();
        return new RemoteVersionResult()
        {
            UpdateSource = isCanary ? UpdateSource.AppCenterCanary : UpdateSource.AppCenter,
            IsMandatory = versions["mandatory_update"]?.ToString()=="True",
            Version = Version.Parse(versions["version"]?.ToString() ?? ""),
            UpdateLog = "更新日志:\n" + versions["release_notes"]?.ToString().Replace("* ","") + $"\n更新发布于 {TimeZoneInfo.ConvertTimeFromUtc(DateTime.Parse(versions["uploaded_at"]?.ToString()), TimeZoneInfo.Local)}",
            DownloadLink = versions["download_url"]?.ToString()
        };
    }

    public static async Task<RemoteVersionResult> GetRemoteVersion(UpdateSource updateSource)
    {
        return updateSource switch
        {
            UpdateSource.MicrosoftStore => await GetVersionFromStore(),
            UpdateSource.AppCenter => await GetVersionFromAppCenter(false),
            UpdateSource.AppCenterCanary => await GetVersionFromAppCenter(true),
            _ => throw new ArgumentOutOfRangeException(nameof(updateSource), updateSource, null)
        };
    }

    public static async Task PopupVersionCheck(bool isStartup = false)
    {
        var remoteResult = await GetRemoteVersion((UpdateSource)Common.Setting.UpdateSource);
        var localVersion = new Version(Package.Current.Id.Version.Major, Package.Current.Id.Version.Minor,
            Package.Current.Id.Version.Build, Package.Current.Id.Version.Revision);
        var title = "发现新版本";
        if (remoteResult.Version == null || remoteResult.Version == localVersion)
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
            ContentDialog contentDialog = new ContentDialog();
            contentDialog.Title = title;
            contentDialog.Content = message;
                contentDialog.PrimaryButtonText = "更新";
                contentDialog.PrimaryButtonClick += async (_, _) =>
                    await Windows.System.Launcher.LaunchUriAsync(
                        new Uri(remoteResult.DownloadLink));
            contentDialog.CloseButtonText = "取消";
            await contentDialog.ShowAsync();
        }
    }
    public static async Task GetUserCanaryChannelAvailability(string userEmail)
    {
        var usersGetter = new WebClient();
        usersGetter.Headers.Add("X-API-Token", "50f1aa0749d70814b0e91493444759885119a58d");
        var users = JArray.Parse(
                await usersGetter.DownloadStringTaskAsync(
                    "https://api.appcenter.ms/v0.1/apps/kengwang/HyPlayer/distribution_groups/Canary/members"));
        if (users.Where(t => t["email"].ToString() == userEmail).FirstOrDefault() != null)
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
}