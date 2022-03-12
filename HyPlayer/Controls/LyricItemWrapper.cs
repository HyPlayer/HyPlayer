using HyPlayer.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Controls
{
    public class LyricItemWrapper : ContentPresenter
    {
        public LyricItemWrapper()
        {
            this.SizeChanged += LyricItemWrapper_SizeChanged;
        }

        private void LyricItemWrapper_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Content is LyricItem item)
            {
                item.RefreshFontSize();
            }
        }

        public SongLyric SongLyric
        {
            get { return (SongLyric)GetValue(SongLyricProperty); }
            set { SetValue(SongLyricProperty, value); }
        }

        public static readonly DependencyProperty SongLyricProperty =
            DependencyProperty.Register("SongLyric", typeof(SongLyric), typeof(LyricItemWrapper), new PropertyMetadata(null, (s, a) =>
            {
                if (!object.Equals(a.NewValue, a.OldValue) && s is LyricItemWrapper sender)
                {
                    if (a.NewValue != null)
                    {
                        var item = new LyricItem((SongLyric)a.NewValue)
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        sender.Content = item;

                        if (sender.IsShow)
                        {
                            item.OnShow();
                        }
                        else
                        {
                            item.OnHind();
                        }
                    }
                    else
                    {
                        sender.Content = null;
                    }
                }
            }));


        public bool IsShow
        {
            get { return (bool)GetValue(IsShowProperty); }
            set { SetValue(IsShowProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsShow.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsShowProperty =
            DependencyProperty.Register("IsShow", typeof(bool), typeof(LyricItemWrapper), new PropertyMetadata(false, (s, a) =>
            {
                if (!object.Equals(a.NewValue, a.OldValue) && s is LyricItemWrapper sender)
                {
                    if (sender.Content is LyricItem item)
                    {
                        if ((bool)a.NewValue)
                        {
                            item.OnShow();
                        }
                        else
                        {
                            item.OnHind();
                        }
                    }
                }
            }));


    }

    public class LyricItemModel : INotifyPropertyChanged
    {
        private bool isShow;

        public event PropertyChangedEventHandler PropertyChanged;

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
    }
}
