using Windows.UI.Xaml;

namespace HyPlayer.Classes
{
    public partial class PlayBarCornerRadiusConverter : PlayBarValueConverter
    {
        protected override object ExpandedValue => new CornerRadius(4);

        protected override object CompactValue => new CornerRadius(8, 8, 0, 0);
    }
}
