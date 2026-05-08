using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Xaml;

namespace HyPlayer.Resources
{
    public sealed partial class Theme : ResourceDictionary
    {
        public Theme()
        {
            this.InitializeComponent();
            if (Ioc.Default.GetRequiredService<Setting>().IsOldThemeEnabled)
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
