using KerwaKasse.Helper;

namespace KerwaKasse.Tests.Helpers;

public class RelayCommandTests
{
    [Fact]
    public void Execute_InvokesAction()
    {
        object? receivedParam = null;
        var cmd = new RelayCommand(p => receivedParam = p);

        cmd.Execute("test");

        Assert.Equal("test", receivedParam);
    }

    [Fact]
    public void CanExecute_WithoutPredicate_ReturnsTrue()
    {
        var cmd = new RelayCommand(_ => { });

        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public void CanExecute_WithPredicate_ReturnsFalse()
    {
        var cmd = new RelayCommand(_ => { }, _ => false);

        Assert.False(cmd.CanExecute(null));
    }

    [Fact]
    public void CanExecute_WithPredicate_ReturnsTrue()
    {
        var cmd = new RelayCommand(_ => { }, p => (string)p == "yes");

        Assert.True(cmd.CanExecute("yes"));
        Assert.False(cmd.CanExecute("no"));
    }
}
