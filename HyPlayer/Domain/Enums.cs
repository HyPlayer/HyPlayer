namespace HyPlayer.Domain;

public enum RomajiSource
{
    None = 0,
    AutoSelect = 1,
    NeteaseOnly = 2,
    KawazuOnly = 3
}

public enum BackgroundType
{
    CoverBlur = 0,
    CoverTheme = 1,
    Animated = 2,
    Isolation = 3,
    LikeApple = 4
}

public enum ExpandedWindowMode
{
    Both = 0,
    CoverOnly = 1,
    LyricOnly = 2
}

public enum UpdateSource
{
    MicrosoftStore = 0,
    Release = 1,
    Canary = 2,
    GitHub = 3,
    CI = 4
}


public enum OccupySolution
{
    Skip = 0,
    ReWrite = 1,
    AppendID = 2,
    UpdateInfo = 3
}

public enum LyricAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

public enum GestureMode
{
    Basic = 0,
    Shift = 1,
    DJ = 2,
    RealDJ = 3
}

public enum ColorGeneratorType
{
    KMeans = 0,
    OctTree = 1,
    Auto = 2
}

public enum ThemeRequest
{
    Auto = 0,
    Light = 1,
    Dark = 2
}

public enum RollingCalculator
{
    ElasticEaseRollingCalculator = 0,
    SinRollingCalculator = 1,
    LyricifyRollingCalculator = 2,
    SyncRollingCalculator = 3,
    CircleEaseRollingCalculator = 4
}

public enum LyricScanStyle
{
    RectReveal = 0,
    TokenOpacity = 1
}
