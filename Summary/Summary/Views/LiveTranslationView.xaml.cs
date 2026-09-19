using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Summary.ViewModels;

namespace Summary.Views
{
    public sealed partial class LiveTranslationView : Page
    {
        public LiveTranslationViewModel ViewModel { get; set; } = default!;

        public LiveTranslationView()
        {
            InitializeComponent();
            ViewModel = App.ServiceProvider.GetRequiredService<LiveTranslationViewModel>();
            DataContext = ViewModel;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.InitializeAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ViewModel.Shutdown();
            base.OnNavigatedFrom(e);
        }
    }
}
