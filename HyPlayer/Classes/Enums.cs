namespace HyPlayer.Classes
{
    public enum RomajiSource : int
    {
        None = 0,
        AutoSelect = 1,
        NeteaseOnly = 2,
        KawazuOnly = 3
    }

    public enum BackgroundType : int
    {
        CoverBlur = 0,
        CoverTheme = 1,
        DesktopAcrylic = 2,
        Animated = 3,
        Isolation = 4
    }
    public enum ExpandedWindowMode : int
    {
        Both = 0,
        CoverOnly = 1,
        LyricOnly = 2
    }
    public enum UpdateSource : int
    {
        MicrosoftStore = 0,
        Release = 1,
        Canary = 2,
        GitHub = 3,
        CI = 4
    }
    public enum LyricColor : int
    {
        Auto = 0,
        White = 1,
        Black = 2,
        FollowCover = 3
    }
    public enum OccupySolution : int
    {
        Skip = 0,
        ReWrite = 1,
        AppendID = 2,
        UpdateInfo = 3
    }
    public enum LyricAlignment : int
    {
        Left = 0,
        Center = 1,
        Right = 2
    }
    public enum GestureMode : int
    {
        Basic = 0,
        Shift = 1,
        DJ = 2,
        RealDJ = 3
    }
    public enum ColorGeneratorType : int
    {
        KMeans = 0,
        OctTree = 1,
        Auto = 2
    }
    public enum PlayMode : int
    {
        DefaultRoll = 0,
        SinglePlay = 1,
        Shuffled = 2
    }
    public enum ThemeRequest : int
    {
        Auto = 0,
        Light = 1,
        Dark = 2
    }
    public enum RollingCalculator : int
    {
        ElasticEaseRollingCalculator = 0,
        SinRollingCalculator = 1,
        LyricifyRollingCalculator = 2,
        SyncRollingCalculator = 3,
        CircleEaseRollingCalculator = 4
    }
}
