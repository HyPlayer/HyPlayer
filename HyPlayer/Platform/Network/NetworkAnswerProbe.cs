using System.Threading;

namespace HyPlayer.Platform.Network;

internal sealed class NetworkAnswerProbe
{
    private static readonly AsyncLocal<NetworkAnswerProbe?> Current = new();

    private volatile bool _answered;

    public bool Answered => _answered;

    public static NetworkAnswerProbe Begin()
    {
        var probe = new NetworkAnswerProbe();
        Current.Value = probe;
        return probe;
    }

    public static void Report(bool answered)
    {
        if (answered && Current.Value is { } probe)
            probe._answered = true;
    }
}
