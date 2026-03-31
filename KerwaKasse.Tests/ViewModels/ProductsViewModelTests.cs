using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class ProductsViewModelTests
{
    private readonly IProductService _productService;
    private readonly ISettingsService _settings;
    private readonly ProductsViewModel _sut;

    public ProductsViewModelTests()
    {
        _productService = Substitute.For<IProductService>();
        _settings = Substitute.For<ISettingsService>();

        _productService.GetAll().Returns(new List<Product>
        {
            new() { Id = 1, Description = "Bratwurst", Price = 3.50m, Available = true, SortOrder = 1 },
            new() { Id = 2, Description = "Bier", Price = 2.80m, Available = true, SortOrder = 2 },
            new() { Id = 3, Description = "Pommes", Price = 2.50m, Available = false, SortOrder = 3 }
        });

        _settings.Get("productsSidePanelWidth", 300.0).Returns(300.0);

        _sut = new ProductsViewModel(_productService, _settings);
    }

    [Fact]
    public void Constructor_LoadsAllProducts()
    {
        Assert.Equal(3, _sut.Products.Count);
        Assert.Equal("Bratwurst", _sut.Products[0].Description);
    }

    [Fact]
    public void Constructor_StartsInModeNone()
    {
        Assert.True(_sut.Mode_None);
        Assert.False(_sut.Mode_AddNew);
        Assert.False(_sut.Mode_Editing);
    }

    [Fact]
    public void AddNewProductCommand_SwitchesToAddNewMode()
    {
        _sut.AddNewProductCommand.Execute(null);

        Assert.True(_sut.Mode_AddNew);
        Assert.False(_sut.Mode_Editing);
        Assert.False(_sut.Mode_None);
        Assert.NotNull(_sut.NewProduct);
    }

    [Fact]
    public void SaveNewProductCommand_CallsProductServiceAdd()
    {
        _sut.AddNewProductCommand.Execute(null);
        _sut.NewProduct.Description = "Schnitzel";
        _sut.NewProduct.Price = 6.00m;

        _sut.SaveNewProductCommand.Execute(null);

        _productService.Received(1).Add(Arg.Is<Product>(p =>
            p.Description == "Schnitzel" && p.Price == 6.00m));
    }

    [Fact]
    public void DiscardNewProductCommand_ResetsToNoneMode()
    {
        _sut.AddNewProductCommand.Execute(null);

        _sut.DiscardNewProductCommand.Execute(null);

        Assert.True(_sut.Mode_None);
        Assert.False(_sut.Mode_AddNew);
        Assert.Null(_sut.NewProduct);
    }

    [Fact]
    public void SelectedProduct_SwitchesToEditingMode()
    {
        _sut.SelectedProduct = _sut.Products[0];

        Assert.True(_sut.Mode_Editing);
        Assert.False(_sut.Mode_AddNew);
        Assert.False(_sut.Mode_None);
        Assert.NotNull(_sut.EditingProduct);
    }

    [Fact]
    public void SelectedProduct_ClonesIntoEditingProduct()
    {
        _sut.SelectedProduct = _sut.Products[0];

        Assert.Equal(_sut.Products[0].ProductID, _sut.EditingProduct.ProductID);
        Assert.Equal(_sut.Products[0].Description, _sut.EditingProduct.Description);
        Assert.Equal(_sut.Products[0].Price, _sut.EditingProduct.Price);
    }

    [Fact]
    public void SaveEditingProductCommand_CallsUpdate()
    {
        _sut.SelectedProduct = _sut.Products[0];
        _sut.EditingProduct.Price = 4.00m;

        _sut.SaveEditingProductCommand.Execute(null);

        _productService.Received(1).Update(Arg.Is<Product>(p =>
            p.Id == 1 && p.Price == 4.00m));
    }

    [Fact]
    public void SaveEditingProductCommand_PersistsDescription()
    {
        _sut.SelectedProduct = _sut.Products[0];
        _sut.EditingProduct.Description = "Currywurst";

        _sut.SaveEditingProductCommand.Execute(null);

        _productService.Received(1).Update(Arg.Is<Product>(p =>
            p.Id == 1 && p.Description == "Currywurst"));
    }

    [Fact]
    public void SaveEditingProductCommand_PersistsAvailability()
    {
        _sut.SelectedProduct = _sut.Products[0];
        _sut.EditingProduct.Available = false;

        _sut.SaveEditingProductCommand.Execute(null);

        _productService.Received(1).Update(Arg.Is<Product>(p =>
            p.Id == 1 && p.Available == false));
    }

    [Fact]
    public void DiscardEditingProductCommand_ResetsToNoneMode()
    {
        _sut.SelectedProduct = _sut.Products[0];

        _sut.DiscardEditingProductCommand.Execute(null);

        Assert.True(_sut.Mode_None);
        Assert.False(_sut.Mode_Editing);
        Assert.Null(_sut.SelectedProduct);
    }

    [Fact]
    public void Reload_RefreshesProductList()
    {
        _productService.GetAll().Returns(new List<Product>
        {
            new() { Id = 1, Description = "Bratwurst", Price = 4.00m, Available = true, SortOrder = 1 }
        });

        _sut.Reload();

        Assert.Single(_sut.Products);
        Assert.Equal(4.00m, _sut.Products[0].Price);
    }
}
