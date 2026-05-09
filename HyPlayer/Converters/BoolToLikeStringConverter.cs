namespace HyPlayer.Classes
{
    public partial class BoolToLikeStringConverter : BoolToLikeConverterBase
    {
        protected override string TrueValue => "已收藏";

        protected override string FalseValue => "收藏";
    }
}
