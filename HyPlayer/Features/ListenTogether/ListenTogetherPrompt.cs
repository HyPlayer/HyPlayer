using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Features.ListenTogether;

internal sealed class ListenTogetherPrompt(IUIThreadDispatcher ui) : IListenTogetherPrompt
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1.5);
    private const int MaxAttempts = 20;
    private int _showing;

    public async Task<ListenTogetherTakeoverChoice> AskAsync(ListenTogetherPromptRequest request)
    {
        if (Interlocked.CompareExchange(ref _showing, 1, 0) != 0) return ListenTogetherTakeoverChoice.Cancel;
        try
        {
            var choice = ListenTogetherTakeoverChoice.Cancel;
            await ui.TryRunAsync(async () => choice = await ShowAsync(request)).ConfigureAwait(false);
            return choice;
        }
        finally
        {
            Volatile.Write(ref _showing, 0);
        }
    }

    private static async Task<ListenTogetherTakeoverChoice> ShowAsync(ListenTogetherPromptRequest request)
    {
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = new TextBlock { Text = request.Description, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = request.ContinueText,
            SecondaryButtonText = request.EndText,
            CloseButtonText = request.IgnoreText,
            DefaultButton = ContentDialogButton.Primary
        };
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await dialog.ShowAsync() switch
                {
                    ContentDialogResult.Primary => ListenTogetherTakeoverChoice.TakeOver,
                    ContentDialogResult.Secondary => ListenTogetherTakeoverChoice.End,
                    _ => ListenTogetherTakeoverChoice.Cancel
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Listen together prompt waiting for another dialog: {ex.Message}");
                await Task.Delay(RetryDelay);
            }
        }

        return ListenTogetherTakeoverChoice.Cancel;
    }
}
