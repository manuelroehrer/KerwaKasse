using System.Windows.Data;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class ProductsViewModelTests
{
    private readonly IProductService _productService;
    private readonly IMenuService _menuService;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogService;
    private readonly ProductsViewModel _sut;

    public ProductsViewModelTests()
    {
        _productService = Substitute.For<IProductService>();
        _menuService = Substitute.For<IMenuService>();
        _settings = Substitute.For<ISettingsService>();
        _dialogService = Substitute.For<IDialogService>();

        // Fresh list per call so the view model can reload without sharing state.
        _productService.GetAll().Returns(_ => new List<Product>
        {
            new() { Id = 1, Name = "Bratwurst", Price = 3.50m, Available = true, Color = "#FF0000", SortOrder = 1 },
            new() { Id = 2, Name = "Bier", Price = 2.80m, Available = false, Color = "#00FF00", SortOrder = 2 }
        });
        _menuService.GetAll().Returns(new List<Menu>());

        _sut = new ProductsViewModel(_productService, _menuService, _settings, _dialogService);
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
    public void CommitPositionEdit_MovesSelectedProductToTypedPosition()
    {
        _sut.SelectedProduct = _sut.Products[1]; // "Bier", position 2

        _sut.BeginPositionEditCommand.Execute(null);
        Assert.True(_sut.IsEditingPosition);
        Assert.Equal("2", _sut.PositionInput);

        _sut.PositionInput = "1";
        _sut.CommitPositionEdit();

        Assert.False(_sut.IsEditingPosition);
        Assert.Equal("Bier", _sut.Products[0].Name);
        Assert.Equal("1 / 2", _sut.CurrentPositionText);
        _productService.Received(1).UpdateSortOrder(Arg.Any<IEnumerable<Product>>());
    }

    [Fact]
    public void CommitPositionEdit_ClampsOutOfRange_AndIgnoresInvalidInput()
    {
        _sut.SelectedProduct = _sut.Products[0]; // "Bratwurst", position 1

        _sut.BeginPositionEditCommand.Execute(null);
        _sut.PositionInput = "99";
        _sut.CommitPositionEdit();
        Assert.Equal("Bratwurst", _sut.Products[^1].Name); // clamped to the last position

        _sut.BeginPositionEditCommand.Execute(null);
        _sut.PositionInput = "abc";
        _sut.CommitPositionEdit();
        Assert.Equal("Bratwurst", _sut.Products[^1].Name); // discarded, order unchanged
        _productService.Received(1).UpdateSortOrder(Arg.Any<IEnumerable<Product>>());
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
    public void TogglingAvailabilityInList_ForSelectedProduct_DoesNotMarkDetailDirty()
    {
        _sut.SelectedProduct = _sut.Products[0];
        Assert.False(_sut.DetailPanel!.HasUnsavedChanges);

        _sut.Products[0].Available = false; // in-list toggle writes through to the DB

        Assert.False(_sut.DetailPanel!.IsDirty);
        Assert.False(_sut.DetailPanel.HasUnsavedChanges);
        Assert.False(_sut.DetailPanel.Available);
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
    public void MoveProduct_ByDragAndDrop_ReordersPersistsAndKeepsSelection()
    {
        var bier = _sut.Products[1];
        _sut.SelectedProduct = _sut.Products[0]; // Bratwurst stays selected while Bier is dragged up
        var detail = _sut.DetailPanel;

        var move = new DragReorderMove(bier, 1, 0);
        Assert.True(_sut.MoveProductCommand.CanExecute(move));
        _sut.MoveProductCommand.Execute(move);

        Assert.Same(bier, _sut.Products[0]);
        Assert.Equal("Bratwurst", _sut.SelectedProduct!.Name);
        Assert.Same(detail, _sut.DetailPanel);
        Assert.Equal("2 / 2", _sut.CurrentPositionText);
        _productService.Received(1).UpdateSortOrder(Arg.Is<IEnumerable<Product>>(ps =>
            ps.Select(p => p.Id).SequenceEqual(new[] { 2, 1 })));
    }

    [Fact]
    public void MoveProduct_IsDisabledWhileSearching()
    {
        var move = new DragReorderMove(_sut.Products[1], 1, 0);

        _sut.SearchText = "Bi";

        Assert.False(_sut.CanDragReorder);
        Assert.False(_sut.MoveProductCommand.CanExecute(move));

        _sut.SearchText = "";
        Assert.True(_sut.CanDragReorder);
    }

    [Fact]
    public void CurrentPositionText_ReflectsSelection()
    {
        _sut.SelectedProduct = _sut.Products[0];
        Assert.Equal("1 / 2", _sut.CurrentPositionText);

        _sut.SelectedProduct = _sut.Products[1];
        Assert.Equal("2 / 2", _sut.CurrentPositionText);
    }

    // ── Speisekarten (menu) ──────────────────────────────────

    [Fact]
    public void ApplyMenuCommand_WhenConfirmed_AppliesMenu()
    {
        _dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        _sut.ApplyMenuCommand.Execute(new Menu { Id = 7, Name = "Freitag" });

        _menuService.Received(1).ApplyMenu(7);
    }

    [Fact]
    public void ApplyMenuCommand_WhenCancelled_DoesNotApply()
    {
        _dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        _sut.ApplyMenuCommand.Execute(new Menu { Id = 7, Name = "Freitag" });

        _menuService.DidNotReceive().ApplyMenu(Arg.Any<int>());
    }

    [Fact]
    public void DetectActiveMenu_WhenAvailabilityMatchesMenu_SelectsIt()
    {
        // Available products are {1} (Bratwurst); menu 10 contains exactly {1}.
        _menuService.GetAll().Returns(new List<Menu> { new() { Id = 10, Name = "Grill" } });
        _menuService.GetProductIds(10).Returns(new List<int> { 1 });

        var sut = new ProductsViewModel(_productService, _menuService, _settings, _dialogService);

        Assert.NotNull(sut.SelectedMenu);
        Assert.Equal(10, sut.SelectedMenu!.Id);
        Assert.True(sut.HasActiveMenu);
        Assert.Equal("Grill", sut.ActiveMenuText);
        Assert.True(sut.MenuOptions.Single().IsActive);
    }

    [Fact]
    public void DetectActiveMenu_WhenNoMenuMatches_SelectsNone()
    {
        _menuService.GetAll().Returns(new List<Menu> { new() { Id = 10, Name = "Grill" } });
        _menuService.GetProductIds(10).Returns(new List<int> { 1, 2 }); // available is only {1}

        var sut = new ProductsViewModel(_productService, _menuService, _settings, _dialogService);

        Assert.Null(sut.SelectedMenu);
        Assert.False(sut.HasActiveMenu);
    }

    [Fact]
    public void SearchText_FiltersTheProductList()
    {
        var view = CollectionViewSource.GetDefaultView(_sut.Products);

        _sut.SearchText = "Bratwurst";
        var visible = view.Cast<ProductListItemViewModel>().ToList();
        Assert.Single(visible);
        Assert.Equal("Bratwurst", visible[0].Name);

        _sut.SearchText = "";
        Assert.Equal(2, view.Cast<ProductListItemViewModel>().Count());
    }
}
