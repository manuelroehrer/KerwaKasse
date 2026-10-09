using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KerwaKasse.MVVM.View
{
    /// <summary>
    /// "Freie Eingabe" in two steps: first the amount on the number pad, then what it is for, a
    /// description and/or an existing product the amount is a special price for. Neither is required
    /// as such, but adding without both takes a second, deliberate tap. With an existing entry passed
    /// in, only the second step is shown, to change what the entry is for. <see cref="Result"/> is null
    /// when the dialog was closed without adding.
    /// </summary>
    public partial class FreeEntryDialog : ContentDialog
    {
        private const int SuggestionCount = 5;
        private static readonly Brush SuggestionCaptionBrush = Frozen(Color.FromRgb(0x8A, 0x8F, 0x96));

        private readonly IReadOnlyList<string> _recentNotes;
        private bool _onPurposeStep;

        public FreeEntryResult Result { get; private set; }

        public FreeEntryDialog(IReadOnlyList<string> recentNotes, IReadOnlyList<ProductModel> products, FreeEntryResult existing = null)
        {
            InitializeComponent();
            _recentNotes = recentNotes ?? Array.Empty<string>();

            // A special price only makes sense for a product with a fixed price; a variable one has its
            // own price at every sale anyway.
            var choices = (products ?? Array.Empty<ProductModel>()).Where(p => !p.VariablePrice).ToList();
            // The product of an existing entry stays selectable, even when it is no longer offered.
            if (existing?.Product != null && choices.All(p => p.ProductID != existing.Product.ProductID))
                choices.Insert(0, existing.Product);

            var options = new List<ProductOption> { new(null, "Kein Produkt") };
            options.AddRange(choices.Select(p => new ProductOption(p,
                $"{p.Name} (sonst {(p.VariablePrice ? "ca. " : "")}{p.Price.ToString("C", CultureInfo.CurrentCulture)})")));
            ProductBox.ItemsSource = options;
            ProductBox.SelectedIndex = 0;

            Pad.ValueChanged += (_, _) => AmountError.Visibility = Visibility.Collapsed;
            NoteBox.TextChanged += (_, _) => { Hurdle.Visibility = Visibility.Collapsed; RefreshSuggestions(); };
            ProductBox.SelectionChanged += (_, _) => Hurdle.Visibility = Visibility.Collapsed;

            Opened += (_, _) =>
            {
                PlaceNearTop();
                if (_onPurposeStep) NoteBox.Focus();
                else Pad.Focus();
            };
            PreviewTextInput += (_, e) => { if (!_onPurposeStep) e.Handled = Pad.HandleTextInput(e.Text); };
            PreviewKeyDown += OnPreviewKeyDown;

            if (existing != null)
            {
                Pad.SetSuggestion(existing.Amount);
                NoteBox.Text = existing.Note ?? string.Empty;
                NoteBox.CaretIndex = NoteBox.Text.Length;
                ProductBox.SelectedItem = options.FirstOrDefault(o => o.Product?.ProductID == existing.Product?.ProductID);
                ShowPurposeStep();
                // Only what it is for can be changed here, so there is no way back to the amount.
                BackButton.Visibility = Visibility.Collapsed;
                PrimaryText.Text = "Übernehmen";
                WithoutDescriptionButton.Content = "Trotzdem ohne Angabe übernehmen";
            }

            RefreshSuggestions();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter when !ProductBox.IsDropDownOpen:
                    Primary_Click(this, e);
                    e.Handled = true;
                    break;
                case Key.Back when !_onPurposeStep:
                    Pad.HandleBackspace();
                    e.Handled = true;
                    break;
                case Key.Escape when !ProductBox.IsDropDownOpen:
                    Hide();
                    e.Handled = true;
                    break;
            }
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (!_onPurposeStep)
                ShowPurposeStep();
            else if (TryAccept(withoutDescription: false))
                Hide();
        }

        private void Back_Click(object sender, RoutedEventArgs e) => ShowAmountStep();

        private void Close_Click(object sender, RoutedEventArgs e) => Hide();

        private void WithoutDescription_Click(object sender, RoutedEventArgs e)
        {
            if (TryAccept(withoutDescription: true))
                Hide();
        }

        private void ShowPurposeStep()
        {
            if (Pad.Value <= 0)
            {
                AmountError.Visibility = Visibility.Visible;
                return;
            }

            // Keep the height of the amount step, so the dialog does not jump.
            StepArea.MinHeight = AmountStep.ActualHeight;

            _onPurposeStep = true;
            TitleText.Text = $"Freie Eingabe · {Pad.Value.ToString("C", CultureInfo.CurrentCulture)}";
            AmountStep.Visibility = Visibility.Collapsed;
            PurposeStep.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            PrimaryText.Text = "Hinzufügen";
            // With a physical keyboard the description can be typed right away; on touch, tapping the
            // field opens the Windows touch keyboard.
            NoteBox.Focus();
        }

        private void ShowAmountStep()
        {
            _onPurposeStep = false;
            Hurdle.Visibility = Visibility.Collapsed;
            TitleText.Text = "Freie Eingabe";
            PurposeStep.Visibility = Visibility.Collapsed;
            AmountStep.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Hidden;
            PrimaryText.Text = "Weiter";
            Pad.Focus();
        }

        private bool TryAccept(bool withoutDescription)
        {
            string note = NoteBox.Text.Trim();
            var product = (ProductBox.SelectedItem as ProductOption)?.Product;
            if (!withoutDescription && note.Length == 0 && product == null)
            {
                Hurdle.Visibility = Visibility.Visible;
                // Back to the text field, so a description can be typed right away.
                NoteBox.Focus();
                return false;
            }

            Result = new FreeEntryResult(Pad.Value, note.Length > 0 ? note : null, product);
            return true;
        }

        /// <summary>Shows the recent descriptions that fit what has been typed so far.</summary>
        private void RefreshSuggestions()
        {
            Suggestions.Children.Clear();
            string typed = NoteBox.Text.Trim();
            var matches = _recentNotes
                .Where(n => typed.Length == 0
                            || (n.Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                                && !n.Equals(typed, StringComparison.CurrentCultureIgnoreCase)))
                .Take(SuggestionCount)
                .ToList();

            Suggestions.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (matches.Count == 0) return;

            Suggestions.Children.Add(new TextBlock
            {
                Text = "Zuletzt:",
                FontSize = 12.5,
                Foreground = SuggestionCaptionBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4)
            });
            foreach (var note in matches)
            {
                // Not focusable: a tap leaves the focus (and the touch keyboard) in the text box.
                var chip = new Button
                {
                    Content = note,
                    FontSize = 13,
                    Padding = new Thickness(10, 3, 10, 4),
                    Margin = new Thickness(0, 0, 6, 4),
                    Focusable = false
                };
                chip.Click += (_, _) =>
                {
                    NoteBox.Text = note;
                    NoteBox.CaretIndex = note.Length;
                };
                Suggestions.Children.Add(chip);
            }
        }

        /// <summary>Moves the dialog card up, so the Windows touch keyboard (docked at the bottom) does not
        /// cover the description field.</summary>
        private void PlaceNearTop()
        {
            if (GetTemplateChild("BackgroundElement") is FrameworkElement card)
            {
                card.VerticalAlignment = VerticalAlignment.Top;
                card.Margin = new Thickness(0, 48, 0, 0);
            }
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private sealed record ProductOption(ProductModel Product, string Text)
        {
            // What UI Automation (screen readers) reads for the item, instead of the record's member dump.
            public override string ToString() => Text;
        }
    }
}
