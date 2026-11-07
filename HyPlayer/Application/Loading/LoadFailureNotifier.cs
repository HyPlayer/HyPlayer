using System;
using System.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;

namespace HyPlayer.Application.Loading;

public static class LoadFailureNotifier
{
    public static void ShowFailure(
        this INotificationService notification,
        string title,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        var environment = Ioc.Default.GetService<ILoadEnvironment>();
        var failure = LoadFailureClassifier.Classify(exception, cancellationToken,
            environment?.IsNetworkAvailable ?? true);
        if (failure.IsSilent)
            return;

        environment?.Record(title, failure);
        notification.ShowMessage(title, failure.Reason);
    }

    public static void Record(string subject, Exception exception, CancellationToken cancellationToken = default)
    {
        var environment = Ioc.Default.GetService<ILoadEnvironment>();
        var failure = LoadFailureClassifier.Classify(exception, cancellationToken,
            environment?.IsNetworkAvailable ?? true);
        if (!failure.IsSilent)
            environment?.Record(subject, failure);
    }
}
