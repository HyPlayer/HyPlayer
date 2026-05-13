using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace HyPlayer.Classes.Settings
{
    /// <summary>
    /// Base class for sub-settings groups providing shared storage and notification infrastructure.
    /// </summary>
    public abstract class SettingsBase
    {
        public static T GetSettings<T>(string propertyName, T defaultValue)
        {
            try
            {
                var success = ApplicationData.Current.LocalSettings.Values.TryGetValue(propertyName, out object value);
                if (success)
                {
                    return (T)value;
                }

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
