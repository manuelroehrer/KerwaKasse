using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace KerwaKasse.Helper
{
    /// <summary>The dragged item and its old and new index, passed to
    /// <see cref="DragReorder.MoveCommandProperty"/> when a drag ends on a different slot.</summary>
    public sealed record DragReorderMove(object Item, int OldIndex, int NewIndex);

    /// <summary>Drag-and-drop reordering for an ItemsControl, in the style of the sortable lists of
    /// web apps: the grabbed item lifts off as a floating copy (rendered with
    /// <see cref="DragTemplateProperty"/>) that follows the mouse, the other items slide aside to
    /// open a gap where it will land, and on release it settles into that gap. The bound collection
    /// stays untouched while dragging; only on drop is <see cref="MoveCommandProperty"/> executed, so
    /// the view model reorders and persists once. Escape or losing the mouse cancels.
    ///
    /// Every item slides from its own layout slot to the slot of its new index, so the same code
    /// serves a vertical list and a wrap panel. That assumes a non-virtualizing items panel (all
    /// containers exist and keep their slots) and items of one size, as with KerwaKasse's product
    /// rows. While dragging, the source container carries <see cref="IsDragSourceProperty"/> so its
    /// style can draw it as the placeholder that marks where the item will land.</summary>
    public static class DragReorder
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(DragReorder),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty MoveCommandProperty =
            DependencyProperty.RegisterAttached(
                "MoveCommand",
                typeof(ICommand),
                typeof(DragReorder),
                new PropertyMetadata(null));

        /// <summary>Template of the floating copy; falls back to the ItemsControl's ItemTemplate.</summary>
        public static readonly DependencyProperty DragTemplateProperty =
            DependencyProperty.RegisterAttached(
                "DragTemplate",
                typeof(DataTemplate),
                typeof(DragReorder),
                new PropertyMetadata(null));

        /// <summary>Set on the dragged item's container while it stands in as the landing placeholder.</summary>
        public static readonly DependencyProperty IsDragSourceProperty =
            DependencyProperty.RegisterAttached(
                "IsDragSource",
                typeof(bool),
                typeof(DragReorder),
                new PropertyMetadata(false));

        private static readonly DependencyProperty ControllerProperty =
            DependencyProperty.RegisterAttached(
                "Controller",
                typeof(DragReorderController),
                typeof(DragReorder),
                new PropertyMetadata(null));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static ICommand GetMoveCommand(DependencyObject obj) => (ICommand)obj.GetValue(MoveCommandProperty);
        public static void SetMoveCommand(DependencyObject obj, ICommand value) => obj.SetValue(MoveCommandProperty, value);

        public static DataTemplate GetDragTemplate(DependencyObject obj) => (DataTemplate)obj.GetValue(DragTemplateProperty);
        public static void SetDragTemplate(DependencyObject obj, DataTemplate value) => obj.SetValue(DragTemplateProperty, value);

        public static bool GetIsDragSource(DependencyObject obj) => (bool)obj.GetValue(IsDragSourceProperty);
        public static void SetIsDragSource(DependencyObject obj, bool value) => obj.SetValue(IsDragSourceProperty, value);

        // The handlers stay attached once created; IsEnabled = false only keeps new drags from starting
        // (e.g. while a search filter hides part of the list and indices no longer match the collection).
        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ItemsControl owner && e.NewValue is true && owner.GetValue(ControllerProperty) == null)
                owner.SetValue(ControllerProperty, new DragReorderController(owner));
        }
    }

    internal sealed class DragReorderController
    {
        private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(180));
        private static readonly Duration LiftDuration = new(TimeSpan.FromMilliseconds(120));
        private static readonly IEasingFunction Ease = CreateEase();

        // Auto-scroll while the mouse is within this distance of the list's top or bottom edge (or past
        // it), up to this many pixels per tick, faster the closer it gets.
        private const double AutoScrollZone = 40;
        private const double AutoScrollMaxStep = 14;

        private enum State { Idle, Pending, Dragging, Settling }

        private readonly ItemsControl _owner;
        private State _state;

        private FrameworkElement _sourceContainer;
        private Point _pressPoint;
        private UIElement _hitTestBlocked;

        private Panel _panel;
        private FrameworkElement _viewport;
        private ScrollViewer _scrollViewer;
        private FrameworkElement[] _containers;
        private Transform[] _originalTransforms;
        private TranslateTransform[] _shifts;
        private Vector[] _shiftTargets;
        private Rect[] _slots;
        private int _sourceIndex;
        private int _targetIndex;
        private Vector _grabOffset;
        private bool _verticalOnly;
        private AdornerLayer _cardLayer;
        private DragReorderAdorner _card;
        private DispatcherTimer _autoScrollTimer;
        private Window _window;

        public DragReorderController(ItemsControl owner)
        {
            _owner = owner;
            owner.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            owner.PreviewMouseMove += OnPreviewMouseMove;
            owner.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            owner.LostMouseCapture += OnLostMouseCapture;
            owner.Unloaded += (_, _) => Abort();
        }

        // ── Mouse input ──────────────────────────────────────────

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_state != State.Idle || !DragReorder.GetIsEnabled(_owner)) return;
            if (e.OriginalSource is not DependencyObject source) return;
            if (_owner.ContainerFromElement(source) is not FrameworkElement container) return;
            if (IsInsideInteractiveControl(source, container)) return;

            // Not handled: a plain click still selects the item as usual.
            _sourceContainer = container;
            _pressPoint = e.GetPosition(_owner);
            _state = State.Pending;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            switch (_state)
            {
                case State.Pending:
                    if (e.LeftButton != MouseButtonState.Pressed) { ResetPending(); return; }
                    e.Handled = true;
                    BlockItemHitTesting();
                    Vector moved = e.GetPosition(_owner) - _pressPoint;
                    if (Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance)
                        BeginDrag();
                    break;

                case State.Dragging:
                    e.Handled = true;
                    UpdateDrag();
                    break;

                case State.Settling:
                    e.Handled = true;
                    break;
            }
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            switch (_state)
            {
                case State.Pending:
                    ResetPending();
                    break;
                case State.Dragging:
                    e.Handled = true;
                    Settle(commit: true);
                    break;
                case State.Settling:
                    e.Handled = true;
                    break;
            }
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_state == State.Dragging && Mouse.Captured != _owner)
                Settle(commit: false);
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_state == State.Dragging && e.Key == Key.Escape)
            {
                e.Handled = true;
                Settle(commit: false);
            }
        }

        private void ResetPending()
        {
            RestoreItemHitTesting();
            _state = State.Idle;
            _sourceContainer = null;
        }

        // A ListBox selects every row the pressed mouse enters (its drag-to-select), even while it
        // holds the mouse capture. With the rows invisible to hit testing during a drag, no row is
        // entered, so the selection stays on the dragged item and no row shows its hover state.
        private void BlockItemHitTesting()
        {
            if (_hitTestBlocked != null || VisualTreeHelper.GetParent(_sourceContainer) is not UIElement panel) return;
            panel.IsHitTestVisible = false;
            _hitTestBlocked = panel;
        }

        private void RestoreItemHitTesting()
        {
            _hitTestBlocked?.ClearValue(UIElement.IsHitTestVisibleProperty);
            _hitTestBlocked = null;
        }

        // ── Drag lifecycle ───────────────────────────────────────

        private void BeginDrag()
        {
            var generator = _owner.ItemContainerGenerator;
            int count = _owner.Items.Count;
            _panel = VisualTreeHelper.GetParent(_sourceContainer) as Panel;
            _cardLayer = AdornerLayer.GetAdornerLayer(_owner);
            _sourceIndex = generator.IndexFromContainer(_sourceContainer);
            if (count < 2 || _panel is not { IsItemsHost: true } || _cardLayer == null || _sourceIndex < 0)
            {
                ResetPending();
                return;
            }

            _containers = new FrameworkElement[count];
            _slots = new Rect[count];
            for (int i = 0; i < count; i++)
            {
                // A missing container means a virtualizing panel, which this behaviour does not support.
                if (generator.ContainerFromIndex(i) is not FrameworkElement c) { ResetPending(); return; }
                _containers[i] = c;
                _slots[i] = new Rect(c.TranslatePoint(new Point(), _panel), c.RenderSize);
            }

            _viewport = FindAncestor<ScrollContentPresenter>(_panel);
            _scrollViewer = FindAncestor<ScrollViewer>(_panel);
            _verticalOnly = _slots.All(s => Math.Abs(s.X - _slots[0].X) < 0.5);
            _targetIndex = _sourceIndex;
            _grabOffset = _owner.TranslatePoint(_pressPoint, _panel) - _slots[_sourceIndex].TopLeft;

            _originalTransforms = new Transform[count];
            _shifts = new TranslateTransform[count];
            _shiftTargets = new Vector[count];
            for (int i = 0; i < count; i++)
            {
                _originalTransforms[i] = _containers[i].RenderTransform;
                _shifts[i] = new TranslateTransform();
                _containers[i].RenderTransform = _shifts[i];
            }

            var item = generator.ItemFromContainer(_sourceContainer);
            var dragTemplate = DragReorder.GetDragTemplate(_owner) ?? _owner.ItemTemplate;
            _card = new DragReorderAdorner(_owner, item, dragTemplate, new Size(_slots[_sourceIndex].Width, double.NaN));
            _cardLayer.Add(_card);
            DragReorder.SetIsDragSource(_sourceContainer, true);

            Mouse.Capture(_owner, CaptureMode.Element);
            Mouse.OverrideCursor = _verticalOnly ? Cursors.SizeNS : Cursors.SizeAll;
            _window = Window.GetWindow(_owner);
            if (_window != null) _window.PreviewKeyDown += OnWindowPreviewKeyDown;
            _autoScrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Input,
                                                   OnAutoScrollTick, _owner.Dispatcher);
            _autoScrollTimer.Start();

            _state = State.Dragging;
            UpdateDrag();
            _card.ScaleTo(DragReorderAdorner.LiftScale, LiftDuration, Ease);
        }

        /// <summary>Moves the floating copy with the mouse (kept inside the visible list area) and
        /// re-targets to the landing slot whose centre is closest to the copy's centre.</summary>
        private void UpdateDrag()
        {
            Size size = _slots[_sourceIndex].Size;
            Point topLeft = Mouse.GetPosition(_panel) - _grabOffset;
            if (_verticalOnly) topLeft.X = _slots[_sourceIndex].X;

            Rect bounds = new(0, 0, _panel.ActualWidth, _panel.ActualHeight);
            if (_viewport != null)
                bounds.Intersect(new Rect(_viewport.TranslatePoint(new Point(), _panel), _viewport.RenderSize));
            if (!bounds.IsEmpty)
            {
                topLeft.X = Math.Max(bounds.Left, Math.Min(topLeft.X, bounds.Right - size.Width));
                topLeft.Y = Math.Max(bounds.Top, Math.Min(topLeft.Y, bounds.Bottom - size.Height));
            }

            _card.MoveTo(_panel.TranslatePoint(topLeft, _owner));

            Point center = topLeft + new Vector(size.Width / 2, size.Height / 2);
            int target = _sourceIndex;
            double bestDistance = double.MaxValue;
            for (int t = 0; t < _slots.Length; t++)
            {
                Rect landing = _slots[t];
                double distance = (new Point(landing.X + landing.Width / 2, landing.Y + landing.Height / 2) - center).LengthSquared;
                if (distance < bestDistance) { bestDistance = distance; target = t; }
            }

            if (target != _targetIndex)
            {
                _targetIndex = target;
                LayOutDrag();
            }
        }

        /// <summary>The index of item k once the dragged item has moved to the target index.</summary>
        private int IndexAfterMove(int k)
        {
            if (k == _sourceIndex) return _targetIndex;
            if (_sourceIndex < _targetIndex && k > _sourceIndex && k <= _targetIndex) return k - 1;
            if (_targetIndex < _sourceIndex && k >= _targetIndex && k < _sourceIndex) return k + 1;
            return k;
        }

        /// <summary>Slides every item to the slot it takes once the dragged item lands on the target
        /// index; the source container (the placeholder) slides straight to the target slot.</summary>
        private void LayOutDrag()
        {
            for (int k = 0; k < _containers.Length; k++)
                SlideTo(k, IndexAfterMove(k));
        }

        private void SlideTo(int k, int slot)
        {
            Vector offset = _slots[slot].TopLeft - _slots[k].TopLeft;
            if (offset == _shiftTargets[k]) return;
            _shiftTargets[k] = offset;
            _shifts[k].BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset.X, SlideDuration) { EasingFunction = Ease });
            _shifts[k].BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset.Y, SlideDuration) { EasingFunction = Ease });
        }

        private void OnAutoScrollTick(object sender, EventArgs e)
        {
            if (_state != State.Dragging || _scrollViewer == null || _viewport == null) return;

            double y = Mouse.GetPosition(_viewport).Y;
            double height = _viewport.ActualHeight;
            double step = 0;
            if (y < AutoScrollZone)
                step = -AutoScrollMaxStep * Math.Min(1, (AutoScrollZone - y) / AutoScrollZone);
            else if (y > height - AutoScrollZone)
                step = AutoScrollMaxStep * Math.Min(1, (y - (height - AutoScrollZone)) / AutoScrollZone);
            if (step == 0) return;

            double before = _scrollViewer.VerticalOffset;
            _scrollViewer.ScrollToVerticalOffset(before + step);
            _scrollViewer.UpdateLayout();
            if (_scrollViewer.VerticalOffset != before) UpdateDrag();
        }

        /// <summary>Ends the drag: the floating copy glides onto its slot (the target on drop, the
        /// original one on cancel, with the other items sliding back). Then everything is reset and,
        /// on drop, the move is reported.</summary>
        private void Settle(bool commit)
        {
            _state = State.Settling;
            StopTracking();

            if (!commit) _targetIndex = _sourceIndex;
            for (int k = 0; k < _containers.Length; k++)
                SlideTo(k, IndexAfterMove(k));

            Point slot = _panel.TranslatePoint(_slots[_targetIndex].TopLeft, _owner);
            _card.ScaleTo(1, SlideDuration, Ease);
            _card.GlideTo(slot, SlideDuration, Ease, () => Finish(commit));
        }

        private void Finish(bool commit)
        {
            if (_state != State.Settling) return;

            var item = _owner.ItemContainerGenerator.ItemFromContainer(_sourceContainer);
            int from = _sourceIndex, to = _targetIndex;
            Cleanup();

            var command = DragReorder.GetMoveCommand(_owner);
            var move = new DragReorderMove(item, from, to);
            if (commit && from != to && command?.CanExecute(move) == true)
                command.Execute(move);
        }

        /// <summary>The list left the visual tree mid-drag: drop everything without animating.</summary>
        private void Abort()
        {
            if (_state == State.Pending) ResetPending();
            if (_state is State.Dragging or State.Settling)
            {
                StopTracking();
                Cleanup();
            }
        }

        private void StopTracking()
        {
            _autoScrollTimer?.Stop();
            _autoScrollTimer = null;
            if (_window != null) _window.PreviewKeyDown -= OnWindowPreviewKeyDown;
            _window = null;
            Mouse.OverrideCursor = null;
            if (Mouse.Captured == _owner) _owner.ReleaseMouseCapture();
        }

        private void Cleanup()
        {
            _cardLayer.Remove(_card);
            _sourceContainer.ClearValue(DragReorder.IsDragSourceProperty);
            for (int i = 0; i < _containers.Length; i++)
                _containers[i].RenderTransform = _originalTransforms[i];
            RestoreItemHitTesting();

            _card = null;
            _cardLayer = null;
            _containers = null;
            _originalTransforms = null;
            _shifts = null;
            _shiftTargets = null;
            _slots = null;
            _panel = null;
            _viewport = null;
            _scrollViewer = null;
            _sourceContainer = null;
            _state = State.Idle;
        }

        // ── Helpers ──────────────────────────────────────────────

        private static IEasingFunction CreateEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();
            return ease;
        }

        /// <summary>Presses on controls inside an item (e.g. the availability toggle) keep their own
        /// behaviour and never start a drag.</summary>
        private static bool IsInsideInteractiveControl(DependencyObject node, DependencyObject container)
        {
            for (var current = node; current != null && current != container; current = GetParent(current))
                if (current is ButtonBase or TextBoxBase or Thumb or ModernWpf.Controls.ToggleSwitch) return true;
            return false;
        }

        private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            for (var current = GetParent(node); current != null; current = GetParent(current))
                if (current is T match) return match;
            return null;
        }

        private static DependencyObject GetParent(DependencyObject d) =>
            d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
    }

    /// <summary>The floating copy of the dragged item, drawn above everything else in the window's
    /// adorner layer and positioned in the coordinates of the reordered ItemsControl.</summary>
    internal sealed class DragReorderAdorner : Adorner
    {
        public const double LiftScale = 1.02;

        private readonly ContentPresenter _presenter;
        private readonly TranslateTransform _position = new();
        private readonly ScaleTransform _scale = new();

        /// <param name="size">Size of the drawn item; a NaN height lets the template decide.</param>
        public DragReorderAdorner(UIElement adornedElement, object item, DataTemplate template, Size size)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            var transform = new TransformGroup();
            transform.Children.Add(_scale);
            transform.Children.Add(_position);
            _presenter = new ContentPresenter
            {
                Content = item,
                ContentTemplate = template,
                Width = size.Width,
                Height = size.Height,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = transform
            };
            AddVisualChild(_presenter);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _presenter;

        protected override Size MeasureOverride(Size constraint)
        {
            _presenter.Measure(new Size(_presenter.Width, double.IsNaN(_presenter.Height) ? double.PositiveInfinity : _presenter.Height));
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _presenter.Arrange(new Rect(_presenter.DesiredSize));
            return finalSize;
        }

        /// <summary>Jumps to a position; only before any <see cref="GlideTo"/>, whose animation holds its value.</summary>
        public void MoveTo(Point topLeft)
        {
            _position.X = topLeft.X;
            _position.Y = topLeft.Y;
        }

        public void GlideTo(Point topLeft, Duration duration, IEasingFunction ease, Action completed = null)
        {
            var glideY = new DoubleAnimation(topLeft.Y, duration) { EasingFunction = ease };
            if (completed != null) glideY.Completed += (_, _) => completed();
            _position.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(topLeft.X, duration) { EasingFunction = ease });
            _position.BeginAnimation(TranslateTransform.YProperty, glideY);
        }

        public void ScaleTo(double scale, Duration duration, IEasingFunction ease)
        {
            var animation = new DoubleAnimation(scale, duration) { EasingFunction = ease };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }
}
