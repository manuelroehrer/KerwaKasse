using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KerwaKasse.MVVM.View
{
    public partial class InfoView : UserControl
    {
        public InfoView()
        {
            InitializeComponent();
        }

        private EventsViewModel Events => (DataContext as InfoViewModel)?.Events;

        // Every action in the event flyout opens a ContentDialog next, and a ContentDialog cannot open
        // while the flyout is up, so the flyout is closed first (same as the Speisekarten dropdown).
        private void RunFromFlyout(ICommand command, object sender)
        {
            var item = (sender as FrameworkElement)?.DataContext as EventListItem;
            (FlyoutService.GetFlyout(EventButton) as FlyoutBase)?.Hide();
            command?.Execute(item);
        }

        private void EventOption_Click(object sender, RoutedEventArgs e) => RunFromFlyout(Events?.SwitchCommand, sender);
        private void RenameEvent_Click(object sender, RoutedEventArgs e) => RunFromFlyout(Events?.RenameCommand, sender);
        private void DeleteEvent_Click(object sender, RoutedEventArgs e) => RunFromFlyout(Events?.DeleteCommand, sender);
        private void CreateEvent_Click(object sender, RoutedEventArgs e) => RunFromFlyout(Events?.CreateCommand, sender);

        // Give the pill an accent border while its flyout is open (mirrors a focused ModernWpf control).
        private void EventFlyout_Opened(object sender, object e) => EventButton.Tag = "open";
        private void EventFlyout_Closed(object sender, object e) => EventButton.Tag = null;
    }
}
