using System;
using System.Windows;
using System.Windows.Controls;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;

namespace KerwaKasse.MVVM.View
{
    public partial class AnalysisView : UserControl
    {
        public AnalysisView()
        {
            InitializeComponent();
        }

        // Saving/applying/managing opens a ContentDialog or changes the view behind the flyout; a
        // ContentDialog cannot open while the flyout is up, so close it first (mirrors the products view).
        private void CloseAuswertungFlyout_Click(object sender, RoutedEventArgs e)
            => (FlyoutService.GetFlyout(AuswertungButton) as FlyoutBase)?.Hide();

        // Size of the doughnut hole as a fraction of the chart's smaller side. This is THE knob to tune
        // the ring: smaller value = smaller hole = thicker ring (segments easier to see); larger = bigger
        // hole. It scales with the chart, so the proportion stays the same at every size.
        private const double DonutHoleFactor = 0.1;

        private void DonutChart_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is AnalysisViewModel vm)
                vm.SetPieInnerRadius(Math.Min(e.NewSize.Width, e.NewSize.Height) * DonutHoleFactor);
        }
    }
}
