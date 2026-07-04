using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace KerwaKasse.Helper
{
    /// <summary>Clicking empty (non-focusable) space in WPF does not move keyboard focus away from
    /// whatever control currently has it, so that control's own LostFocus-triggered commit never
    /// runs — e.g. a date typed into a DatePicker, or KerwaKasse's own click-to-edit position box.
    /// Wire <see cref="CommitPendingEditOnOutsideClick"/> to a view's root PreviewMouseDown to force
    /// that commit. Clicking inside the focused control's own visual tree (e.g. to reposition the
    /// caret) is left alone, and clicking another focusable control still focuses it normally right
    /// after, so ordinary interactions are unaffected.</summary>
    public static class FocusCommitBehavior
    {
        public static void CommitPendingEditOnOutsideClick(MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is not DependencyObject focused) return;
            if (e.OriginalSource is DependencyObject clicked && IsSelfOrDescendant(focused, clicked)) return;
            Keyboard.ClearFocus();
        }

        private static bool IsSelfOrDescendant(DependencyObject ancestor, DependencyObject node)
        {
            for (var current = node; current != null; current = GetParent(current))
                if (Equals(current, ancestor)) return true;
            return false;
        }

        private static DependencyObject GetParent(DependencyObject d) =>
            d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
    }
}
