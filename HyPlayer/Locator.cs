using HyPlayer.Contracts.Services;
using HyPlayer.NeteaseApi;
using HyPlayer.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;


namespace HyPlayer;

public class Locator
{
    public static Locator Instance => _Instance ??= new Locator();
    private static Locator _Instance;

    private IServiceProvider _services;

    public T GetService<T>()
        where T : class
    {
        if (_services.GetService(typeof(T)) is not T service)
        {
            throw new Exception($"{typeof(T)} needs to be regiestered in ConfigureServices.");
        }

        return service;
    }

    public Locator()
    {
        var _servicesCollection = new ServiceCollection();

        _servicesCollection.AddSingleton<NeteaseCloudMusicApiHandler>();
        _servicesCollection.AddSingleton<AudioGraphPlayer>();
        _servicesCollection.AddSingleton<INeteaseProviderService, NeteaseProviderService>();
        _servicesCollection.AddSingleton<IDownloadManagementService, DownloadManagementService>();

        _servicesCollection.AddTransient<HomeViewModel>();

        _services = _servicesCollection.BuildServiceProvider();

    }

}


