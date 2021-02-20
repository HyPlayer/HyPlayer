using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeteaseCloudMusicApi;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.Pages;

namespace HyPlayer
{
    class Common
    {
        public static NeteaseCloudMusicApi.CloudMusicApi ncapi = new CloudMusicApi();
        public static bool Logined = false;
        public static LoginedUser LoginedUser;
        public static ExpandedPlayer PageExpandedPlayer;
        public static MainPage PageMain;
        public static PlayBar BarPlayBar;
        public static Frame BaseFrame;
        public static Dictionary<string,object> GLOBAL = new Dictionary<string, object>();
    }
}
