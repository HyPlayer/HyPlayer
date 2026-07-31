using Windows.UI.Xaml;
using CommunityToolkit.WinUI.Converters;

namespace HyPlayer.UI.Converters;

public partial class NegationBoolToVisibilityConverter : BoolToObjectConverter
{
    public NegationBoolToVisibilityConverter()
    {
        TrueValue = Visibility.Visible;
        FalseValue = Visibility.Collapsed;
    }
}