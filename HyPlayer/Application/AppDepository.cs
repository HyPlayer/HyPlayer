using Depository.Abstraction.Models.Options;
using Depository.Core;
using Depository.Extensions;
using System;
using System.Collections.Generic;

namespace HyPlayer.Application;

/// <summary>
/// Provides the application-owned native Depository root used by XAML code-behind.
/// </summary>
public static class AppDepository
{
    /// <summary>
    /// Gets the root Depository container for the current app instance.
    /// </summary>
    public static Depository.Core.Depository Root { get; private set; } = null!;

    /// <summary>
    /// Creates the root Depository container.
    /// </summary>
    /// <param name="options">Optional Depository configuration.</param>
    public static void Initialize(Action<DepositoryOption>? options = null)
    {
        Root?.Dispose();
        Root = DepositoryFactory.CreateNew(options);
    }

    /// <summary>
    /// Resolves a dependency from the root Depository container.
    /// </summary>
    /// <typeparam name="T">The dependency type to resolve.</typeparam>
    /// <returns>The resolved dependency instance.</returns>
    public static T Resolve<T>() => Root.Resolve<T>();

    /// <summary>
    /// Resolves all registered implementations for a dependency from the root Depository container.
    /// </summary>
    /// <typeparam name="T">The dependency type to resolve.</typeparam>
    /// <returns>The resolved dependency instances.</returns>
    public static List<T> ResolveMultiple<T>() => Root.ResolveMultiple<T>();
}
