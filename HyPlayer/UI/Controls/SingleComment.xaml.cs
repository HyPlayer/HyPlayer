#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.User;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Platform.Storage.Cache;
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

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了"用户控件"项模板

namespace HyPlayer.UI.Controls;

public sealed partial class SingleComment : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty AvatarSourceProperty =
        DependencyProperty.Register("AvatarSource", typeof(BitmapImage), typeof(SingleComment),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MainCommentProperty =
        DependencyProperty.Register("MainComment", typeof(CommentBase), typeof(SingleComment),
            new PropertyMetadata(null)); //主评论

    public event PropertyChangedEventHandler PropertyChanged;

    public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
    }


    private ObservableCollection<CommentBase> floorComments = new ObservableCollection<CommentBase>();
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
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

    public CommentBase MainComment
    {
        get => (CommentBase)GetValue(MainCommentProperty);
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
        var offset = !IsLoadMoreComments ? 0 : int.Parse(time ?? "0");
        const int count = 20;
        if (!TryResolveCommentTarget(MainComment, out var itemId, out var typeId))
            return;

        var result = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Comments, $"{MainComment.ProvidableItemId}_{MainComment.ActualId}_{offset}_{count}", async () =>
        {
            return await Ioc.Default.GetRequiredService<ICommentProvidable>()
                .GetThreadedCommentsAsync(
                    itemId,
                    typeId,
                    MainComment.ActualId,
                    offset,
                    count);
        }, TimeSpan.FromMinutes(5));
        if (result == null)
        {
            return;
        }
        foreach (var floorcomment in result.Items)
        {
            var floorComment = floorcomment;
            floorComment.ProvidableItemId = MainComment.ProvidableItemId;
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
            if (!TryResolveCommentTarget(MainComment, out var itemId, out var typeId))
                return;

            await Ioc.Default.GetRequiredService<ICommentProvidable>().SetCommentLikeStateAsync(
                itemId,
                typeId,
                MainComment.ActualId,
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

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
        // NOTE: Comment delete functionality not yet implemented
    }

    private void NavToUser_Click(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Me), MainComment.Sender?.ActualId);
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
        CommentUserDisplay = new UserDisplay(
            new CommentUserInfo
            {
                ActualId = MainComment.Sender?.ActualId ?? string.Empty,
                Name = MainComment.Sender?.Name ?? string.Empty,
                AvatarUrl = string.Empty,
            },
            _setting.noImage);
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

    private static bool TryResolveCommentTarget(CommentBase comment, out string itemId, out string typeId)
    {
        itemId = string.Empty;
        typeId = string.Empty;
        if (string.IsNullOrWhiteSpace(comment.ProvidableItemId))
            return false;

        var knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
        var fullId = comment.ProvidableItemId;
        var providerScopedId = fullId.StartsWith(knownTypeIds.Id, StringComparison.Ordinal)
            ? fullId[knownTypeIds.Id.Length..]
            : fullId;
        if (providerScopedId.Length < 2)
            return false;

        typeId = providerScopedId[..2];
        itemId = providerScopedId[2..];
        return !string.IsNullOrWhiteSpace(itemId);
    }
}
