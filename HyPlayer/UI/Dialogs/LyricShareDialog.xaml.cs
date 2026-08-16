using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Lyrics;
using ObservableCollections;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.UI.Dialogs;

public sealed partial class LyricShareDialog : ContentDialog
{
    private readonly Dictionary<SongLyric, string> _outputLines = new();
    private List<SongLyric> _lyrics = [];

    public LyricShareDialog()
    {
        ShareLyricItemView = ShareLyricItem.ToNotifyCollectionChanged();
        InitializeComponent();
        Closed += LyricShareDialog_Closed;
    }

    public List<SongLyric> Lyrics
    {
        get => _lyrics;
        set
        {
            _lyrics = value;
            LoadLyricsList();
        }
    }

    public ObservableList<LyricShareItem> ShareLyricItem { get; } = [];

    public NotifyCollectionChangedSynchronizedViewList<LyricShareItem> ShareLyricItemView { get; }

    private void LoadLyricsList()
    {
        ShareLyricItem.Clear();
        var items = new List<LyricShareItem>(Lyrics.Count * 2);
        foreach (var songLyric in Lyrics)
        {
            items.Add(new LyricShareItem
            {
                Type = LyricShareItemType.Original,
                Text = songLyric.LyricLine.CurrentLyric,
                Time = songLyric.LyricLine.StartTime,
                OriginalLyric = songLyric
            });
            if (songLyric.HaveTranslation)
                items.Add(new LyricShareItem
                {
                    Type = LyricShareItemType.Translation,
                    Text = songLyric.Translation,
                    Time = songLyric.LyricLine.StartTime,
                    OriginalLyric = songLyric
                });
        }

        ShareLyricItem.AddRange(items);
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _outputLines.Clear();
        var output = LoadSelectedText();
        var dp = new DataPackage();
        dp.SetText(output);
        Clipboard.SetContent(dp);
        Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("成功复制");
    }

    private string LoadSelectedText()
    {
        var items = MainListView.SelectedItems.Cast<LyricShareItem>().ToList();
        foreach (var item in items)
        {
            if (!_outputLines.ContainsKey(item.OriginalLyric))
                _outputLines[item.OriginalLyric] =
                    TextBoxOutputFormat.Text
                        .Replace("{$NEWLINE}", "\r\n")
                        .Replace("{$TIME}", item.OriginalLyric.LyricLine.StartTime.ToString(@"mm\:ss\.ff"));
            _outputLines[item.OriginalLyric] = item.Type switch
            {
                LyricShareItemType.Original => _outputLines[item.OriginalLyric]
                    .Replace("{$ORIGINAL}", item.Text),
                LyricShareItemType.Translation => _outputLines[item.OriginalLyric]
                    .Replace("{$TRANSLATION}", item.Text),
                LyricShareItemType.Romaji => _outputLines[item.OriginalLyric]
                    .Replace("{$ROMAJI}", item.Text),
                _ => _outputLines[item.OriginalLyric]
            };
        }

        // 最后把没有碰撞上的匹配项给替换掉
        // 首先先复制一份出来
        var newOutputLines = _outputLines.ToDictionary(t => t.Key, t => t.Value);
        foreach (var outputLinesKey in _outputLines.Keys)
            newOutputLines[outputLinesKey] =
                _outputLines[outputLinesKey]
                    .Replace("{$ORIGINAL}", string.Empty)
                    .Replace("{$TRANSLATION}", string.Empty)
                    .Replace("{$ROMAJI}", string.Empty);

        // 再根据歌词拍下序
        newOutputLines = newOutputLines.OrderBy(t => t.Key.LyricLine.StartTime).ToDictionary(t => t.Key, t => t.Value);
        return string.Join(string.Empty, newOutputLines.Values);
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }

    private void LyricShareDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Closed -= LyricShareDialog_Closed;
        ShareLyricItem.Clear();
        _outputLines.Clear();
    }

    private void SelectOriginal(object sender, RoutedEventArgs e)
    {
        foreach (var lyricShareItem in ShareLyricItem.Where(t => t.Type == LyricShareItemType.Original))
            if (!MainListView.SelectedItems.Contains(lyricShareItem))
                MainListView.SelectedItems.Add(lyricShareItem);
    }

    private void SelectTranslation(object sender, RoutedEventArgs e)
    {
        foreach (var lyricShareItem in ShareLyricItem.Where(t => t.Type == LyricShareItemType.Translation))
            if (!MainListView.SelectedItems.Contains(lyricShareItem))
                MainListView.SelectedItems.Add(lyricShareItem);
    }

    private void CleanSelection(object sender, RoutedEventArgs e)
    {
        MainListView.SelectedItems.Clear();
    }

    private void ReverseSelection(object sender, RoutedEventArgs e)
    {
        foreach (var lyricShareItem in ShareLyricItem)
            if (!MainListView.SelectedItems.Contains(lyricShareItem))
                MainListView.SelectedItems.Add(lyricShareItem);
            else
                MainListView.SelectedItems.Remove(lyricShareItem);
    }

    private void SelectRomaji(object sender, RoutedEventArgs e)
    {
        foreach (var lyricShareItem in ShareLyricItem.Where(t => t.Type == LyricShareItemType.Romaji))
            if (!MainListView.SelectedItems.Contains(lyricShareItem))
                MainListView.SelectedItems.Add(lyricShareItem);
    }

    private void LoadRomaji(object sender, RoutedEventArgs e)
    {
        CheckBoxLoadRomaji.IsEnabled = false;
        var items = new List<LyricShareItem>(ShareLyricItem.Count * 2);
        foreach (var lyricShareItem in ShareLyricItem)
        {
            items.Add(lyricShareItem);
            if (lyricShareItem.Type == LyricShareItemType.Original
                && lyricShareItem.OriginalLyric.HaveRomaji)
                items.Add(new LyricShareItem
                {
                    Type = LyricShareItemType.Romaji,
                    Text = lyricShareItem.OriginalLyric.Romaji ??
                           string.Empty,
                    Time = lyricShareItem.Time,
                    OriginalLyric = lyricShareItem.OriginalLyric
                });
        }

        ShareLyricItem.Clear();
        ShareLyricItem.AddRange(items);
    }

    private void NoSelectEmpty(object sender, RoutedEventArgs e)
    {
        foreach (var item in MainListView.SelectedItems)
            if (item is LyricShareItem lyricShareItem && string.IsNullOrWhiteSpace(lyricShareItem.Text))
                MainListView.SelectedItems.Remove(item);
    }
}
