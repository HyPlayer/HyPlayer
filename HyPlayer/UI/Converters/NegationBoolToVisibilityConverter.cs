using CommunityToolkit.WinUI.Converters;
using Windows.UI.Xaml;

namespace HyPlayer.UI.Converters
{
    public partial class NegationBoolToVisibilityConverter : BoolToObjectConverter
    {
        public NegationBoolToVisibilityConverter()
        {
            base.TrueValue = Visibility.Visible;
            base.FalseValue = Visibility.Collapsed;
        }
    }
}
