using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace HyPlayer.Shell.Search;

public sealed partial class ShellSearchBox : UserControl
{
    private readonly ShellSearchViewModel _viewModel = Ioc.Default.GetRequiredService<ShellSearchViewModel>();
    private CancellationTokenSource? _suggestionCts;

    public ShellSearchBox()
    {
        InitializeComponent();
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        _suggestionCts?.Cancel();
        _suggestionCts?.Dispose();
        _suggestionCts = new CancellationTokenSource();
        var token = _suggestionCts.Token;
        var keyword = sender.Text;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            sender.ItemsSource = null;
            return;
        }

        try
        {
            await Task.Delay(250, token);
            var suggestions = await _viewModel.GetSuggestionsAsync(keyword, token);
            if (!token.IsCancellationRequested && sender.Text == keyword)
                sender.ItemsSource = suggestions;
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke superseded this request.
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _suggestionCts?.Cancel();
        SearchAutoSuggestBox.ItemsSource = null;
    }

    private void SearchBox_Unloaded(object sender, RoutedEventArgs e)
    {
        _suggestionCts?.Cancel();
        _suggestionCts?.Dispose();
        _suggestionCts = null;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _viewModel.NavigateToSearch(sender.Text);
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = (string)args.SelectedItem;
    }
}
