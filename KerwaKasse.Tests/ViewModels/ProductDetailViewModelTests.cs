using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Tests.ViewModels;

public class ProductDetailViewModelTests
{
    private static ProductDetailViewModel ExistingProduct() =>
        new(productId: 1, description: "Bratwurst", price: 3.5m,
            colorAsString: "#FF0000", available: true, usageCount: 0, isNew: false);

    private static ProductDetailViewModel NewProduct() =>
        new(productId: 0, description: string.Empty, price: 0m,
            colorAsString: "#5B9BD5", available: true, usageCount: 0, isNew: true);

    [Fact]
    public void NewProduct_NameStartsUnlocked_NoUnlockButton()
    {
        var vm = NewProduct();

        Assert.True(vm.NameUnlocked);
        Assert.False(vm.UnlockButtonVisible);
    }

    [Fact]
    public void ExistingProduct_NameStartsLocked_WithUnlockButton()
    {
        var vm = ExistingProduct();

        Assert.False(vm.NameUnlocked);
        Assert.True(vm.UnlockButtonVisible);
    }

    [Fact]
    public void VariablePrice_CountsAsChange_AndRenamesThePriceField()
    {
        var vm = ExistingProduct();
        Assert.Equal("PREIS", vm.PriceLabel);

        vm.VariablePrice = true;

        Assert.True(vm.IsDirty);
        Assert.Equal("VORSCHLAGSPREIS", vm.PriceLabel);
    }

    [Fact]
    public void MoreSettings_AreFoldedByDefault_AndOpenForAVariablePriceProduct()
    {
        Assert.False(ExistingProduct().ShowMoreSettings);
        Assert.False(NewProduct().ShowMoreSettings);

        var variable = new ProductDetailViewModel(productId: 2, description: "Makrele", price: 7m,
            colorAsString: "#00FF00", available: true, usageCount: 0, isNew: false, variablePrice: true);
        Assert.True(variable.ShowMoreSettings);

        // Switching the option off leaves the section open, so the switch doesn't jump away.
        variable.VariablePrice = false;
        Assert.True(variable.ShowMoreSettings);
    }

    [Fact]
    public void VariablePrice_NeedsASuggestionAboveZero()
    {
        var vm = NewProduct();
        vm.Name = "Makrele";
        Assert.False(vm.HasErrors); // zero is fine for a fixed price

        vm.VariablePrice = true;
        Assert.True(vm.HasErrors);
        Assert.NotEqual(string.Empty, vm["PriceText"]);

        vm.PriceText = "7,00";
        Assert.False(vm.HasErrors);
        Assert.Equal(string.Empty, vm["PriceText"]);
    }

    [Fact]
    public void ValidProduct_HasNoErrors()
    {
        var vm = ExistingProduct();

        Assert.False(vm.HasErrors);
        Assert.Equal(string.Empty, vm["PriceText"]);
        Assert.Equal(string.Empty, vm["Name"]);
    }

    [Fact]
    public void NegativePrice_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.PriceText = "-5";

        Assert.True(vm.HasErrors);
        Assert.Equal("Preis darf nicht negativ sein", vm["PriceText"]);
    }

    [Fact]
    public void NonNumericPrice_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.PriceText = "abc";

        Assert.True(vm.HasErrors);
        Assert.Equal("Kein gültiger Preis", vm["PriceText"]);
    }

    [Fact]
    public void EmptyName_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.Name = "   ";

        Assert.True(vm.HasErrors);
        Assert.Equal("Name darf nicht leer sein", vm["Name"]);
    }

    [Fact]
    public void NameExceedingMaxLength_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.Name = new string('x', ProductDetailViewModel.MaxNameLength + 1);

        Assert.True(vm.HasErrors);
        Assert.Equal($"Maximal {ProductDetailViewModel.MaxNameLength} Zeichen erlaubt", vm["Name"]);
    }

    [Fact]
    public void PriceWithMoreThanTwoDecimals_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.PriceText = "7,9999";

        Assert.True(vm.HasErrors);
        Assert.Equal("Höchstens zwei Nachkommastellen", vm["PriceText"]);
    }

    [Fact]
    public void PriceWithDotSeparator_IsParsedAsDecimal()
    {
        var vm = ExistingProduct();

        vm.PriceText = "8.50";

        Assert.False(vm.HasErrors);
        Assert.Equal(8.50m, vm.ParsedPrice);
    }
}
