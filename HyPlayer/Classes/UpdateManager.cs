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

    public static string[] UpdateSourceLink =
    {
        "ms-windows-store://pdp/?productid=9N5TD916686K",
        "https://install.appcenter.ms/users/kengwang/apps/hyplayer/distribution_groups/public",
        "https://install.appcenter.ms/users/kengwang/apps/hyplayer/distribution_groups/canary"
    };

    public class RemoteVersionResult
    {
        public UpdateSource UpdateSource { get; set; }
        public bool IsMandatory { get; set; }
        public Version? Version { get; set; }
        public string? UpdateLog { get; set; }
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
            UpdateLog = update?.Package.Description
        };
    }

    public static async Task<RemoteVersionResult> GetVersionFromAppCenter(bool isCanary)
    {
        var versionsGetter = new WebClient();
        versionsGetter.Headers.Add("X-API-Token", "50f1aa0749d70814b0e91493444759885119a58d");
        var versions =
            JArray.Parse(
                await versionsGetter.DownloadStringTaskAsync(
                    $"https://api.appcenter.ms/v0.1/apps/kengwang/HyPlayer/distribution_groups/{(isCanary ? "Canary" : "Release")}/releases"));
        if (versions?.First?["version"] == null) return new RemoteVersionResult();
        return new RemoteVersionResult()
        {
            UpdateSource = isCanary ? UpdateSource.AppCenterCanary : UpdateSource.AppCenter,
            IsMandatory = versions.First["mandatory_update"]?.ToString()=="True",
            Version = Version.Parse(versions.First?["version"]?.ToString() ?? ""),
            UpdateLog = $"更新发布于 {versions.First["uploaded_at"]}"
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
            contentDialog.SecondaryButtonText = "更新";
            contentDialog.SecondaryButtonClick += async (_, _) =>
                await Windows.System.Launcher.LaunchUriAsync(
                    new Uri(UpdateSourceLink[Common.Setting.UpdateSource]));
            contentDialog.PrimaryButtonText = "取消";
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
        foreach (var user in users)
        {
            if (user["email"].ToString() == userEmail)
            {
                Common.AddToTeachingTipLists("Canary版本已解锁","感谢您参加HyPlayer测试\nCanary版本现已解锁\n请到“关于”页面检测更新");
                Common.Setting.canaryChannelAvailability = true;
                return;
            }  
        }
        Common.Setting.canaryChannelAvailability = false;
        Common.AddToTeachingTipLists("未搜索到邮箱","未搜索到此邮箱,请检查此邮箱是否是申请内测通道所使用的邮箱。\nCanary通道未能解锁");
    }
}