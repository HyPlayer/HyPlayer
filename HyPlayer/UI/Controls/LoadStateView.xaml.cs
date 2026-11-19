using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using AsyncAwaitBestPractices;
using HyPlayer.Application.Loading;

namespace HyPlayer.UI.Controls;

public sealed partial class LoadStateView : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LoadState), typeof(LoadStateView),
        new PropertyMetadata(null, (d, e) => ((LoadStateView)d).OnStateChanged(e.OldValue as LoadState)));

    public static readonly DependencyProperty RingSizeProperty = DependencyProperty.Register(
        nameof(RingSize), typeof(double), typeof(LoadStateView),
        new PropertyMetadata(32d, (d, _) => ((LoadStateView)d).ApplyRingSize()));

    private bool _isLoaded;
    private LoadState? _subscribed;

    public LoadStateView()
    {
        InitializeComponent();
    }

    public LoadState? State
    {
        get => (LoadState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    private void OnStateChanged(LoadState? previous)
    {
        if (previous is not null && ReferenceEquals(previous, _subscribed))
            Unsubscribe();

        if (_isLoaded)
            Subscribe();
        Render();
    }

    private void LoadStateView_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ApplyRingSize();
        Subscribe();
        Render();
    }

    private void LoadStateView_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (ReferenceEquals(_subscribed, State))
            return;

        Unsubscribe();
        _subscribed = State;
        if (_subscribed is not null)
            _subscribed.PropertyChanged += State_PropertyChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
            return;

        _subscribed.PropertyChanged -= State_PropertyChanged;
        _subscribed = null;
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.HasThreadAccess)
            Render();
        else
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, Render);
    }

    private void ApplyRingSize()
    {
        if (Ring is null)
            return;

        Ring.Width = RingSize;
        Ring.Height = RingSize;
    }

    private void Render()
    {
        if (ContentPanel is null)
            return;

        var state = State;
        if (state is not { IsVisible: true })
        {
            Ring.IsActive = false;
            ContentPanel.Opacity = 0;
            Visibility = Visibility.Collapsed;
            return;
        }

        var loading = state.IsLoading;
        Ring.IsActive = loading;
        Ring.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = state.Title;
        var detail = state.Detail;
        DetailText.Text = detail;
        DetailText.Visibility = loading || detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = state.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
        ContentPanel.Opacity = 1;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        State?.RetryAsync().SafeFireAndForget();
    }
}
