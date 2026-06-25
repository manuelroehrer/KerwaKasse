using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KerwaKasse.Controls
{
    /// <summary>
    /// A connected group of mutually exclusive options rendered as a single control: one rounded
    /// outer frame, a vertical divider between each segment (outer corners rounded, inner edges
    /// square) and the selected segment filled with the accent colour. Generalises the segmented
    /// look already used by <see cref="NumberStepper"/> for an arbitrary number of options.
    ///
    /// Bind <see cref="ItemsSource"/> to a collection whose items expose <c>Label</c> and an
    /// observable <c>IsSelected</c> (e.g. AnalysisSegmentItem). <see cref="Command"/> is invoked with
    /// the clicked item as its parameter; the view model keeps <c>IsSelected</c> in sync.
    /// </summary>
    public partial class SegmentedControl : UserControl
    {
        public SegmentedControl() => InitializeComponent();

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IEnumerable), typeof(SegmentedControl));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(SegmentedControl));

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        /// <summary>Inner padding of each segment (lets callers size the control for tabs vs. compact pills).</summary>
        public static readonly DependencyProperty SegmentPaddingProperty = DependencyProperty.Register(
            nameof(SegmentPadding), typeof(Thickness), typeof(SegmentedControl),
            new PropertyMetadata(new Thickness(18, 8, 18, 8)));

        public Thickness SegmentPadding
        {
            get => (Thickness)GetValue(SegmentPaddingProperty);
            set => SetValue(SegmentPaddingProperty, value);
        }
    }
}
