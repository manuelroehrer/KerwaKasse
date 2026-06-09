using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class ProductsViewModelTests
{
    private readonly IProductService _productService;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogService;
    private readonly ProductsViewModel _sut;

    public ProductsViewModelTests()
    {
        _productService = Substitute.For<IProductService>();
        _settings = Substitute.For<ISettingsService>();
        _dialogService = Substitute.For<IDialogService>();

        // Fresh list per call so the view model can reload without sharing state.
        _productService.GetAll().Returns(_ => new List<Product>
        {
            new() { Id = 1, Name = "Bratwurst", Price = 3.50m, Available = true, Color = "#FF0000", SortOrder = 1 },
            new() { Id = 2, Name = "Bier", Price = 2.80m, Available = false, Color = "#00FF00", SortOrder = 2 }
        });

        _sut = new ProductsViewModel(_productService, _settings, _dialogService);
    }

    [Fact]
    public void Constructor_LoadsProducts()
    {
        Assert.Equal(2, _sut.Products.Count);
        Assert.Equal("Bratwurst", _sut.Products[0].Name);
    }

    [Fact]
    public void Initially_PlaceholderVisible_NoDetail()
    {
        Assert.True(_sut.IsPlaceholderVisible);
        Assert.False(_sut.IsDetailPanelVisible);
        Assert.Null(_sut.DetailPanel);
    }

    [Fact]
    public void SelectingProduct_OpensDetailPanel()
    {
        _sut.SelectedProduct = _sut.Products[0];

        Assert.True(_sut.IsDetailPanelVisible);
        Assert.False(_sut.IsAddingNew);
        Assert.NotNull(_sut.DetailPanel);
        Assert.Equal("Bratwurst", _sut.DetailPanel!.Name);
    }

    [Fact]
    public void AddProduct_OpensEmptyNewDetailPanel()
    {
        _sut.AddProductCommand.Execute(null);

        Assert.True(_sut.IsAddingNew);
        Assert.NotNull(_sut.DetailPanel);
        Assert.True(_sut.DetailPanel!.IsNew);
        Assert.Null(_sut.SelectedProduct);
    }

    [Fact]
    public void SaveNewProduct_CallsAdd_AndReturnsToPlaceholder()
    {
        _sut.AddProductCommand.Execute(null);
        _sut.DetailPanel!.Name = "Pommes";
        _sut.DetailPanel.PriceText = "2,50";

        _sut.SaveDetailCommand.Execute(null);

        _productService.Received(1).Add(Arg.Is<Product>(p => p.Name == "Pommes" && p.Price == 2.50m));
        Assert.True(_sut.IsPlaceholderVisible);
        Assert.Null(_sut.DetailPanel);
    }

    [Fact]
    public void SaveExistingProduct_CallsUpdate()
    {
        _sut.SelectedProduct = _sut.Products[0];
        _sut.DetailPanel!.Name = "Bratwurst XL";

        _sut.SaveDetailCommand.Execute(null);

        _productService.Received(1).Update(Arg.Is<Product>(p => p.Id == 1 && p.Name == "Bratwurst XL"));
    }

    [Fact]
    public void DiscardDetail_ReturnsToPlaceholder()
    {
        _sut.SelectedProduct = _sut.Products[0];

        _sut.DiscardDetailCommand.Execute(null);

        Assert.True(_sut.IsPlaceholderVisible);
        Assert.Null(_sut.DetailPanel);
        Assert.Null(_sut.SelectedProduct);
    }

    [Fact]
    public void SaveDetail_CanExecute_RequiresValidInput()
    {
        _sut.AddProductCommand.Execute(null); // new product, empty name -> invalid
        Assert.False(_sut.SaveDetailCommand.CanExecute(null));

        _sut.DetailPanel!.Name = "Pommes";
        Assert.True(_sut.SaveDetailCommand.CanExecute(null));
    }

    [Fact]
    public void TogglingAvailabilityInList_PersistsImmediately()
    {
        _sut.Products[0].Available = false;

        _productService.Received(1).UpdateAvailability(1, false);
    }

    [Fact]
    public void MoveSelectedDown_ReordersAndPersists()
    {
        _sut.SelectedProduct = _sut.Products[0]; // Bratwurst at index 0

        _sut.MoveSelectedDownCommand.Execute(null);

        Assert.Equal("Bier", _sut.Products[0].Name);
        Assert.Equal("Bratwurst", _sut.Products[1].Name);
        _productService.Received(1).UpdateSortOrder(Arg.Any<IEnumerable<Product>>());
    }

    [Fact]
    public void CurrentPositionText_ReflectsSelection()
    {
        _sut.SelectedProduct = _sut.Products[0];
        Assert.Equal("1 / 2", _sut.CurrentPositionText);

        _sut.SelectedProduct = _sut.Products[1];
        Assert.Equal("2 / 2", _sut.CurrentPositionText);
    }
}
