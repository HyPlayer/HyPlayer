using HyPlayer.Features.Downloads.Services;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.Navigation;
using HyPlayer.UI.Dialogs;
using HyPlayer.UI.Lists;
using HyPlayer.UI.Lists.IncrementalLoading;
using HyPlayer.UI.Playback.PlayBar;
using ObservableCollections;
using WinRT;

// ObservableCollections creates a generic implementation object behind
// ToNotifyCollectionChanged(). Register every closed implementation that is
// passed to XAML so CsWinRT can generate its CCW vtable for NativeAOT.
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<DownloadObject, DownloadObject>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<LocalSong, LocalSong>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<CommentBase, CommentBase>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<NavigationNode, NavigationNode>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<ProvidableItemRowViewModel, ProvidableItemRowViewModel>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<ProvidableItemRowGroup, ProvidableItemRowGroup>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<PlayBarQueueItem, PlayBarQueueItem>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<LyricEffectOperationItem, LyricEffectOperationItem>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<FocusedTextOperationItem, FocusedTextOperationItem>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(NonFilteredSynchronizedViewList<LyricShareItem, LyricShareItem>))]

// These two closed incremental collections are also passed through XAML (as
// ISupportIncrementalLoading/object), so they need concrete CCWs as well.
[assembly: GeneratedWinRTExposedExternalType(typeof(IncrementalLoadingCollection<CommentBase>))]
[assembly: GeneratedWinRTExposedExternalType(typeof(IncrementalLoadingCollection<ProvidableItemRowViewModel>))]
