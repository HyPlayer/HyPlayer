#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Comments;
using HyPlayer.Features.User;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.UI.Controls;

public sealed partial class SingleComment : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty AvatarSourceProperty =
        DependencyProperty.Register("AvatarSource", typeof(BitmapImage), typeof(SingleComment),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MainCommentProperty =
        DependencyProperty.Register("MainComment", typeof(Comment), typeof(SingleComment),
            new PropertyMetadata(null)); //主评论

    public event PropertyChangedEventHandler PropertyChanged;

    public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
    }


    private ObservableCollection<Comment> floorComments = new ObservableCollection<Comment>();
    public UserDisplay CommentUserDisplay;
    private string time = "0";

    public SingleComment()
    {
        InitializeComponent();
        floorComments.CollectionChanged += FloorComments_CollectionChanged;
    }

    private void FloorComments_CollectionChanged(object sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(floorComments));
    }

    public BitmapImage AvatarSource
    {
        get => (BitmapImage)GetValue(AvatarSourceProperty);
        set => SetValue(AvatarSourceProperty, value);
    }

    public Comment MainComment
    {
        get => (Comment)GetValue(MainCommentProperty);
        set
        {
            SetValue(MainCommentProperty, value);
            ReplyCountIndicator.Text = value.ReplyCount.ToString();
            LikeCountTB.Text = value.LikedCount.ToString();
        }
    }

    private async Task LoadFloorComments(bool IsLoadMoreComments)
    {
        if (!IsLoadMoreComments) floorComments.Clear();
        var result = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Comments, $"{MainComment.ResourceType}_{MainComment.ResourceId}_{MainComment.CommentId}", async () =>
        {
            return await Ioc.Default.GetRequiredService<ICommentProvidable>()
                .GetThreadedCommentsAsync(
                    MainComment.ResourceId,
                    MapCommentTypeId(MainComment.ResourceType),
                    MainComment.CommentId,
                    !IsLoadMoreComments ? 0 : int.Parse(time ?? "0"),
                    20);
        }, TimeSpan.FromMinutes(5));
        if (result == null)
        {
            return;
        }
        foreach (var floorcomment in result.Items)
        {
            var floorComment = MapProviderComment(floorcomment);
            floorComment.ResourceId = MainComment.ResourceId;
            floorComment.ResourceType = MainComment.ResourceType;
            floorComment.IsMainComment = false;
            floorComments.Add(floorComment);
        }

        time = (result?.NextOffset ?? 0).ToString();
        LoadMore.Visibility = result?.HasMore is true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Ioc.Default.GetRequiredService<ICommentProvidable>().SetCommentLikeStateAsync(
                MainComment.ResourceId,
                MapCommentTypeId(MainComment.ResourceType),
                MainComment.CommentId,
                !MainComment.HasLiked);
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("点赞失败", ex.Message);
            return;
        }

        MainComment.LikedCount += MainComment.HasLiked ? -1 : 1;
        MainComment.HasLiked = !MainComment.HasLiked;
        LikeCountTB.Text = MainComment.LikedCount.ToString();
    }

    private static string MapCommentTypeId(NeteaseResourceType resourceType)
    {
        return resourceType switch
        {
            NeteaseResourceType.Song => "sg",
            NeteaseResourceType.Album => "al",
            NeteaseResourceType.Playlist => "pl",
            NeteaseResourceType.MV => "mv",
            NeteaseResourceType.Video => "vd",
            NeteaseResourceType.RadioChannel => "dj",
            _ => "sg"
        };
    }

    private static Comment MapProviderComment(CommentBase comment)
    {
        return new Comment
        {
            CommentId = comment.ActualId ?? string.Empty,
            Content = comment.Content ?? comment.Name,
            LikedCount = comment.LikedCount,
            SendTime = comment.SendDate > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(comment.SendDate).LocalDateTime : DateTime.MinValue,
            CommentUser = new Domain.Music.NCUser
            {
                Id = comment.Sender?.ActualId ?? string.Empty,
                Name = comment.Sender?.Name ?? string.Empty,
                Avatar = string.Empty,
            }
        };
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
        // NOTE: Comment delete functionality not yet implemented
    }

    private void NavToUser_Click(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Me), MainComment.CommentUser.Id);
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ReplyText.Text) && Ioc.Default.GetRequiredService<IAuthService>().IsLoggedIn)
        {
            try
            {
                // NOTE: Comment send functionality not yet implemented
                ReplyText.Text = string.Empty;
                await Task.Delay(1000);
                _ = LoadFloorComments(false);
            }
            catch (Exception ex)
            {
                var dlg = new MessageDialog(ex.Message, "出现问题，评论失败");
                await dlg.ShowAsync();
            }
        }
        else if (string.IsNullOrWhiteSpace(ReplyText.Text))
        {
            var dlg = new MessageDialog("评论不能为空");
            await dlg.ShowAsync();
        }
        else
        {
            var dlg = new MessageDialog("请先登录");
            await dlg.ShowAsync();
        }
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadFloorComments(true);
    }


    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        CommentUserDisplay = new(MainComment.CommentUser);
        ReplyBtn.Visibility = Visibility.Visible;
        FloorCommentsExpander.Visibility = MainComment.IsMainComment ? Visibility.Visible : Visibility.Collapsed;
        Bindings.Update();
    }

    private void FloorCommentsExpander_Expanding(Microsoft.UI.Xaml.Controls.Expander sender,
        Microsoft.UI.Xaml.Controls.ExpanderExpandingEventArgs args)
    {
        _ = LoadFloorComments(false);
    }

    private void FloorCommentsExpander_Collapsed(Microsoft.UI.Xaml.Controls.Expander sender,
        Microsoft.UI.Xaml.Controls.ExpanderCollapsedEventArgs args)
    {
        floorComments.Clear();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        floorComments.CollectionChanged -= FloorComments_CollectionChanged;
    }
}
