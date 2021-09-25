#region

using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Pages
{
    /// <summary>
    ///     An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Welcome : Page
    {
        public Welcome()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (Common.Logined)
                TBHINT.Text = "点击侧边按钮开始吧~";
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ImageE.Source = null;
        }
    }
}