using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TagLib.Mpeg;
using Windows.UI.Xaml;

namespace HyPlayer.Resources
{
    public sealed partial class Theme : ResourceDictionary
    {
        public Theme()
        {
            this.InitializeComponent();
            if (Common.Setting.IsOldThemeEnabled)
            {
                MergedDictionaries.Add(new XamlControlsResources { ControlsResourcesVersion = ControlsResourcesVersion.Version1 });
            }
            else
            {
                MergedDictionaries.Add(new XamlControlsResources { ControlsResourcesVersion = ControlsResourcesVersion.Version2 });
            }
        }
    }


}
