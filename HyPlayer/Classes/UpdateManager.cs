using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http;

namespace HyPlayer.Classes;

public static class UpdateManager
{
    public enum UpdateSource
    {
        MicrosoftStore,
        AppCenter,
        AppCenterCanary,
        GitHub
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
        using var versionsResponse = await Common.HttpClient.TryGetAsync(
            new Uri($"https://hyplayer.kengwang.com.cn/Channel/{(isCanary ? 2 : 3)}/latest"));
        if (!versionsResponse.Succeeded)
        {
            Common.AddToTeachingTipLists("获取更新失败", await versionsResponse.ResponseMessage.Content.ReadAsStringAsync());
            throw new Exception("获取更新失败");
        }

        var versionResp =
            JsonConvert.DeserializeObject<LatestApplicationUpdate>(await versionsResponse.ResponseMessage.Content.ReadAsStringAsync());
        return new RemoteVersionResult
        {
            UpdateSource = isCanary ? UpdateSource.AppCenterCanary : UpdateSource.AppCenter,
            IsMandatory = versionResp?.Mandatory ?? false,
            Version = Version.Parse(versionResp?.Version ?? ""),
            DownloadLink = versionResp?.DownloadUrl,
            UpdateLog = versionResp?.UpdateLog ?? ""
        };
    }
    public static async Task<RemoteVersionResult> GetVersionFromGitHub()
    {
        using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/repos/HyPlayer/HyPlayer/releases/latest"));
        message.Headers.Add("user-agent", "HyPlayer-UpdateChecker");
        using var versionsResponse = await Common.HttpClient.TrySendRequestAsync(message);
        if (!versionsResponse.Succeeded)
        {
            Common.AddToTeachingTipLists("获取更新失败", await versionsResponse.ResponseMessage.Content.ReadAsStringAsync());
            throw new Exception("获取更新失败");
        }
        var versionData =
            JObject.Parse(await versionsResponse.ResponseMessage.Content.ReadAsStringAsync());
        var versionResp = new LatestApplicationUpdate()
        {
            Version = versionData["tag_name"].ToString(),
            Date = DateTime.Parse(versionData["published_at"].ToString()),
            Mandatory = false,
            DownloadUrl = versionData["html_url"].ToString(),
            UpdateLog = versionData["body"].ToString(),
        };
        var result = new RemoteVersionResult
        {
            UpdateSource = UpdateSource.GitHub,
            IsMandatory = versionResp?.Mandatory ?? false,
            Version = Version.Parse(versionResp?.Version ?? ""),
            DownloadLink = versionResp?.DownloadUrl,
            UpdateLog = versionResp?.UpdateLog ?? ""
        };
        versionData.RemoveAll();
        return result;
    }

    public static async Task<RemoteVersionResult> GetRemoteVersion(UpdateSource updateSource)
    {
        return updateSource switch
        {
            UpdateSource.MicrosoftStore => await GetVersionFromStore(),
            UpdateSource.AppCenter => await GetVersionFromAppCenter(false),
            UpdateSource.AppCenterCanary => await GetVersionFromAppCenter(true),
            UpdateSource.GitHub => await GetVersionFromGitHub(),
            _ => throw new ArgumentOutOfRangeException(nameof(updateSource), updateSource, null)
        };
    }

    public static async Task PopupVersionCheck(bool isStartup = false)
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
        var userResp = await Common.HttpClient.TryGetAsync(new Uri($"https://hyplayer.kengwang.com.cn/user/email/{userEmail}"));
        if (userResp.Succeeded)
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