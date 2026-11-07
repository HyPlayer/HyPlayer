namespace HyPlayer.Application.Loading;

public interface ILoadEnvironment
{
    bool IsLoginPending { get; }
    bool IsLoggedIn { get; }
    bool IsNetworkAvailable { get; }
    void Record(string subject, LoadFailure failure);
}
