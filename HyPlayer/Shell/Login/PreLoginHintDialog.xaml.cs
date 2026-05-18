using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Pages;

public sealed partial class PreLoginHintDialog : ContentDialog
{
    public PreLoginHintDialog()
    {
        InitializeComponent();
    }

    public event Action? TutorialRequested;
    public event Action? RegisterDeviceRequested;

    private void TutorialLink_Click(object sender, RoutedEventArgs e)
    {
        TutorialRequested?.Invoke();
    }

    private void RegisterLink_Click(object sender, RoutedEventArgs e)
    {
        RegisterDeviceRequested?.Invoke();
    }
}
