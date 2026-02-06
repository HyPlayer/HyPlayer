namespace HyPlayer.Classes
{
    public enum RomajiSource : int
    {
        None,
        AutoSelect,
        NeteaseOnly,
        KawazuOnly
    }

    public enum BackgroundType : int
    {
        CoverBlur = 0,
        CoverTheme = 1,
        DesktopAcrylic = 5,
        Animated = 6,
        Isolation = 7
    }
    public enum ExpandedWindowMode
    {
        Both,
        CoverOnly,
        LyricOnly
    }
}
