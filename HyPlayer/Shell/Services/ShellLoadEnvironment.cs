using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Loading;
using HyPlayer.Features.Account.Services;
using HyPlayer.Shell.Navigation;

namespace HyPlayer.Shell.Services;

public sealed class ShellLoadEnvironment : ILoadEnvironment
{
    private readonly IAuthService _auth;
    private readonly IDiagnosticsStateService _diagnostics;
    private NavigationShellViewModel? _navigationShell;

    public ShellLoadEnvironment(IAuthService auth, IDiagnosticsStateService diagnostics)
    {
        _auth = auth;
        _diagnostics = diagnostics;
    }

    public bool IsLoginPending =>
        !_auth.IsLoggedIn &&
        (_navigationShell ??= Ioc.Default.GetRequiredService<NavigationShellViewModel>()).IsAutoLoginPending;

    public bool IsLoggedIn => _auth.IsLoggedIn;

    public bool IsNetworkAvailable
    {
        get
        {
            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return true;
            }
        }
    }

    public void Record(string subject, LoadFailure failure)
    {
        if (string.IsNullOrEmpty(failure.Detail))
            return;

        lock (_diagnostics.ErrorMessages)
            _diagnostics.AddError($"{subject}: {failure.Reason} [{failure.Detail}]");
    }
}
