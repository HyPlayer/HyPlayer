using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeteaseCloudMusicApi;
using Windows.UI.Xaml.Controls;

namespace HyPlayer
{
    class Common
    {
        public static NeteaseCloudMusicApi.CloudMusicApi ncapi = new CloudMusicApi();
        public static bool Logined = false;
        public static Frame MainFrame;
    }
}
