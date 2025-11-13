# Migrating Existing Pages to MVVM

This guide shows how to migrate existing pages to use the new MVVM ViewModels.

## Overview

The following ViewModels have been created and are ready for integration:
- `AlbumViewModel` - For album detail pages
- `SongListDetailViewModel` - For playlist detail pages
- `ArtistViewModel` - For artist detail pages

## Quick Migration Guide

### For AlbumPage

The `AlbumViewModel` is already created and registered. To integrate it:

#### Step 1: Update the XAML

Change the root element from `Page` to `local:AlbumPageBase`:

```xml
<!-- Before -->
<Page
    x:Class="HyPlayer.Pages.AlbumPage"
    ...>

<!-- After -->
<local:AlbumPageBase
    x:Class="HyPlayer.Pages.AlbumPage"
    ...>
```

And close it properly:

```xml
<!-- Before -->
</Page>

<!-- After -->
</local:AlbumPageBase>
```

#### Step 2: Update the Code-Behind

Change the class to extend `AlbumPageBase`:

```csharp
// Before
public sealed partial class AlbumPage : Page, IDisposable

// After
public sealed partial class AlbumPage : AlbumPageBase
```

#### Step 3: Use ViewModel in OnNavigatedTo

```csharp
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    base.OnNavigatedTo(e);
    
    string albumId = null;
    NCAlbum album = null;
    
    switch (e.Parameter)
    {
        case NCAlbum a:
            album = a;
            albumId = a.id;
            break;
        case string id:
            albumId = id;
            break;
    }
    
    if (albumId != null)
    {
        await ViewModel.InitializeAsync(albumId, album);
    }
}

protected override void OnNavigatedFrom(NavigationEventArgs e)
{
    base.OnNavigatedFrom(e);
    ViewModel.Cleanup();
}
```

#### Step 4: Update XAML Bindings

Replace direct property access with ViewModel bindings:

```xml
<!-- Before (direct property access in code-behind) -->
<TextBlock x:Name="TextBoxAlbumName" />

<!-- After (binding to ViewModel) -->
<TextBlock Text="{x:Bind ViewModel.AlbumName, Mode=OneWay}" />
```

```xml
<!-- Before -->
<Image x:Name="ImageRect" />

<!-- After -->
<Image>
    <Image.Source>
        <BitmapImage UriSource="{x:Bind ViewModel.AlbumCoverUrl, Mode=OneWay}" />
    </Image.Source>
</Image>
```

```xml
<!-- Before -->
<ItemsControl x:Name="SongsList" />

<!-- After -->
<ItemsControl ItemsSource="{x:Bind ViewModel.Songs, Mode=OneWay}">
    <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="classes:NCSong">
            <!-- Song item template -->
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

#### Step 5: Use Commands for Actions

Replace event handlers with command bindings:

```xml
<!-- Before -->
<Button Click="ButtonPlayAll_OnClick" />

<!-- After -->
<Button Command="{x:Bind ViewModel.PlayAllCommand}" />
```

### For SongListDetail Page

Follow similar steps as AlbumPage:
1. Change root to `<local:SongListDetailPageBase>`
2. Extend `SongListDetailPageBase` in code-behind
3. Use `ViewModel.InitializeAsync(playlistId, playlist)` in OnNavigatedTo
4. Bind to `ViewModel.PlaylistName`, `ViewModel.Songs`, etc.
5. Use `ViewModel.PlayAllCommand`, `ViewModel.SubscribeCommand`, etc.

### For ArtistPage

Follow similar steps:
1. Change root to `<local:ArtistPageBase>`
2. Extend `ArtistPageBase` in code-behind
3. Use `ViewModel.InitializeAsync(artistId)` in OnNavigatedTo
4. Bind to `ViewModel.ArtistName`, `ViewModel.HotSongs`, `ViewModel.Albums`, etc.
5. Use `ViewModel.PlayHotSongsCommand`, `ViewModel.PlayAllSongsCommand`, etc.

## Benefits of Migration

1. **Cleaner Code**: Separation of concerns between UI and business logic
2. **Type Safety**: x:Bind provides compile-time checking
3. **Performance**: x:Bind is faster than traditional Binding
4. **Testability**: ViewModels can be unit tested without UI
5. **Maintainability**: Changes to business logic don't affect UI structure

## Common Patterns

### Loading States

```xml
<ProgressRing 
    IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}"
    Visibility="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
```

### Collections

```xml
<ListView ItemsSource="{x:Bind ViewModel.Items, Mode=OneWay}">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="classes:ItemType">
            <TextBlock Text="{x:Bind PropertyName}" />
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

### Commands

```xml
<Button 
    Content="Action" 
    Command="{x:Bind ViewModel.ActionCommand}"
    CommandParameter="{x:Bind SomeParameter}" />
```

## Gradual Migration

You don't have to migrate everything at once:
1. Start by using the ViewModel alongside existing code
2. Gradually move logic from code-behind to ViewModel
3. Update bindings from named controls to ViewModel properties
4. Remove old code-behind logic once ViewModel handles it

## Example: HomePage

See `HomePage.xaml` and `HomePage.xaml.cs` for a complete working example of the MVVM pattern in action.

Key points:
- Extends `HomePageBase : AppPageBase<HomeViewModel>`
- Uses `x:Bind ViewModel.PropertyName, Mode=OneWay` throughout
- Commands bound with `Command="{x:Bind ViewModel.CommandName}"`
- Data templates use `x:DataType` for compile-time checking

## Troubleshooting

### "ViewModel is null"
- Ensure page extends the correct PageBase class
- Ensure ViewModel is registered in `Locator.cs`

### "Property not found on ViewModel"
- Check property name matches exactly (case-sensitive)
- Ensure property has `[ObservableProperty]` attribute
- Rebuild project to regenerate source generators

### "Command not found"
- Ensure method has `[RelayCommand]` attribute
- Command name is MethodName + "Command" (e.g., PlayAll → PlayAllCommand)
- Rebuild project to regenerate source generators

### "x:Bind compilation errors"
- Ensure `Mode=OneWay` is specified for most bindings
- Check `x:DataType` matches the actual type in DataTemplate
- Verify property names and paths are correct

## Next Steps

1. Choose a page to migrate
2. Follow the steps above
3. Test thoroughly
4. Commit changes
5. Repeat for other pages

For more details on the MVVM pattern, see [MVVM_PATTERN.md](./MVVM_PATTERN.md).
