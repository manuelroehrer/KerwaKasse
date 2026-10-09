using System.Globalization;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class AnalysisViewModelTests
{
    private readonly IAnalyticsService _analytics;
    private readonly IProductService _products;
    private readonly ISavedAnalysisService _saved;
    private readonly IDialogService _dialog;

    public AnalysisViewModelTests()
    {
        _analytics = Substitute.For<IAnalyticsService>();
        _products = Substitute.For<IProductService>();
        _saved = Substitute.For<ISavedAnalysisService>();
        _dialog = Substitute.For<IDialogService>();

        _products.GetAll().Returns(new List<Product>
        {
            new() { Id = 1, Name = "Bratwurst", Price = 3.00m },
            new() { Id = 2, Name = "Bier", Price = 3.50m }
        });
        _saved.GetAll().Returns(new List<SavedAnalysis>());
        _analytics.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<bool>())
            .Returns(new List<SalesFigure>());
        _analytics.GetSalesOverTime(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<AnalysisTimeResolution>(), Arg.Any<bool>())
            .Returns(new List<TimeBucketSales>());
        _analytics.GetPricePeriods(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<ProductPricePeriod>());
        _analytics.GetFreeEntryGroups(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<bool>())
            .Returns(new List<FreeEntryGroup>());
    }

    private AnalysisViewModel CreateSut() => new(_analytics, _products, _saved, _dialog, NullLogger<AnalysisViewModel>.Instance);

    private void WithFigures(params SalesFigure[] figures) =>
        _analytics.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<bool>())
            .Returns(figures.ToList());

    [Fact]
    public void Constructor_DefaultsToTodayAndBreakdown()
    {
        var sut = CreateSut();

        Assert.True(sut.IsBreakdown);
        Assert.False(sut.IsTimeCourse);
        Assert.Equal("Heute", sut.QuickRanges.Single(q => q.IsSelected).Label);
        Assert.Equal(DateTime.Today, sut.DateFrom);
    }

    [Fact]
    public void Constructor_SelectsAllProductsByDefault()
    {
        var sut = CreateSut();

        // "Freie Eingabe" first, then the products.
        Assert.Equal(new[] { "Freie Eingabe", "Bratwurst", "Bier" }, sut.ProductFilter.Select(p => p.Name));
        Assert.True(sut.ProductFilter[0].IsFreeEntries);
        Assert.All(sut.ProductFilter, p => Assert.True(p.IsSelected));
        Assert.Equal("alle (3)", sut.ProductFilterSummary);
    }

    [Fact]
    public void Constructor_ResolutionsAreDayHourQuarter_WithHourSelected()
    {
        var sut = CreateSut();

        Assert.Equal(new[] { "Tag", "Stunde", "15 Min" }, sut.Resolutions.Select(r => r.Label));
        // "Stunde" stays the default even though it is now the middle segment.
        Assert.Equal("Stunde", sut.Resolutions.Single(r => r.IsSelected).Label);
    }

    [Fact]
    public void Constructor_LoadsBreakdownAndTotals()
    {
        WithFigures(
            new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m },
            new SalesFigure { ProductId = 2, ProductName = "Bier", TotalAmount = 5, TotalRevenue = 17.50m });

        var sut = CreateSut();

        Assert.Equal(2, sut.Breakdown.Count);
        Assert.Equal(15, sut.TotalAmount);
        Assert.Equal(47.50m, sut.TotalRevenue);
        Assert.True(sut.HasData);
    }

    [Fact]
    public void SortBreakdown_TogglesDirectionAndSortsByColumn()
    {
        WithFigures(
            new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m },
            new SalesFigure { ProductId = 2, ProductName = "Bier", TotalAmount = 5, TotalRevenue = 17.50m });
        var sut = CreateSut();

        // Default: amount descending.
        Assert.Equal("Bratwurst", sut.Breakdown[0].Name);
        Assert.Equal(" ▼", sut.AmountSortIndicator);

        // Second click on the same column flips the direction.
        sut.SortBreakdownCommand.Execute("Amount");
        Assert.Equal("Bier", sut.Breakdown[0].Name);
        Assert.Equal(" ▲", sut.AmountSortIndicator);

        // Switching to the name column starts alphabetically ascending; ▼ marks that default
        // direction, matching the ▼-on-first-click convention of the other columns.
        sut.SortBreakdownCommand.Execute("Name");
        Assert.Equal("Bier", sut.Breakdown[0].Name);
        Assert.Equal(" ▼", sut.NameSortIndicator);
        Assert.Equal(string.Empty, sut.AmountSortIndicator);

        // A second click on Name flips to Z→A, now shown with ▲.
        sut.SortBreakdownCommand.Execute("Name");
        Assert.Equal("Bratwurst", sut.Breakdown[0].Name);
        Assert.Equal(" ▲", sut.NameSortIndicator);

        // Numeric columns start descending.
        sut.SortBreakdownCommand.Execute("Revenue");
        Assert.Equal("Bratwurst", sut.Breakdown[0].Name);
        Assert.Equal(" ▼", sut.RevenueSortIndicator);
    }

    [Fact]
    public void Breakdown_CarriesFormattedPricePeriodsPerProduct()
    {
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m });
        _analytics.GetPricePeriods(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<ProductPricePeriod>
            {
                new() { ProductId = 1, UnitPrice = 3.00m, From = new DateTime(2022, 7, 24), To = new DateTime(2022, 7, 29) },
                new() { ProductId = 1, UnitPrice = 3.50m, From = new DateTime(2023, 7, 25), To = new DateTime(2023, 7, 25) }
            });

        var sut = CreateSut();

        var row = Assert.Single(sut.Breakdown);
        Assert.True(row.HasPriceDetails);
        Assert.Equal(2, row.PriceDetails.Count);
        Assert.Equal("Einzelpreise", row.PriceDetailsHeader);
        Assert.Equal("3,00 €", row.PriceDetails[0].Price);
        Assert.Equal("24.07.2022 – 29.07.2022", row.PriceDetails[0].Range);
        Assert.Equal("3,50 €", row.PriceDetails[1].Price);
        Assert.Equal("25.07.2023", row.PriceDetails[1].Range); // single-day segment shows one date
    }

    [Fact]
    public void PriceDetailsHeader_IsSingularForASinglePriceLine()
    {
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m });
        _analytics.GetPricePeriods(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<ProductPricePeriod>
            {
                new() { ProductId = 1, UnitPrice = 3.00m, From = new DateTime(2022, 7, 24), To = new DateTime(2025, 7, 29) }
            });

        var sut = CreateSut();

        Assert.Equal("Einzelpreis", Assert.Single(sut.Breakdown).PriceDetailsHeader);
    }

    [Fact]
    public void Breakdown_ShowsTheUnitPrice_WhenAllSalesShareOnePrice()
    {
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m, MinUnitPrice = 3.00m, MaxUnitPrice = 3.00m });

        var sut = CreateSut();

        var row = Assert.Single(sut.Breakdown);
        Assert.Equal(3.00m, row.UnitPrice);
        Assert.Equal(3.00m.ToString("C", CultureInfo.GetCultureInfo("de-DE")), row.UnitPriceText);
    }

    [Fact]
    public void Breakdown_ShowsAWordInsteadOfARange_WhenThePriceChanged()
    {
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 32.50m, MinUnitPrice = 3.00m, MaxUnitPrice = 3.50m });
        _analytics.GetPricePeriods(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<ProductPricePeriod>
            {
                new() { ProductId = 1, UnitPrice = 3.00m, From = new DateTime(2022, 7, 24), To = new DateTime(2022, 7, 29) },
                new() { ProductId = 1, UnitPrice = 3.50m, From = new DateTime(2023, 7, 25), To = new DateTime(2023, 7, 25) }
            });

        var sut = CreateSut();

        var row = Assert.Single(sut.Breakdown);
        Assert.Null(row.UnitPrice);
        // Underlined as a hint to the tooltip with the individual prices.
        Assert.True(row.IsUnitPriceVariable);
        Assert.Equal("var.", row.UnitPriceText);
    }

    [Fact]
    public void ClearProducts_EmptiesResults_ThenSelectAllRestoresThem()
    {
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m });
        var sut = CreateSut();

        sut.ClearProductsCommand.Execute(null);

        Assert.Empty(sut.Breakdown);
        Assert.Equal(0, sut.TotalAmount);
        Assert.False(sut.HasData);
        Assert.Equal("keine Produkte", sut.ProductFilterSummary);

        // Regression: selecting all again must restore the data (HasData was stuck true/false before).
        sut.SelectAllProductsCommand.Execute(null);

        Assert.True(sut.HasData);
        Assert.Single(sut.Breakdown);
    }

    [Fact]
    public void SelectQuickRange_Yesterday_FillsDateFields()
    {
        var sut = CreateSut();
        var yesterday = sut.QuickRanges.Single(q => (QuickRange)q.Value == QuickRange.Yesterday);

        sut.SelectQuickRangeCommand.Execute(yesterday);

        Assert.True(yesterday.IsSelected);
        Assert.Equal(DateTime.Today.AddDays(-1), sut.DateFrom);
    }

    [Fact]
    public void SetViewMode_TimeCourse_QueriesTimeSeries()
    {
        _analytics.GetSalesOverTime(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<AnalysisTimeResolution>(), Arg.Any<bool>())
            .Returns(new List<TimeBucketSales>
            {
                new() { BucketStart = DateTime.Today.AddHours(10), TotalAmount = 5 }
            });
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 5, TotalRevenue = 15.00m });
        var sut = CreateSut();

        var timeCourse = sut.ViewModes.Single(v => (string)v.Value == "TimeCourse");
        sut.SetViewModeCommand.Execute(timeCourse);

        Assert.True(sut.IsTimeCourse);
        Assert.False(sut.IsBreakdown);
        Assert.True(timeCourse.IsSelected); // drives the highlighted segment in the UI
        Assert.NotEmpty(sut.TimeCourseSeries);
    }

    [Fact]
    public void SaveCurrent_PersistsAnalysisFromCurrentState()
    {
        _dialog.ShowTextInputAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult("Getränke"));
        var sut = CreateSut();

        sut.SaveCurrentCommand.Execute(null);

        _saved.Received(1).Add(Arg.Is<SavedAnalysis>(a =>
            a.Name == "Getränke" && a.AllProducts && a.From.HasValue));
    }

    [Fact]
    public void ApplySaved_RestoresProductSelection_AndBecomesActive()
    {
        _saved.GetAll().Returns(new List<SavedAnalysis>
        {
            new() { Id = 7, Name = "Nur Bratwurst", AllProducts = false, ProductCount = 1,
                    From = DateTime.Today, To = DateTime.Today.AddHours(23).AddMinutes(59) }
        });
        _saved.GetProductIds(7).Returns(new List<int> { 1 });
        var sut = CreateSut();

        sut.ApplySavedCommand.Execute(7);

        Assert.True(sut.ProductFilter.Single(p => p.ProductId == 1).IsSelected);
        Assert.False(sut.ProductFilter.Single(p => p.ProductId == 2).IsSelected);
        // Active is detected at runtime: current selection now equals the stored analysis.
        Assert.True(sut.HasActiveAnalysis);
        Assert.Equal("Nur Bratwurst", sut.ActiveAnalysisText);
    }

    [Fact]
    public void ManualChange_ClearsActiveAnalysis()
    {
        _saved.GetAll().Returns(new List<SavedAnalysis>
        {
            new() { Id = 5, Name = "Sonntag", AllProducts = true,
                    From = DateTime.Today.AddDays(-2), To = DateTime.Today.AddDays(-2).AddHours(23) }
        });
        var sut = CreateSut();
        sut.ApplySavedCommand.Execute(5);
        Assert.True(sut.HasActiveAnalysis);

        sut.SelectQuickRangeCommand.Execute(sut.QuickRanges[0]); // "Heute"

        Assert.False(sut.HasActiveAnalysis);
    }

    // Bratwurst with special prices, a mackerel with a variable price and free entries without product.
    private void WithAllKinds()
    {
        WithFigures(
            new SalesFigure { Kind = SalesFigureKind.Product, ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m, MinUnitPrice = 3.00m, MaxUnitPrice = 3.00m },
            new SalesFigure { Kind = SalesFigureKind.SpecialPrice, ProductId = 1, ProductName = "Bratwurst", TotalAmount = 3, TotalRevenue = 5.00m, MinUnitPrice = 1.00m, MaxUnitPrice = 2.00m },
            new SalesFigure { Kind = SalesFigureKind.FreeEntries, ProductName = "", TotalAmount = 4, TotalRevenue = 14.00m, MinUnitPrice = 2.00m, MaxUnitPrice = 4.00m },
            new SalesFigure { Kind = SalesFigureKind.Product, ProductId = 2, ProductName = "Bier", TotalAmount = 6, TotalRevenue = 21.00m, MinUnitPrice = 3.50m, MaxUnitPrice = 3.50m });
        _analytics.GetFreeEntryGroups(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<bool>())
            .Returns(new List<FreeEntryGroup>
            {
                new() { ProductId = 1, Note = "Helfer", UnitPrice = 1.00m, TotalAmount = 1, TotalRevenue = 1.00m },
                new() { ProductId = 1, Note = null, UnitPrice = 2.00m, TotalAmount = 2, TotalRevenue = 4.00m },
                new() { ProductId = null, Note = "Kuchen", UnitPrice = 4.00m, TotalAmount = 3, TotalRevenue = 12.00m },
                new() { ProductId = null, Note = null, UnitPrice = 2.00m, TotalAmount = 1, TotalRevenue = 2.00m }
            });
    }

    [Fact]
    public void Breakdown_HasRowsForSpecialPricesAndFreeEntries()
    {
        WithAllKinds();

        var sut = CreateSut();

        var special = sut.Breakdown.Single(r => r.Kind == SalesFigureKind.SpecialPrice);
        Assert.Equal("Bratwurst (Sonderpreis)", special.Name);
        // Same colour as the product.
        Assert.Equal(sut.Breakdown.Single(r => r.Name == "Bratwurst").ColorBrush.ToString(), special.ColorBrush.ToString());
        Assert.Equal(new[] { "Helfer", "Ohne Angabe" }, special.Details.Select(d => d.Name));
        Assert.True(special.Details[1].IsWithoutDescription);

        var free = sut.Breakdown.Single(r => r.Kind == SalesFigureKind.FreeEntries);
        Assert.Equal("Freie Eingabe", free.Name);
        Assert.Equal(new[] { "Kuchen", "Ohne Angabe" }, free.Details.Select(d => d.Name));
        Assert.Equal(12.00m, free.Details[0].Revenue);

        // Everything counts towards the totals.
        Assert.Equal(23, sut.TotalAmount);
        Assert.Equal(70.00m, sut.TotalRevenue);
        Assert.Equal(4, sut.PieSeries.Length);
    }

    [Fact]
    public void SortByName_KeepsSpecialPricesBelowTheirProductAndFreeEntriesLast()
    {
        WithAllKinds();
        var sut = CreateSut();

        sut.SortBreakdownCommand.Execute("Name");
        Assert.Equal(new[] { "Bier", "Bratwurst", "Bratwurst (Sonderpreis)", "Freie Eingabe" }, sut.Breakdown.Select(r => r.Name));

        sut.SortBreakdownCommand.Execute("Name");
        Assert.Equal(new[] { "Bratwurst", "Bratwurst (Sonderpreis)", "Bier", "Freie Eingabe" }, sut.Breakdown.Select(r => r.Name));

        // By amount every row stands on its own.
        sut.SortBreakdownCommand.Execute("Amount");
        Assert.Equal(new[] { "Bratwurst", "Bier", "Freie Eingabe", "Bratwurst (Sonderpreis)" }, sut.Breakdown.Select(r => r.Name));
    }

    [Fact]
    public void ToggleRow_ExpandsDetails_AndKeepsThemOpenWhenResorted()
    {
        WithAllKinds();
        var sut = CreateSut();
        var free = sut.Breakdown.Single(r => r.Kind == SalesFigureKind.FreeEntries);

        sut.ToggleRowCommand.Execute(free);
        Assert.True(free.IsExpanded);

        sut.SortBreakdownCommand.Execute("Name");
        Assert.True(sut.Breakdown.Single(r => r.Kind == SalesFigureKind.FreeEntries).IsExpanded);

        // A product row has no details to show.
        var product = sut.Breakdown.Single(r => r.Name == "Bier");
        sut.ToggleRowCommand.Execute(product);
        Assert.False(product.IsExpanded);
    }

    [Fact]
    public void Breakdown_VariablePrice_TooltipShowsSuggestionAndEnteredPrices()
    {
        WithFigures(new SalesFigure
        {
            Kind = SalesFigureKind.Product, ProductId = 1, ProductName = "Makrele", TotalAmount = 3, TotalRevenue = 23.00m,
            MinUnitPrice = 6.00m, MaxUnitPrice = 8.50m,
            VariablePriceAmount = 3, VariablePriceRevenue = 23.00m, VariablePriceMin = 6.00m, VariablePriceMax = 8.50m
        });
        _analytics.GetPricePeriods(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<ProductPricePeriod>
            {
                new() { ProductId = 1, UnitPrice = 7.00m, IsVariablePrice = true, From = new DateTime(2026, 8, 1), To = new DateTime(2026, 8, 2) }
            });

        var sut = CreateSut();

        var row = Assert.Single(sut.Breakdown);
        Assert.Equal("ca. 7,00 €", row.PriceDetails[0].Price);
        Assert.Equal("6,00 – 8,50 € · Ø 7,67 €", row.EnteredPrices);
        Assert.True(row.IsUnitPriceVariable);
    }

    [Fact]
    public void FreeEntriesUnticked_QueriesAllProductsWithoutThem()
    {
        var sut = CreateSut();
        _analytics.ClearReceivedCalls();

        sut.ProductFilter.Single(p => p.IsFreeEntries).IsSelected = false;

        _analytics.Received().GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(), null, false);
        Assert.Equal("2 von 3", sut.ProductFilterSummary);
    }

    [Fact]
    public void OnlyFreeEntriesTicked_QueriesNoProductButThem()
    {
        var sut = CreateSut();
        sut.ClearProductsCommand.Execute(null);
        _analytics.ClearReceivedCalls();

        sut.ProductFilter.Single(p => p.IsFreeEntries).IsSelected = true;

        _analytics.Received().GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Is<IReadOnlyCollection<int>>(ids => ids != null && ids.Count == 0), true);
    }

    [Fact]
    public void SaveCurrent_WithoutFreeEntries_StoresTheSelection()
    {
        _dialog.ShowTextInputAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult("Nur Essen"));
        var sut = CreateSut();
        sut.ProductFilter.Single(p => p.IsFreeEntries).IsSelected = false;

        sut.SaveCurrentCommand.Execute(null);

        _saved.Received(1).Add(Arg.Is<SavedAnalysis>(a =>
            !a.AllProducts && !a.IncludeFreeEntries && a.ProductIds.OrderBy(id => id).SequenceEqual(new[] { 1, 2 })));
    }

    [Fact]
    public void ApplySaved_WithoutFreeEntries_UnticksThem_AndOnlyMatchesThatWay()
    {
        _saved.GetAll().Returns(new List<SavedAnalysis>
        {
            new() { Id = 8, Name = "Nur Bratwurst", AllProducts = false, IncludeFreeEntries = false, ProductCount = 1,
                    From = DateTime.Today, To = DateTime.Today.AddHours(23).AddMinutes(59) }
        });
        _saved.GetProductIds(8).Returns(new List<int> { 1 });
        var sut = CreateSut();

        sut.ApplySavedCommand.Execute(8);

        Assert.False(sut.ProductFilter.Single(p => p.IsFreeEntries).IsSelected);
        Assert.True(sut.HasActiveAnalysis);

        // Ticking the free entries is another selection than the saved one.
        sut.ProductFilter.Single(p => p.IsFreeEntries).IsSelected = true;
        Assert.False(sut.HasActiveAnalysis);
    }

    [Fact]
    public void ExportPdf_OffersTheDetailsPage_WhenThereAreSpecialPricesOrFreeEntries()
    {
        WithAllKinds();
        var sut = CreateSut();

        sut.ExportPdfCommand.Execute(null);

        _dialog.Received(1).ShowPdfExportAsync("Benutzerdefiniert", true);
    }

    [Fact]
    public void ExportPdf_WithoutSpecialPricesOrFreeEntries_DoesNotOfferTheDetailsPage()
    {
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 30.00m });
        var sut = CreateSut();

        sut.ExportPdfCommand.Execute(null);

        _dialog.Received(1).ShowPdfExportAsync("Benutzerdefiniert", false);
    }
}
