using HyPlayer.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls;

public sealed partial class LyricShareDialog : ContentDialog
{
    public static readonly DependencyProperty LyricsProperty = DependencyProperty.Register(
        "Lyrics", typeof(List<SongLyric>), typeof(LyricShareDialog),
        new PropertyMetadata(default(List<SongLyric>)));

    public static readonly DependencyProperty ShareLyricItemProperty = DependencyProperty.Register(
        "ShareLyricItem", typeof(ObservableCollection<LyricShareItem>), typeof(LyricShareDialog),
        new PropertyMetadata(new ObservableCollection<LyricShareItem>()));

    public Dictionary<SongLyric, string> OutputLines = new();

    public LyricShareDialog()
    {
        InitializeComponent();
    }

    public List<SongLyric> Lyrics
    {
        get => (List<SongLyric>)GetValue(LyricsProperty);
        set
        {
            SetValue(LyricsProperty, value);
            LoadLyricsList();
        }
    }

    public ObservableCollection<LyricShareItem> ShareLyricItem
    {
        get => (ObservableCollection<LyricShareItem>)GetValue(ShareLyricItemProperty);
        set => SetValue(ShareLyricItemProperty, value);
    }

    private void LoadLyricsList()
    {
        ShareLyricItem.Clear();
        foreach (var songLyric in Lyrics)
        {
            ShareLyricItem.Add(new LyricShareItem
            {
                Type = LyricShareItemType.Original,
                Text = songLyric.LyricLine.CurrentLyric,
                Time = songLyric.LyricLine.StartTime,
                OriginalLyric = songLyric
            });
            if (songLyric.HaveTranslation)
                ShareLyricItem.Add(new LyricShareItem
                {
                    Type = LyricShareItemType.Translation,
                    Text = songLyric.Translation,
                    Time = songLyric.LyricLine.StartTime,
                    OriginalLyric = songLyric
                });
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        OutputLines.Clear();
        var output = LoadSelectedText();
        var dp = new DataPackage();
        dp.SetText(output);
        Clipboard.SetContent(dp);
        Common.AddToTeachingTipLists("成功复制");
    }

    private string LoadSelectedText()
    {
        var items = MainListView.SelectedItems.Cast<LyricShareItem>().ToList();
        foreach (var item in items)
        {
            if (!OutputLines.ContainsKey(item.OriginalLyric))
                OutputLines[item.OriginalLyric] =
                    TextBoxOutputFormat.Text
                        .Replace("{$NEWLINE}", "\r\n")
                        .Replace("{$TIME}", item.OriginalLyric.LyricLine.StartTime.ToString(@"mm\:ss\.ff"));
            OutputLines[item.OriginalLyric] = item.Type switch
            {
                LyricShareItemType.Original => OutputLines[item.OriginalLyric]
                    .Replace("{$ORIGINAL}", item.Text),
                LyricShareItemType.Translation => OutputLines[item.OriginalLyric]
                    .Replace("{$TRANSLATION}", item.Text),
                LyricShareItemType.Romaji => OutputLines[item.OriginalLyric]
                    .Replace("{$ROMAJI}", item.Text),
                _ => OutputLines[item.OriginalLyric]
            };
        }

        // 最后把没有碰撞上的匹配项给替换掉
        // 首先先复制一份出来
        var newOutputLines = OutputLines.ToDictionary(t => t.Key, t => t.Value);
        foreach (var outputLinesKey in OutputLines.Keys)
            newOutputLines[outputLinesKey] =
                OutputLines[outputLinesKey]
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
        for (var index = 0; index < ShareLyricItem.Count; index++)
        {
            var lyricShareItem = ShareLyricItem[index];
            if (lyricShareItem.Type != LyricShareItemType.Original) continue;
            if (lyricShareItem.OriginalLyric.HaveRomaji)
            {
                ShareLyricItem.Insert(index + 1, new LyricShareItem
                {
                    Type = LyricShareItemType.Romaji,
                    Text = lyricShareItem.OriginalLyric.Romaji ??
                           string.Empty,
                    Time = lyricShareItem.Time,
                    OriginalLyric = lyricShareItem.OriginalLyric
                });
                index++;
            }
        }
    }

    private void NoSelectEmpty(object sender, RoutedEventArgs e)
    {
        foreach (var item in MainListView.SelectedItems)
            if (item is LyricShareItem lyricShareItem && string.IsNullOrWhiteSpace(lyricShareItem.Text))
                MainListView.SelectedItems.Remove(item);
    }
}

public class LyricShareItem
{
    public SongLyric OriginalLyric;
    public string Text;
    public TimeSpan Time;
    public LyricShareItemType Type;
}

public enum LyricShareItemType
{
    Original,
    Translation,
    Romaji
}