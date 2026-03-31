namespace KerwaKasse.Tests.Helpers;

public class PropertyChangedBaseTests
{
    private class TestModel : PropertyChangedBase
    {
        private string? _name;
        public string? Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
    }

    [Fact]
    public void OnPropertyChanged_RaisesEvent()
    {
        var model = new TestModel();
        string? changedProp = null;
        model.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        model.Name = "Test";

        Assert.Equal("Name", changedProp);
    }

    [Fact]
    public void OnPropertyChanged_WithNoSubscribers_DoesNotThrow()
    {
        var model = new TestModel();
        model.Name = "Test"; // Should not throw
    }

    [Fact]
    public void OnPropertyChanged_FiresForEachChange()
    {
        var model = new TestModel();
        var firedCount = 0;
        model.PropertyChanged += (_, _) => firedCount++;

        model.Name = "A";
        model.Name = "B";

        Assert.Equal(2, firedCount);
    }
}
