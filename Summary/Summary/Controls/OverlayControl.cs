using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Summary.Controls
{
    public sealed partial class OverlayControl : ContentControl
    {
        private Grid? _overlayGrid;
        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        public string BusyMessage
        {
            get => (string)GetValue(BusyMessageProperty);
            set => SetValue(BusyMessageProperty, value);
        }

        public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(OverlayControl), new PropertyMetadata(false, OnIsBussyChanged));
        public static readonly DependencyProperty BusyMessageProperty = DependencyProperty.Register(nameof(BusyMessage), typeof(string), typeof(OverlayControl), new PropertyMetadata(string.Empty));

        private static void OnIsBussyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayControl overlayControl)
                overlayControl.UpdateOverlayVisibility((bool)e.NewValue);
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _overlayGrid = GetTemplateChild("OverlayGrid") as Grid;
            UpdateOverlayVisibility(IsBusy);
        }

        public OverlayControl()
        {
            DefaultStyleKey = typeof(OverlayControl);
        }

        private void UpdateOverlayVisibility(bool isBusy)
        {
            _overlayGrid?.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}