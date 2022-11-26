using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SongListItem : UserControl,INotifyPropertyChanged
    {
        //TODO 在选中item时将列表中的其他项缩回
        public SongListItem()
        {
            this.InitializeComponent();
            this.DataContextChanged += (s, e) => Bindings.Update();
        }
        public event PropertyChangedEventHandler PropertyChanged;

        public static readonly DependencyProperty SongProperty =
            DependencyProperty.Register(nameof(Song), typeof(NCSong), typeof(SongListItem), new PropertyMetadata(null));
        public NCSong Song
        {
            get => (NCSong)GetValue(SongProperty);
            set
            {
                SetValue(SongProperty, value);
                OnItemChanged();
            }
        }
        public string ConvertTranslate(string source)
        {
            return string.IsNullOrEmpty(source) ? "" : "(" + source + ")";
        }

        private void OnItemChanged()
        {
            //当内容改变时缩回原本的模式
            DefaultGrid.Visibility = Visibility.Visible;
            ExpandedGrid.Visibility = Visibility.Collapsed;
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            //TODO 添加更多选项
        }

        private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            //TODO 添加右击事件
        }

        private void SongGrid_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            DefaultGrid.Visibility = Visibility.Collapsed;
            ExpandedGrid.Visibility = Visibility.Visible;
        }
    }
}
