#nullable enable
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;

namespace HyPlayer.UI.Dialogs;

public sealed partial class ThirdPartyLyricSourcesDialog : ContentDialog
{
    public ThirdPartyLyricSourcesDialog()
    {
        InitializeComponent();
    }

    public LyricSettings Lyric { get; } = Ioc.Default.GetRequiredService<LyricSettings>();
}
