using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace HyPlayer.Domain.Settings;

/// <summary>
/// Base class for one independently persisted and observable settings section.
/// </summary>
public abstract partial class SettingsBase : ObservableObject
{
    private static readonly object MissingValue = new();

    private static readonly ConcurrentDictionary<string, object> Cache = new();

    protected abstract string SectionName { get; }

    protected string GetStorageKey(string propertyName) => $"{SectionName}.{propertyName}";

    private static object ReadStored(string storageKey) =>
        Cache.GetOrAdd(storageKey, static key =>
            ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var stored) && stored is not null
                ? stored
                : MissingValue);

    protected T GetSettings<T>(string propertyName, T defaultValue)
    {
        try
        {
            var value = ReadStored(GetStorageKey(propertyName));
            if (ReferenceEquals(value, MissingValue))
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
            var stored = ReadStored(GetStorageKey(propertyName));
            if (!ReferenceEquals(stored, MissingValue))
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

        var currentValue = ReadStored(storageKey);
        if (!ReferenceEquals(currentValue, MissingValue)
            && EqualityComparer<object?>.Default.Equals(currentValue, storedValue))
            return false;

        if (storedValue is null)
            values.Remove(storageKey);
        else
            values[storageKey] = storedValue;

        Cache[storageKey] = storedValue ?? MissingValue;

        OnPropertyChanged(notifyingPropertyName);
        return true;
    }
}
