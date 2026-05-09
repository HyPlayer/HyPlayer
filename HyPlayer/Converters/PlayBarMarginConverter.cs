using Windows.UI.Xaml;

namespace HyPlayer.Classes
{
    public partial class PlayBarMarginConverter : PlayBarValueConverter
    {
        protected override object ExpandedValue => new Thickness(16);

        protected override object CompactValue => new Thickness(0);
    }
}
