using HyPlayer.ViewModels;

namespace HyPlayer.Pages
{
    /// <summary>
    /// Base class for AlbumPage that provides ViewModel support.
    /// This demonstrates the MVVM pattern where the page extends AppPageBase
    /// with its corresponding ViewModel type.
    /// </summary>
    public class AlbumPageBase : AppPageBase<AlbumViewModel>
    {
        public AlbumPageBase()
        {
            // ViewModel is automatically injected by AppPageBase
            // DataContext is automatically set to ViewModel
        }
    }
}
