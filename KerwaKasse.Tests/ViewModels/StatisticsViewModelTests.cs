using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class StatisticsViewModelTests
{
    private readonly IOrderService _orderService;
    private readonly StatisticsViewModel _sut;

    public StatisticsViewModelTests()
    {
        _orderService = Substitute.For<IOrderService>();
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<SalesFigure>());

        _sut = new StatisticsViewModel(_orderService);
    }

    [Fact]
    public void Constructor_SetsDefaultDatesToToday()
    {
        Assert.Equal(DateTime.Today, _sut.DateFrom);
        Assert.Equal(DateTime.Today, _sut.DateTo);
    }

    [Fact]
    public void Constructor_WithEmptyData_TotalIsZero()
    {
        Assert.Equal(0m, _sut.Total);
    }

    [Fact]
    public void LoadData_WithSalesFigures_CalculatesTotal()
    {
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<SalesFigure>
            {
                new() { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 35.00m },
                new() { ProductId = 2, ProductName = "Bier", TotalAmount = 5, TotalRevenue = 14.00m }
            });

        // Trigger reload via DateFrom setter (use different date to trigger change)
        _sut.DateFrom = DateTime.Today.AddDays(-1);

        Assert.Equal(49.00m, _sut.Total);
        Assert.Equal(2, _sut.OrderPositions.Count);
    }

    [Fact]
    public void OnSelectionChanged_CorrectsDatesWhenEndBeforeStart()
    {
        _sut.DateFrom = DateTime.Today;
        _sut.DateTo = DateTime.Today.AddDays(-1);

        // DateTo should be corrected to DateFrom
        Assert.True(_sut.DateTo >= _sut.DateFrom);
    }

    [Fact]
    public void TimeFrom_Values_Has24Entries()
    {
        Assert.Equal(24, _sut.TimeFrom_Values.Count);
    }

    [Fact]
    public void TimeTo_Values_Has25Entries_Including2359()
    {
        Assert.Equal(25, _sut.TimeTo_Values.Count);
        var last = _sut.TimeTo_Values[^1];
        Assert.Equal(23, last.Hour);
        Assert.Equal(59, last.Minute);
    }

    [Fact]
    public void OnSelectionChanged_SameDayInvalidTime_CorrectsToCeiling()
    {
        // Set same date for both
        _sut.DateFrom = DateTime.Today.AddDays(-1);
        _sut.DateTo = DateTime.Today.AddDays(-1);

        // Set TimeFrom to 14:00 and TimeTo to 10:00 (invalid)
        _sut.TimeFrom = _sut.TimeFrom_Values[14]; // 14:00
        _sut.TimeTo = _sut.TimeTo_Values[10];     // 10:00

        // TimeTo should be corrected to 23:59
        Assert.Equal(23, _sut.TimeTo.Hour);
        Assert.Equal(59, _sut.TimeTo.Minute);
    }

    [Fact]
    public void LoadData_ZeroAmountProducts_AreFiltered()
    {
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<SalesFigure>
            {
                new() { ProductId = 1, ProductName = "Bratwurst", TotalAmount = 10, TotalRevenue = 35.00m },
                new() { ProductId = 2, ProductName = "Leer", TotalAmount = 0, TotalRevenue = 0m }
            });

        _sut.DateFrom = DateTime.Today.AddDays(-1);

        // Only non-zero amount should appear
        Assert.Single(_sut.OrderPositions);
        Assert.Equal("Bratwurst", _sut.OrderPositions[0].Product.Name);
    }

    [Fact]
    public void LoadData_OrderPositions_SortedByAmountDescending()
    {
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<SalesFigure>
            {
                new() { ProductId = 1, ProductName = "Wenig", TotalAmount = 2, TotalRevenue = 7.00m },
                new() { ProductId = 2, ProductName = "Viel", TotalAmount = 20, TotalRevenue = 56.00m }
            });

        _sut.DateFrom = DateTime.Today.AddDays(-1);

        Assert.Equal("Viel", _sut.OrderPositions[0].Product.Name);
        Assert.Equal("Wenig", _sut.OrderPositions[1].Product.Name);
    }
}
