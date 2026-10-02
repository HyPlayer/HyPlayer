using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace HyPlayer.Domain.Settings;

/// <summary>
/// Base class for one independently persisted and observable settings section.
/// </summary>
public abstract partial class SettingsBase : ObservableObject
{
    protected abstract string SectionName { get; }

    protected string GetStorageKey(string propertyName) => $"{SectionName}.{propertyName}";

    protected T GetSettings<T>(string propertyName, T defaultValue)
    {
        try
        {
            if (!ApplicationData.Current.LocalSettings.Values.TryGetValue(GetStorageKey(propertyName),
                    out var value) || value is null)
                return defaultValue;

            if (value is T typedValue)
                return typedValue;

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (targetType.IsEnum)
                return (T)Enum.ToObject(targetType, value);

            if (targetType == typeof(bool) && value is string text && bool.TryParse(text, out var boolean))
                return (T)(object)boolean;

            return (T)Convert.ChangeType(value, targetType);
        }
        catch
        {
            return defaultValue;
        }
    }

    protected bool TryGetSettings<T>(string propertyName, out T? value)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(GetStorageKey(propertyName), out var stored)
                && stored is not null)
            {
                if (stored is T typed)
                {
                    value = typed;
                    return true;
                }

                var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                value = targetType.IsEnum
                    ? (T)Enum.ToObject(targetType, stored)
                    : (T)Convert.ChangeType(stored, targetType);
                return true;
            }
        }
        catch
        {
            // Treat malformed settings as absent.
        }

        value = default;
        return false;
    }

    protected bool SetSettings<T>(string propertyName, T value,
        [CallerMemberName] string? notifyingPropertyName = null)
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        var storageKey = GetStorageKey(propertyName);
        object? storedValue = value is Enum ? Convert.ToInt32(value) : value;

        if (values.TryGetValue(storageKey, out var currentValue)
            && EqualityComparer<object?>.Default.Equals(currentValue, storedValue))
            return false;

        if (storedValue is null)
            values.Remove(storageKey);
        else
            values[storageKey] = storedValue;

        OnPropertyChanged(notifyingPropertyName);
        return true;
    }
}
