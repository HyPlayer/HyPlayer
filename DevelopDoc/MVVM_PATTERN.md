# MVVM Architecture Pattern in HyPlayer

## Overview

HyPlayer follows the Model-View-ViewModel (MVVM) architectural pattern to separate concerns and improve maintainability. This document describes the pattern and how to implement it for new pages.

## Architecture Components

### 1. Models (Classes/)
- Located in `HyPlayer/Classes/`
- Examples: `NCSong`, `NCPlayList`, `NCAlbum`, `NCArtist`
- Pure data classes representing entities
- Should not contain UI logic or business logic

### 2. ViewModels (ViewModels/)
- Located in `HyPlayer/ViewModels/`
- Examples: `HomeViewModel`, `AlbumViewModel`, `SongListDetailViewModel`
- Extend `ObservableRecipient` from CommunityToolkit.Mvvm
- Implement `IViewModel` interface
- Use `[ObservableProperty]` attribute for bindable properties
- Use `[RelayCommand]` attribute for commands
- Use `INeteaseProviderService` as a bridge to access NeteaseApi

### 3. Views (Pages/)
- Located in `HyPlayer/Pages/`
- XAML files define the UI
- Code-behind files extend `AppPageBase<TViewModel>`
- Use `x:Bind` for data binding (NOT `{Binding}`)

### 4. Services (Services/)
- Located in `HyPlayer/Services/`
- `NeteaseProviderService` implements `INeteaseProviderService`
- Acts as a bridge between ViewModels and NeteaseApi
- Abstracts API details from ViewModels

## Implementation Guide

### Step 1: Create a ViewModel

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.Contracts.Services;
using HyPlayer.Contracts.ViewModels;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.ViewModels
{
    public partial class MyPageViewModel : ObservableRecipient, IViewModel
    {
        private readonly INeteaseProviderService _neteaseProviderService;
        
        // Observable properties - use [ObservableProperty] attribute
        [ObservableProperty] 
        private string _title;
        
        [ObservableProperty] 
        private ObservableCollection<NCSong> _songs;
        
        [ObservableProperty] 
        private bool _isLoading;
        
        private CancellationTokenSource _cancellationTokenSource;

        public MyPageViewModel(INeteaseProviderService neteaseProviderService)
        {
            _neteaseProviderService = neteaseProviderService;
            _songs = new ObservableCollection<NCSong>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task InitializeAsync(string parameter)
        {
            IsLoading = true;
            try
            {
                // Use NeteaseProviderService as a bridge to NeteaseApi
                var data = await _neteaseProviderService.GetSomeDataAsync(
                    parameter, 
                    _cancellationTokenSource.Token
                );
                
                // Update properties
                Title = data.Title;
                Songs.Clear();
                foreach (var song in data.Songs)
                {
                    Songs.Add(song);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        // Commands - use [RelayCommand] attribute
        [RelayCommand]
        private void PlayAll()
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.AppendNcSongs(Songs.ToList());
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        public void Cleanup()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }
    }
}
```

### Step 2: Register ViewModel in Locator

Edit `Locator.cs`:

```csharp
_servicesCollection.AddTransient<MyPageViewModel>();
```

### Step 3: Create Page Base Class

Create `MyPageBase.cs`:

```csharp
using HyPlayer.ViewModels;

namespace HyPlayer.Pages
{
    public class MyPageBase : AppPageBase<MyPageViewModel>
    {
        public MyPageBase()
        {
        }
    }
}
```

### Step 4: Update Page Code-Behind

Update `MyPage.xaml.cs`:

```csharp
using Windows.UI.Xaml.Navigation;

namespace HyPlayer.Pages
{
    public sealed partial class MyPage : MyPageBase
    {
        public MyPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            var parameter = e.Parameter as string;
            await ViewModel.InitializeAsync(parameter);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.Cleanup();
        }
    }
}
```

### Step 5: Update XAML to Use x:Bind

Update `MyPage.xaml`:

```xml
<local:MyPageBase
    x:Class="HyPlayer.Pages.MyPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:HyPlayer.Pages"
    xmlns:classes="using:HyPlayer.Classes">
    
    <!-- Use x:Bind to bind to ViewModel properties -->
    <StackPanel>
        <TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}" />
        
        <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
        
        <ListView ItemsSource="{x:Bind ViewModel.Songs, Mode=OneWay}">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="classes:NCSong">
                    <TextBlock Text="{x:Bind songname}" />
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
        
        <!-- Bind commands -->
        <Button 
            Content="Play All" 
            Command="{x:Bind ViewModel.PlayAllCommand}" />
    </StackPanel>
</local:MyPageBase>
```

## Key Principles

### 1. Use x:Bind Instead of Binding

❌ Wrong:
```xml
<TextBlock Text="{Binding Title}" />
```

✅ Correct:
```xml
<TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}" />
```

### 2. Use NeteaseProviderService as Bridge

❌ Wrong (Direct API access in ViewModel):
```csharp
var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.AlbumInfoApi, ...);
```

✅ Correct (Use service):
```csharp
var (album, songs) = await _neteaseProviderService.GetAlbumDetailsAsync(albumId, token);
```

### 3. Observable Properties

❌ Wrong:
```csharp
private string _title;
public string Title 
{ 
    get => _title; 
    set 
    { 
        _title = value; 
        OnPropertyChanged(); 
    } 
}
```

✅ Correct:
```csharp
[ObservableProperty] 
private string _title;
```

### 4. Commands

❌ Wrong:
```csharp
public ICommand PlayAllCommand { get; }

public MyViewModel()
{
    PlayAllCommand = new RelayCommand(PlayAll);
}

private void PlayAll() { ... }
```

✅ Correct:
```csharp
[RelayCommand]
private void PlayAll() { ... }
```

## Benefits

1. **Separation of Concerns**: UI, business logic, and data access are separated
2. **Testability**: ViewModels can be unit tested without UI
3. **Maintainability**: Changes to one layer don't affect others
4. **Performance**: x:Bind is compiled and faster than Binding
5. **Type Safety**: x:Bind provides compile-time checking

## Examples

See the following files for complete examples:
- `HyPlayer/ViewModels/HomeViewModel.cs`
- `HyPlayer/Pages/HomePage.xaml`
- `HyPlayer/Pages/HomePage.xaml.cs`
- `HyPlayer/Services/NeteaseProviderService.cs`

## Extending NeteaseProviderService

When you need a new API operation, add it to `INeteaseProviderService` and implement it in `NeteaseProviderService`:

```csharp
// In INeteaseProviderService.cs
Task<SomeData> GetSomeDataAsync(string id, CancellationToken token = default);

// In NeteaseProviderService.cs
public async Task<SomeData> GetSomeDataAsync(string id, CancellationToken token)
{
    var result = await _apiHandler.RequestAsync(
        NeteaseApis.SomeApi,
        new SomeRequest() { Id = id },
        token);

    return result.Match(
        success => MapToModel(success),
        error => throw new Exception(error.Message)
    );
}
```

This keeps the API abstraction clean and testable.
