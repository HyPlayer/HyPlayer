using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Comment;
using HyPlayer.NeteaseApi.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.ViewModels
{
    public partial class CommentsViewModel : ObservableRecipient
    {
        [ObservableProperty]
        public partial ObservableCollection<Comment> HotComments { get; set; }
        [ObservableProperty]
        public partial ObservableCollection<Comment> NormalComments { get; set; }
        [ObservableProperty]
        public partial bool NextPageEnabled { get; set; }
        [ObservableProperty]
        public partial bool PrevPageEnabled { get; set; }

        private NeteaseCloudMusicApiHandler _neteaseApi;
        private string _cursor;
        private int _page = 1;
        private string _resourceId;
        private NeteaseResourceType _resourceType;
        private int _sortType = 1;
        private bool _isShiftingPage = false;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private CancellationToken _cancellationToken;
        private Task _commentLoaderTask;
        private Task _hotCommentLoaderTask;

        public CommentsViewModel(NeteaseCloudMusicApiHandler neteaseApi)
        {
            _neteaseApi = neteaseApi;
            _cancellationToken = _cancellationTokenSource.Token;
            HotComments = new ObservableCollection<Comment>();
            NormalComments = new ObservableCollection<Comment>();
        }

        public void Initialize(string resstr)
        {
            if (string.IsNullOrEmpty(resstr))
                return;

            _resourceId = resstr.Substring(2);
            switch (resstr.Substring(0, 2))
            {
                case "sg":
                    _resourceType = NeteaseResourceType.Song;
                    break;
                case "mv":
                    _resourceType = NeteaseResourceType.MV;
                    break;
                case "fm":
                    _resourceType = NeteaseResourceType.RadioProgram;
                    break;
                case "mb":
                    _resourceType = NeteaseResourceType.MLog;
                    break;
                case "al":
                    _resourceType = NeteaseResourceType.Album;
                    break;
                case "pl":
                    _resourceType = NeteaseResourceType.Playlist;
                    break;
            }

            LoadHotComments();
            _commentLoaderTask = LoadComments(_sortType);
        }

        private void LoadHotComments()
        {
            _hotCommentLoaderTask = LoadComments(2);
        }

        public async Task LoadComments(int type)
        {
            if (string.IsNullOrEmpty(_resourceId)) return;
            if (_isShiftingPage) return;
            _cancellationToken.ThrowIfCancellationRequested();

            var result = await _neteaseApi.RequestAsync(NeteaseApis.CommentsApi, new CommentsRequest
            {
                ResourceType = _resourceType,
                ResourceId = _resourceId,
                CommentSortType = type switch
                {
                    2 => CommentSortType.Hot,
                    3 => CommentSortType.Time,
                    _ => CommentSortType.Recommend
                },
                PageSize = 20,
                PageNo = _page,
                Cursor = _page != 1 && type == 3 ? _cursor : null
            }, _cancellationToken);

            if (result.IsError)
            {
                Common.AddToTeachingTipLists("加载评论时出错", result.Error.Message);
                return;
            }

            if (type == 2)
                HotComments.Clear();
            else
                NormalComments.Clear();

            foreach (var comment in result.Value?.Data?.Comments ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var cmt = comment.MapToComment();
                cmt.ResourceType = _resourceType;
                cmt.ResourceId = _resourceId;
                if (type == 2)
                    HotComments.Add(cmt);
                else
                    NormalComments.Add(cmt);
            }

            if (type == 3)
                _cursor = result.Value?.Data?.Cursor;

            NextPageEnabled = result.Value?.Data?.HasMore == true;
            PrevPageEnabled = _page > 1;
        }

        public void NextPage()
        {
            _page++;
            _commentLoaderTask = LoadComments(_sortType);
        }

        public void PrevPage()
        {
            _page--;
            _commentLoaderTask = LoadComments(_sortType);
        }

        public void SendComment()
        {
            // TODO: 评论功能风控
            Common.AddToTeachingTipLists("评论功能暂时关闭", "由于网易云音乐风控策略，评论功能暂时关闭");
        }

        public void ChangeSort(int sortType)
        {
            _sortType = sortType + 1;
            _page = 1;
            _commentLoaderTask = LoadComments(_sortType);
        }

        public void SkipPage(int pageNumber)
        {
            if (pageNumber > 0)
            {
                _page = pageNumber;
                _commentLoaderTask = LoadComments(_sortType);
            }
        }

        public async Task CleanupAsync()
        {
            if (_commentLoaderTask != null && !_commentLoaderTask.IsCompleted)
            {
                try
                {
                    _cancellationTokenSource.Cancel();
                    await _commentLoaderTask;
                }
                catch
                {
                }
            }
            if (_hotCommentLoaderTask != null && !_hotCommentLoaderTask.IsCompleted)
            {
                try
                {
                    _cancellationTokenSource.Cancel();
                    await _hotCommentLoaderTask;
                }
                catch
                {
                }
            }
            _cancellationTokenSource.Dispose();
        }
    }
}
