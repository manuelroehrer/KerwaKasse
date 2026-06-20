using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KerwaKasse.Controls
{
    /// <summary>
    /// Reusable touch-friendly number stepper: a minus and plus button flanking a typeable number
    /// field, styled like a ModernWpf text control (border, corner radius, hover, focus accent and
    /// disabled state). The value is clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>].
    /// </summary>
    public partial class NumberStepper : UserControl
    {
        // Disabled visuals are applied imperatively (a local value), so nothing in the style system
        // can override them.
        private static readonly Brush DisabledBackground = Frozen(Color.FromRgb(0xD7, 0xDB, 0xDF));
        private static readonly Brush DisabledBorder = Frozen(Color.FromRgb(0xC4, 0xC9, 0xCE));

        public NumberStepper()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                UpdateButtonStates();
                ApplyEnabledState();
            };
            IsEnabledChanged += (_, _) => ApplyEnabledState();
        }

        private static Brush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        // When disabled, force a clearly grey fill + border; when enabled, revert to the style
        // (white background, hover-aware border).
        private void ApplyEnabledState()
        {
            if (IsEnabled)
            {
                BgBorder.ClearValue(Border.BackgroundProperty);
                ClearFrameSegmentBorders();
            }
            else
            {
                BgBorder.Background = DisabledBackground;
                LeftFrameSegment.BorderBrush = DisabledBorder;
                MiddleFrameSegment.BorderBrush = DisabledBorder;
                RightFrameSegment.BorderBrush = DisabledBorder;
            }
        }

        private void ClearFrameSegmentBorders()
        {
            LeftFrameSegment.ClearValue(Border.BorderBrushProperty);
            MiddleFrameSegment.ClearValue(Border.BorderBrushProperty);
            RightFrameSegment.ClearValue(Border.BorderBrushProperty);
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(NumberStepper),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged, CoerceValue));

        public int Value
        {
            get => (int)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(int), typeof(NumberStepper),
            new PropertyMetadata(0, OnRangeChanged));

        public int Minimum
        {
            get => (int)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(int), typeof(NumberStepper),
            new PropertyMetadata(100, OnRangeChanged));

        public int Maximum
        {
            get => (int)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var stepper = (NumberStepper)d;
            var value = (int)baseValue;
            if (value < stepper.Minimum) return stepper.Minimum;
            if (value > stepper.Maximum) return stepper.Maximum;
            return value;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((NumberStepper)d).UpdateButtonStates();

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var stepper = (NumberStepper)d;
            stepper.CoerceValue(ValueProperty);
            stepper.UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            MinusButton.IsEnabled = Value > Minimum;
            PlusButton.IsEnabled = Value < Maximum;
        }

        private void Minus_Click(object sender, RoutedEventArgs e)
        {
            if (Value > Minimum) Value--;
        }

        private void Plus_Click(object sender, RoutedEventArgs e)
        {
            if (Value < Maximum) Value++;
        }

        // Restrict typed input to digits (the bound value is clamped to the range on top of this).
        private void ValueBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }
}
