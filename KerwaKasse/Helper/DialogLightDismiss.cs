using ModernWpf.Controls;
using System.Windows;
using System.Windows.Input;

namespace KerwaKasse.Helper
{
    /// <summary>Closes a <see cref="ContentDialog"/> when the dimmed area around it is tapped, the same as
    /// "Abbrechen" (ShowAsync returns None). ModernWpf dialogs don't do that on their own. Registered once at
    /// startup, so it covers every dialog of the app; a dialog that must stay open sets
    /// <see cref="KeepOpenProperty"/>.</summary>
    public static class DialogLightDismiss
    {
        /// <summary>Keeps the dialog open on a tap outside, e.g. while it holds unsaved edits or runs a
        /// download that closing would cancel.</summary>
        public static readonly DependencyProperty KeepOpenProperty =
            DependencyProperty.RegisterAttached("KeepOpen", typeof(bool), typeof(DialogLightDismiss), new PropertyMetadata(false));

        public static bool GetKeepOpen(DependencyObject obj) => (bool)obj.GetValue(KeepOpenProperty);
        public static void SetKeepOpen(DependencyObject obj, bool value) => obj.SetValue(KeepOpenProperty, value);

        public static void Register() =>
            EventManager.RegisterClassHandler(typeof(ContentDialog), UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown));

        // Touch taps arrive here too: WPF promotes them to mouse events.
        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ContentDialog dialog || GetKeepOpen(dialog)) return;

            // "BackgroundElement" is the dialog card in ModernWpf's template; around it lies the dimmed layer
            // that covers the whole window.
            if (dialog.Template?.FindName("BackgroundElement", dialog) is not FrameworkElement card) return;

            var position = e.GetPosition(card);
            if (position.X >= 0 && position.Y >= 0 && position.X <= card.ActualWidth && position.Y <= card.ActualHeight)
                return;

            dialog.Hide();
            e.Handled = true;
        }
    }
}
