using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Summary.Common;
using Summary.Helpers;
using Summary.Views;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Summary
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger = default!;
        public MainWindow()
        {
            InitializeComponent();
            Title = SummaryConstants.APP_NAME;
            _logger = App.ServiceProvider.GetRequiredService<ILogger<MainWindow>>();
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            AppWindow.SetIcon("Assets/StoreLogo.ico");
            RootTitleBar.Subtitle = SummaryConstants.APP_VERSION;
            SetTitleBar(RootTitleBar);
            RootFrame.Navigate(typeof(DefaultView));
            _logger.LogInformation("MainWindow initialized successfully.");
        }

        /// <summary>
        /// Handles the PaneToggleRequested event of the RootTitleBar control. This event is triggered when the user clicks the pane toggle button in the title bar. The method toggles the IsPaneOpen property of the RootNavigationView, effectively opening or closing the navigation pane.
        /// </summary>
        /// <param name="sender">The TitleBar that triggered the event.</param>
        /// <param name="args">The event data.</param>
        private void RootTitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            RootNavigationView.IsPaneOpen = !RootNavigationView.IsPaneOpen;
        }

        /// <summary>
        /// Handles the SelectionChanged event of the RootNavigationView control. This event is triggered when the user selects a different item in the navigation view. The method can be used to navigate to different pages or update the content displayed in the main window based on the selected item. Currently, this method is empty and can be implemented as needed to handle navigation logic.
        /// </summary>
        /// <param name="sender">The NavigationView that triggered the event.</param>
        /// <param name="args">The event data.</param>
        private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
                RootFrame.Navigate(typeof(SettingsView));
            else
            {
                NavigationView? navigationView = args.SelectedItem as NavigationView;
                if (navigationView is not null)
                {
                    string? tag = navigationView.Tag as string;
                    if (!string.IsNullOrWhiteSpace(tag) && NavigationViewHelper.Views.TryGetValue(tag, out Type? view))
                    {
                        RootFrame.Navigate(view);
                    }
                }
            }
        }
    }
}