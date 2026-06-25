using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
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
        _analytics.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<SalesFigure>());
        _analytics.GetSalesOverTime(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<AnalysisTimeResolution>())
            .Returns(new List<TimeBucketSales>());
    }

    private AnalysisViewModel CreateSut() => new(_analytics, _products, _saved, _dialog);

    private void WithFigures(params SalesFigure[] figures) =>
        _analytics.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(figures.ToList());

    [Fact]
    public void Constructor_DefaultsToTodayAndBreakdown()
    {
        var sut = CreateSut();

        Assert.True(sut.IsBreakdown);
        Assert.False(sut.IsCourse);
        Assert.Equal("Heute", sut.QuickRanges.Single(q => q.IsSelected).Label);
        Assert.Equal(DateTime.Today, sut.DateFrom);
    }

    [Fact]
    public void Constructor_SelectsAllProductsByDefault()
    {
        var sut = CreateSut();

        Assert.Equal(2, sut.ProductFilter.Count);
        Assert.All(sut.ProductFilter, p => Assert.True(p.IsSelected));
        Assert.Equal("alle (2)", sut.ProductFilterSummary);
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
    public void SetViewMode_Course_QueriesTimeSeries()
    {
        _analytics.GetSalesOverTime(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<AnalysisTimeResolution>())
            .Returns(new List<TimeBucketSales>
            {
                new() { BucketStart = DateTime.Today.AddHours(10), TotalAmount = 5 }
            });
        WithFigures(new SalesFigure { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 5, TotalRevenue = 15.00m });
        var sut = CreateSut();

        var course = sut.ViewModes.Single(v => (string)v.Value == "Course");
        sut.SetViewModeCommand.Execute(course);

        Assert.True(sut.IsCourse);
        Assert.False(sut.IsBreakdown);
        Assert.True(course.IsSelected); // drives the highlighted segment in the UI
        Assert.NotEmpty(sut.CourseSeries);
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
}
