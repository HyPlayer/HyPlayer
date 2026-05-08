using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace HyPlayer.Classes.Settings
{
    /// <summary>
    /// Base class for sub-settings groups providing shared storage and notification infrastructure.
    /// </summary>
    public abstract class SettingsBase : INotifyPropertyChanged
    {
#nullable enable
        public event PropertyChangedEventHandler? PropertyChanged;
#nullable restore

        public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            catch
            {
                // ignore
            }
        }

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
