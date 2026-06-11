using System.Collections.Generic;
using System.Linq;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Tests.ViewModels;

public class MenuManagementViewModelTests
{
    /// <summary>In-memory IMenuService so the view model's batched edits can be verified end to end.</summary>
    private sealed class FakeMenuService : IMenuService
    {
        private readonly Dictionary<int, string> _menus = new();
        private readonly Dictionary<int, HashSet<int>> _products = new();
        private int _nextId = 1;

        public List<Menu> GetAll() =>
            _menus.OrderBy(kvp => kvp.Key)
                  .Select(kvp => new Menu { Id = kvp.Key, Name = kvp.Value })
                  .ToList();

        public List<int> GetProductIds(int menuId) =>
            _products.TryGetValue(menuId, out var ids) ? ids.ToList() : new List<int>();

        public int Add(string name) { int id = _nextId++; _menus[id] = name; return id; }
        public void Rename(int menuId, string newName) => _menus[menuId] = newName;
        public void Delete(int menuId) { _menus.Remove(menuId); _products.Remove(menuId); }
        public void SetProducts(int menuId, IEnumerable<int> productIds) => _products[menuId] = productIds.ToHashSet();
        public void ApplyMenu(int menuId) { }
    }

    private static MenuManagementViewModel CreateSut(out FakeMenuService service)
    {
        service = new FakeMenuService();
        var products = new List<(int Id, string Name)> { (1, "Bratwurst"), (2, "Pommes"), (3, "Brezel") };
        return new MenuManagementViewModel(service, products);
    }

    [Fact]
    public void AddMenu_UsesSequentialDefaultNames()
    {
        var sut = CreateSut(out var service);

        sut.AddMenuCommand.Execute(null);
        sut.AddMenuCommand.Execute(null);

        var names = service.GetAll().Select(m => m.Name).ToList();
        Assert.Contains("Neue Speisekarte", names);
        Assert.Contains("Neue Speisekarte 2", names);
    }

    [Fact]
    public void AddMenu_SelectsTheNewMenu()
    {
        var sut = CreateSut(out _);

        sut.AddMenuCommand.Execute(null);

        Assert.NotNull(sut.SelectedMenu);
        Assert.Equal("Neue Speisekarte", sut.SelectedMenu!.Name);
        Assert.True(sut.HasMenuSelected);
    }

    [Fact]
    public void Rename_OnSave_PersistsAndClearsDirty()
    {
        var sut = CreateSut(out var service);
        sut.AddMenuCommand.Execute(null);

        sut.MenuName = "Frühstück";
        Assert.True(sut.HasUnsavedChanges);

        sut.SaveCommand.Execute(null);

        Assert.Equal("Frühstück", service.GetAll().Single().Name);
        Assert.False(sut.HasUnsavedChanges);
    }

    [Fact]
    public void SaveProducts_PersistsAssignmentAndUpdatesCount()
    {
        var sut = CreateSut(out var service);
        sut.AddMenuCommand.Execute(null);
        int menuId = sut.SelectedMenu!.Id;

        sut.AllMenuProducts.First(p => p.ProductId == 1).IsIncluded = true;
        sut.AllMenuProducts.First(p => p.ProductId == 3).IsIncluded = true;
        Assert.True(sut.HasUnsavedChanges);

        sut.SaveCommand.Execute(null);

        Assert.Equal(new[] { 1, 3 }, service.GetProductIds(menuId).OrderBy(x => x));
        Assert.Equal(2, sut.SelectedMenu!.ProductCount);
        Assert.False(sut.HasUnsavedChanges);
    }

    [Fact]
    public void Discard_RevertsUnsavedAssignment()
    {
        var sut = CreateSut(out _);
        sut.AddMenuCommand.Execute(null);

        sut.AllMenuProducts.First(p => p.ProductId == 1).IsIncluded = true;
        Assert.True(sut.HasUnsavedChanges);

        sut.DiscardCommand.Execute(null);

        Assert.False(sut.HasUnsavedChanges);
        Assert.False(sut.AllMenuProducts.First(p => p.ProductId == 1).IsIncluded);
    }

    [Fact]
    public void Name_Validation_RejectsEmptyAndTooLong()
    {
        var sut = CreateSut(out _);
        sut.AddMenuCommand.Execute(null);

        sut.MenuName = "";
        Assert.True(sut.HasNameError);

        sut.MenuName = new string('x', MenuManagementViewModel.MaxNameLength + 1);
        Assert.True(sut.HasNameError);

        sut.MenuName = "Frühstück";
        Assert.False(sut.HasNameError);
    }

    [Fact]
    public void Name_Validation_RejectsDuplicate()
    {
        var sut = CreateSut(out _);
        sut.AddMenuCommand.Execute(null);
        sut.MenuName = "Frühstück";
        sut.SaveCommand.Execute(null);

        sut.AddMenuCommand.Execute(null);   // second menu, gets a default name
        sut.MenuName = "Frühstück";          // collides with the first menu

        Assert.True(sut.HasNameError);
    }

    [Fact]
    public void DeleteMenu_RemovesItAndClearsSelection()
    {
        var sut = CreateSut(out var service);
        sut.AddMenuCommand.Execute(null);
        Assert.Single(service.GetAll());

        sut.DeleteMenuCommand.Execute(null);

        Assert.Empty(service.GetAll());
        Assert.Null(sut.SelectedMenu);
    }
}
