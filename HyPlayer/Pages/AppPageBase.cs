using HyPlayer.Contracts.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;


namespace HyPlayer.Pages
{
    public abstract class AppPageBase<TViewModel> : Page
    where TViewModel : class, IViewModel
    {
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(TViewModel), typeof(TViewModel),
                                        new PropertyMetadata(default));

        public TViewModel ViewModel
        {
            get => (TViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        protected AppPageBase()
        {
            ViewModel = Locator.Instance.GetService<TViewModel>();
            DataContext = ViewModel;
        }
    }

}
