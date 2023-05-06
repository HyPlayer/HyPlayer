using HyPlayer.Classes;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Controls;

public class LyricItemWrapper : ContentPresenter
{
    public static readonly DependencyProperty SongLyricProperty =
        DependencyProperty.Register("SongLyric", typeof(SongLyric), typeof(LyricItemWrapper), new PropertyMetadata(null,
            (s, a) =>
            {
                if (!Equals(a.NewValue, a.OldValue) && s is LyricItemWrapper sender)
                {
                    if (a.NewValue != null)
                    {
                        var item = new LyricItem((SongLyric)a.NewValue)
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        sender.Content = item;

                        if (sender.IsShow)
                            item.OnShow();
                        else
                            item.OnHind(                            item.GetTextBoxTranslation());
                    }
                    else
                    {
                        sender.Content = null;
                    }
                }
            }));

    // Using a DependencyProperty as the backing store for IsShow.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsShowProperty =
        DependencyProperty.Register("IsShow", typeof(bool), typeof(LyricItemWrapper), new PropertyMetadata(false,
            (s, a) =>
            {
                if (!Equals(a.NewValue, a.OldValue) && s is LyricItemWrapper sender)
                    if (sender.Content is LyricItem item)
                    {
                        if ((bool)a.NewValue)
                            item.OnShow();
                        else
                            item.OnHind(                            item.GetTextBoxTranslation());
                    }
            }));

    public LyricItemWrapper()
    {
        SizeChanged += LyricItemWrapper_SizeChanged;
    }

    public SongLyric SongLyric
    {
        get => (SongLyric)GetValue(SongLyricProperty);
        set => SetValue(SongLyricProperty, value);
    }


    public bool IsShow
    {
        get => (bool)GetValue(IsShowProperty);
        set => SetValue(IsShowProperty, value);
    }

    private void LyricItemWrapper_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Content is LyricItem item) item.RefreshFontSize();
    }
}

public class LyricItemModel : INotifyPropertyChanged
{
    private bool isShow;

    public LyricItemModel(SongLyric songLyric)
    {
        SongLyric = songLyric;
    }

    public SongLyric SongLyric { get; }

    public bool IsShow
    {
        get => isShow;
        set
        {
            if (isShow != value)
            {
                isShow = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShow)));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}