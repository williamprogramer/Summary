using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Summary.Controls
{
    public sealed partial class OverlayControl : ContentControl
    {
        private Grid? _overlayGrid;
        public bool IsBussy
        {
            get => (bool)GetValue(IsBussyProperty);
            set => SetValue(IsBussyProperty, value);
        }

        public static readonly DependencyProperty IsBussyProperty = DependencyProperty.Register(nameof(IsBussy), typeof(bool), typeof(OverlayControl), new PropertyMetadata(false, OnIsBussyChanged));

        private static void OnIsBussyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayControl overlayControl)
                overlayControl.UpdateOverlayVisibility((bool)e.NewValue);
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _overlayGrid = GetTemplateChild("OverlayGrid") as Grid;
            UpdateOverlayVisibility(IsBussy);
        }

        public OverlayControl()
        {
            DefaultStyleKey = typeof(OverlayControl);
        }

        private void UpdateOverlayVisibility(bool isBussy)
        {
            _overlayGrid?.Visibility = isBussy ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}