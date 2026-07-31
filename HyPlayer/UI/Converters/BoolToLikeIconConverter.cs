using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters;

public abstract class BoolToLikeConverterBase : IValueConverter
{
    protected abstract string TrueValue { get; }

    protected abstract string FalseValue { get; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public partial class BoolToLikeIconConverter : BoolToLikeConverterBase
{
    protected override string TrueValue => "\uE10B";

    protected override string FalseValue => "\uE0B4";
}