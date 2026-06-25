namespace KerwaKasse.MVVM.Model
{
    /// <summary>One option in a segmented control (time quick-pick or chart resolution).
    /// <see cref="IsSelected"/> drives the highlighted state; <see cref="Value"/> carries the enum.</summary>
    public class AnalysisSegmentItem : PropertyChangedBase
    {
        public string Label { get; }
        public object Value { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public AnalysisSegmentItem(string label, object value, bool isSelected = false)
        {
            Label = label;
            Value = value;
            _isSelected = isSelected;
        }
    }
}
