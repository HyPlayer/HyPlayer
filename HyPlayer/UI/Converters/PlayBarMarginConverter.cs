using Windows.UI.Xaml;

namespace HyPlayer.UI.Converters;

public partial class PlayBarMarginConverter : PlayBarValueConverter
{
    protected override object ExpandedValue => new Thickness(12);

    protected override object CompactValue => new Thickness(0);
}