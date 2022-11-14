using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using HyPlayer.HyPlayControl;

namespace HyPlayer.Classes
{
    internal static class LastFMManager
    {
        public delegate void LoginDoneEvent();
        public delegate void LoginErrorEvent(Exception exception);
        public delegate void LogoffDoneEvent();
        public static event LoginDoneEvent OnLoginDone;
        public static event LoginErrorEvent OnLoginError;
        public static event LogoffDoneEvent OnLogoffDone;
        public static string LastFMAPIKey = "641ef15109503085d966e37b73bdcb72";
        public static string LastFMAPISecret = "35c02c12c9c0fdc6f6c1de5d0a9227b5";
        public static bool LastfmLogined => LastfmClient.Auth.Authenticated;
        private static LastfmClient LastfmClient = new LastfmClient(LastFMAPIKey, LastFMAPISecret);
        public static void InitializeLastFMManager()
        {
            OnLoginDone += LastFMManager_OnLoginDone;
            OnLogoffDone += LastFMManager_OnLogoffDone;
            OnLoginError+=LastFMManager_OnLoginError;
            HyPlayList.OnPlayItemChange += HyPlayList.UpdateLastFMNowPlayingAsync;
            TryLoginLastfmAccountFromSession();
        }

        public static void LastFMManager_OnLogoffDone()
        {
            Common.Setting.LastFMUserName = null;
            Common.Setting.LastFMToken = null;
            Common.Setting.LastFMIsSubscriber = false;
        }

        public static void LastFMManager_OnLoginDone()
        {
            Common.Setting.LastFMUserName = LastfmClient.Auth.UserSession.Username;
            Common.Setting.LastFMToken = LastfmClient.Auth.UserSession.Token;
            Common.Setting.LastFMIsSubscriber = LastfmClient.Auth.UserSession.IsSubscriber;
        }
        public static void LastFMManager_OnLoginError(Exception ex)
        {
            if (LastfmLogined)
            {
                LastfmClient.Dispose();
                LastfmClient = new LastfmClient(LastFMAPIKey, LastFMAPISecret);
            }
            OnLogoffDone.Invoke();
        }

        public static async Task TryLoginLastfmAccountFromInternet(string userName, string password)
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password)) OnLoginError.Invoke(new Exception("用户名或密码不能为空"));
            var response = await LastfmClient.Auth.GetSessionTokenAsync(userName,password);
            if (response.Success) OnLoginDone.Invoke();
            else
            {
                OnLoginError.Invoke(new Exception(response.Status.ToString()));
            }
        }
        public static void TryLoginLastfmAccountFromSession()
        {
            if (string.IsNullOrEmpty(Common.Setting.LastFMUserName) || string.IsNullOrEmpty(Common.Setting.LastFMToken)) return;
            LastUserSession session = new LastUserSession
            {
                Username = Common.Setting.LastFMUserName,
                Token = Common.Setting.LastFMToken,
                IsSubscriber = Common.Setting.LastFMIsSubscriber
            };
            LastfmClient.Auth.LoadSession(session);
        }
        public static async Task TryLoginLastfmAccountFromBrowser(string token)
        {
            var signature = LastFMUtils.GetLastFMAPISignature(token);
            HttpResponseMessage responseData = new();
            HttpClient httpClient = new HttpClient();
            try
            {
                responseData = await httpClient.GetAsync($"https://ws.audioscrobbler.com/2.0/?method=auth.getSession&format=json&token={token}&api_key={LastFMAPIKey}&api_sig={signature}");
                if (responseData != null && responseData.IsSuccessStatusCode)
                {
                    string sessionStringData = await responseData.Content.ReadAsStringAsync();
                    JObject sessionJsonObject = JObject.Parse(sessionStringData);
                    var session = new LastUserSession()
                    {
                        Username = sessionJsonObject["session"]["name"].ToString(),
                        Token = sessionJsonObject["session"]["key"].ToString(),
                        IsSubscriber = (bool)sessionJsonObject["session"]["subscriber"]
                    };
                    LastfmClient.Auth.LoadSession(session);
                    OnLoginDone.Invoke();
                }
                else if (responseData!=null)
                {
                    string errorMessageStringData = await responseData.Content.ReadAsStringAsync();
                    JObject errorMessageJsonObject = JObject.Parse(errorMessageStringData);
                    OnLoginError.Invoke(new Exception(errorMessageJsonObject["message"].ToString()));
                }
            }
            catch (Exception ex)
            {
                OnLoginError.Invoke(ex);
            }
            httpClient.Dispose();
        }
        public static bool TryLogoffLastFM()
        {
            LastfmClient.Dispose();
	        LastfmClient = new LastfmClient(LastFMAPIKey, LastFMAPISecret);
            OnLogoffDone.Invoke();
            return true;
        }
        public static async Task<bool> ScrobbleAsync(HyPlayItem scrobbleHyPlayItem)
        {
            if (scrobbleHyPlayItem is null) return false;
            if (!LastfmLogined || !Common.Setting.UseLastFMScrobbler) return false;
            var scrobbleItem = LastFMUtils.GetScrobble(scrobbleHyPlayItem);
            var response= await LastfmClient.Scrobbler.ScrobbleAsync(scrobbleItem);
            if (!response.Success)
            {
                if (response.Status == LastResponseStatus.BadAuth) OnLoginError.Invoke(new Exception(response.Status.ToString()));
                throw response.Exception;
            }
            return response.Success;
        }
        public static async Task<bool> UpdateNowPlayingAsync(HyPlayItem nowPlayingHyPlayItem)
        {
            if (nowPlayingHyPlayItem is null) return false;
            if (!LastfmLogined || !Common.Setting.UpdateLastFMNowPlaying) return false;
            var nowPlayingItem = LastFMUtils.GetScrobble(nowPlayingHyPlayItem);
            var response = await LastfmClient.Track.UpdateNowPlayingAsync(nowPlayingItem);
            if (!response.Success)
            {
                if (response.Status == LastResponseStatus.BadAuth) OnLoginError.Invoke(new Exception(response.Status.ToString()));
                throw new Exception(response.Status.ToString());
            }
            return response.Success;
        }
    }
    public static class LastFMUtils
    {
        public static Scrobble GetScrobble(HyPlayItem scrobbleHyPlayItem) => new Scrobble(scrobbleHyPlayItem.PlayItem.ArtistString, scrobbleHyPlayItem.PlayItem.AlbumString, scrobbleHyPlayItem.PlayItem.Name, DateTimeOffset.UtcNow);
        public static string GetLastFMAPISignature(string token)
        {

            var byteData = HyPlayer.Extensions.ToByteArrayUtf8($"api_key{LastFMManager.LastFMAPIKey}methodauth.getSessiontoken{token}{LastFMManager.LastFMAPISecret}");
            var md5 = HyPlayer.Extensions.ComputeMd5(byteData);
            StringBuilder stringBuilder = new StringBuilder();
            foreach (var data in md5)
            {
                stringBuilder.Append(data.ToString("x2"));
            }
            return stringBuilder.ToString();
        }
    }
}
