namespace KerwaKasse.MVVM.Model
{
    /// <summary>A saved analysis as shown in the "Auswertung" flyout list: name + one-line summary,
    /// plus a runtime <see cref="IsActive"/> flag so the currently matching one can be highlighted.</summary>
    public class SavedAnalysisListItem : PropertyChangedBase
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }
    }
}
