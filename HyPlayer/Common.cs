using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Kawazu;

namespace HyPlayer
{
    internal class Common
    {
        public static NeteaseCloudMusicApi.CloudMusicApi ncapi = new CloudMusicApi();
        public static bool Logined = false;
        public static NCUser LoginedUser;
        public static ExpandedPlayer PageExpandedPlayer;
        public static MainPage PageMain;
        public static PlayBar BarPlayBar;
        public static Frame BaseFrame;
        public static Setting Setting;
        public static bool ShowLyricSound = true;
        public static bool ShowLyricTrans = true;
        public static Dictionary<string, object> GLOBAL = new Dictionary<string, object>();
        public static List<string> LikedSongs = new List<string>();
        public static KawazuConverter KawazuConv = null;

        public static async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            try
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority,
                    () => { action(); });
            }
            catch (Exception e)
            {
                /*
                Invoke((async () =>
                {
                    await new ContentDialog
                    {
                        Title = "发生错误",
                        Content = "Error: " + e.Message + "\r\n" + e.StackTrace,
                        CloseButtonText = "关闭",
                        DefaultButton = ContentDialogButton.Close
                    }.ShowAsync();
                }));
                */
            }

        }
    }

    internal struct Setting
    {
        public string audioRate
        {
            get
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("audioRate"))
                    return ApplicationData.Current.LocalSettings.Values["audioRate"].ToString();
                return "999000";
            }
            set => ApplicationData.Current.LocalSettings.Values["audioRate"] = value;
        }

        public int Volume
        {
            get
            {
                try
                {
                    if (ApplicationData.Current.LocalSettings.Values.ContainsKey("Volume"))
                        return int.Parse(ApplicationData.Current.LocalSettings.Values["Volume"].ToString());
                }
                catch
                {
                    return 1;
                }
                return 1;
            }

            set => ApplicationData.Current.LocalSettings.Values["Volume"] = value;
        }

        public string downloadDir
        {
            get
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("downloadDir"))
                    return ApplicationData.Current.LocalSettings.Values["downloadDir"].ToString();
                return ApplicationData.Current.LocalCacheFolder.Path;
            }
            set => ApplicationData.Current.LocalSettings.Values["downloadDir"] = value;
        }
    }

    internal static class Extensions
    {
        public static byte[] ToByteArrayUtf8(this string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        public static string ToHexStringLower(this byte[] value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte t in value)
            {
                sb.Append(t.ToString("x2"));
            }

            return sb.ToString();
        }

        public static string ToHexStringUpper(this byte[] value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte t in value)
            {
                sb.Append(t.ToString("X2"));
            }

            return sb.ToString();
        }

        public static string ToBase64String(this byte[] value)
        {
            return Convert.ToBase64String(value);
        }

        public static byte[] ComputeMd5(this byte[] value)
        {
            MD5 md5 = MD5.Create();
            return md5.ComputeHash(value);
        }

        public static byte[] RandomBytes(this Random random, int length)
        {
            byte[] buffer = new byte[length];
            random.NextBytes(buffer);
            return buffer;
        }

        public static string Get(this CookieCollection cookies, string name, string defaultValue)
        {
            return cookies[name]?.Value ?? defaultValue;
        }
    }
}
