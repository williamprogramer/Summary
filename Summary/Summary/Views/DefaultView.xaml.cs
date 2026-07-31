using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Summary.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Summary.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class DefaultView : Page
    {
        public DefaultViewModel ViewModel { get; set; } = default!;
        public DefaultView()
        {
            InitializeComponent();
            ViewModel = App.ServiceProvider.GetRequiredService<DefaultViewModel>();
            DataContext = ViewModel;
        }
    }
}