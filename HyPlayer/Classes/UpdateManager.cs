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
        AppCenter
    }

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

    public static async Task<RemoteVersionResult> GetVersionFromAppCenter()
    {
        var versionsGetter = new WebClient();
        versionsGetter.Headers.Add("X-API-Token", "50f1aa0749d70814b0e91493444759885119a58d");
        var versions =
            JArray.Parse(
                await versionsGetter.DownloadStringTaskAsync(
                    "https://api.appcenter.ms/v0.1/apps/kengwang/HyPlayer/distribution_groups/Public/releases"));
        if (versions?.First?["version"] == null) return new RemoteVersionResult();
        return new RemoteVersionResult()
        {
            UpdateSource = UpdateSource.AppCenter,
            IsMandatory = versions["mandatory_update"]?.ToString() == "False",
            Version = Version.Parse(versions?["version"]?.ToString() ?? ""),
            UpdateLog = $"更新发布于 {versions["uploaded_at"]}"
        };
    }

    public static async Task<RemoteVersionResult> GetRemoteVersion(UpdateSource updateSource)
    {
        return updateSource switch
        {
            UpdateSource.MicrosoftStore => await GetVersionFromStore(),
            UpdateSource.AppCenter => await GetVersionFromAppCenter(),
            _ => throw new ArgumentOutOfRangeException(nameof(updateSource), updateSource, null)
        };
    }

    public static async Task PopupVersionCheck(bool isStartup = false)
    {
        var remoteResult = await GetRemoteVersion((UpdateSource)Common.Setting.UpdateSource);
        var localVersion = new Version(Package.Current.Id.Version.Major, Package.Current.Id.Version.Minor,
            Package.Current.Id.Version.Build, Package.Current.Id.Version.Revision);
        var title = "发现新版本";
        if (remoteResult.Version != null || remoteResult.Version == localVersion)
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
                    new Uri(@"ms-windows-store://pdp/?productid=9N5TD916686K"));
            contentDialog.PrimaryButtonText = "取消";
            await contentDialog.ShowAsync();
        }
    }
}