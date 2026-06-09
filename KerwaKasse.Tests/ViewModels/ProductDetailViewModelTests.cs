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
    public void ValidProduct_HasNoErrors()
    {
        var vm = ExistingProduct();

        Assert.False(vm.HasErrors);
        Assert.Equal(string.Empty, vm["PriceText"]);
        Assert.Equal(string.Empty, vm["Description"]);
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
    public void EmptyDescription_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.Description = "   ";

        Assert.True(vm.HasErrors);
        Assert.Equal("Bezeichnung darf nicht leer sein", vm["Description"]);
    }

    [Fact]
    public void DescriptionExceedingMaxLength_IsInvalid()
    {
        var vm = ExistingProduct();

        vm.Description = new string('x', ProductDetailViewModel.MaxDescriptionLength + 1);

        Assert.True(vm.HasErrors);
        Assert.Equal($"Maximal {ProductDetailViewModel.MaxDescriptionLength} Zeichen erlaubt", vm["Description"]);
    }
}
