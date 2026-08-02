using System.Collections.Generic;
using Windows.Storage;

namespace HyPlayer.Features.Account.Services;

public interface IAuthSessionStore
{
    IReadOnlyDictionary<string, string> Load();
    void Save(IReadOnlyDictionary<string, string> sessionValues);
}

public sealed class AuthSessionStore : IAuthSessionStore
{
    private const string ContainerName = "AuthSession";

    public IReadOnlyDictionary<string, string> Load()
    {
        if (!ApplicationData.Current.LocalSettings.Containers.TryGetValue(ContainerName, out var container))
            return new Dictionary<string, string>();

        var values = new Dictionary<string, string>();
        foreach (var item in container.Values)
        {
            if (item.Value is string value)
                values[item.Key] = value;
        }

        return values;
    }

    public void Save(IReadOnlyDictionary<string, string> sessionValues)
    {
        var container = ApplicationData.Current.LocalSettings.CreateContainer(
            ContainerName, ApplicationDataCreateDisposition.Always);
        container.Values.Clear();
        foreach (var item in sessionValues)
            container.Values[item.Key] = item.Value;
    }
}
