using CommunityToolkit.Mvvm.DependencyInjection;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Shell.Search;

public sealed partial class ShellSearchBox : UserControl
{
    private readonly ShellSearchViewModel _viewModel = Ioc.Default.GetRequiredService<ShellSearchViewModel>();

    public ShellSearchBox()
    {
        InitializeComponent();
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        IReadOnlyList<string>? suggestions = await _viewModel.GetSuggestionsAsync(sender.Text);
        sender.ItemsSource = suggestions;
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchAutoSuggestBox.ItemsSource = null;
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