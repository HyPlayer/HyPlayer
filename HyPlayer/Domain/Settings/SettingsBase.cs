using Windows.Storage;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Base class for sub-settings groups providing shared storage and notification infrastructure.
/// </summary>
public abstract class SettingsBase
{
    public static T GetSettings<T>(string propertyName, T defaultValue)
    {
        try
        {
            var success = ApplicationData.Current.LocalSettings.Values.TryGetValue(propertyName, out var value);
            if (success) return (T)value;

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}