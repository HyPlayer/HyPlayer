using CommunityToolkit.WinUI.Converters;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public class NegationBoolToVisibilityConverter : BoolToObjectConverter
    {
        public NegationBoolToVisibilityConverter()
        {
            base.TrueValue = Visibility.Visible;
            base.FalseValue = Visibility.Collapsed;
        }
    }
}
