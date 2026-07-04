using System.Windows;

namespace KerwaKasse.Helper
{
    /// <summary>A Freezable that carries a bindable <see cref="Data"/> value. Placed in an element's
    /// resources with <c>Data="{Binding}"</c>, it captures that element's DataContext and lets objects
    /// that are not part of the visual tree (e.g. a <see cref="System.Windows.Media.ScaleTransform"/>
    /// inside a LayoutTransform) reach it via <c>Source={StaticResource proxy}</c> — where a
    /// RelativeSource FindAncestor binding cannot resolve and logs a spurious trace error.</summary>
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }
    }
}
