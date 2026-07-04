using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class SavedAnalysisManagementViewModelTests
{
    private readonly ISavedAnalysisService _service;
    private readonly IProductService _products;

    public SavedAnalysisManagementViewModelTests()
    {
        _service = Substitute.For<ISavedAnalysisService>();
        _products = Substitute.For<IProductService>();

        _products.GetAll().Returns(new List<Product>
        {
            new() { Id = 1, Name = "Bratwurst", Price = 3.00m },
            new() { Id = 2, Name = "Bier", Price = 3.50m }
        });
        _service.GetAll().Returns(new List<SavedAnalysis>
        {
            new() { Id = 1, Name = "Alpha", AllProducts = true,
                    From = DateTime.Today, To = DateTime.Today.AddHours(23) },
            new() { Id = 2, Name = "Beta", AllProducts = true,
                    From = DateTime.Today, To = DateTime.Today.AddHours(23) }
        });
    }

    private SavedAnalysisManagementViewModel CreateSut() => new(_service, _products);

    [Fact]
    public void Constructor_LoadsItems()
    {
        var sut = CreateSut();
        Assert.Equal(2, sut.Items.Count);
        Assert.True(sut.HasItems);
    }

    [Fact]
    public void Save_PersistsEditedAnalysis()
    {
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 1);

        sut.EditName = "Alpha neu";
        sut.SaveCommand.Execute(null);

        _service.Received(1).Update(Arg.Is<SavedAnalysis>(a => a.Id == 1 && a.Name == "Alpha neu"));
    }

    [Fact]
    public void CommitPositionEdit_MovesSelectedAnalysisToTypedPosition()
    {
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Name == "Beta"); // position 2

        sut.BeginPositionEditCommand.Execute(null);
        Assert.Equal("2", sut.PositionInput);

        sut.PositionInput = "1";
        sut.CommitPositionEdit();

        Assert.False(sut.IsEditingPosition);
        Assert.Equal("Beta", sut.Items[0].Name);
        Assert.Equal("1 / 2", sut.SelectedPositionText);
        _service.Received(1).UpdateSortOrder(Arg.Any<IEnumerable<SavedAnalysis>>());
    }

    [Fact]
    public void Delete_RemovesSelected()
    {
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 2);

        sut.DeleteCommand.Execute(null);

        _service.Received(1).Delete(2);
    }

    [Fact]
    public void Duplicate_AddsACopy()
    {
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 1);

        sut.DuplicateCommand.Execute(null);

        _service.Received(1).Add(Arg.Is<SavedAnalysis>(a => a.Name.Contains("Kopie")));
    }

    [Fact]
    public void AddNew_CreatesAnalysis()
    {
        var sut = CreateSut();

        sut.AddCommand.Execute(null);

        _service.Received(1).Add(Arg.Is<SavedAnalysis>(a => a.AllProducts && a.From.HasValue));
    }

    [Fact]
    public void EmptyName_IsRejected()
    {
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 1);

        sut.EditName = "   ";

        Assert.True(sut.HasNameError);
    }

    [Fact]
    public void Save_Subset_UpdatesSummaryCountImmediately()
    {
        // Regression: after saving a subset analysis, the list summary showed "0 Produkte" until reopened.
        _service.GetAll().Returns(new List<SavedAnalysis>
        {
            new() { Id = 3, Name = "Sub", AllProducts = false, ProductCount = 1,
                    From = DateTime.Today, To = DateTime.Today.AddHours(23) }
        });
        _service.GetProductIds(3).Returns(new List<int> { 1 });
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 3);

        sut.EditName = "Sub neu";
        sut.SaveCommand.Execute(null);

        Assert.Contains("1 Produkte", sut.Selected.Summary);
        Assert.DoesNotContain("0 Produkte", sut.Selected.Summary);
    }

    [Fact]
    public void EditingProducts_MarksDirty()
    {
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 1);

        sut.EditProducts.First().IsSelected = false;

        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void RevertingAChange_ClearsDirtyAgain()
    {
        // Regression: once dirty, editing back to the saved state must clear "changed" again.
        var sut = CreateSut();
        sut.Selected = sut.Items.First(i => i.Id == 1);
        var product = sut.EditProducts.First();

        product.IsSelected = false;
        Assert.True(sut.IsDirty);

        product.IsSelected = true; // back to the original selection
        Assert.False(sut.IsDirty);
    }
}
