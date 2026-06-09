using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.Model;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace KerwaKasse.MVVM.ViewModel
{
    public class StatisticsViewModel : PropertyChangedBase
    {
        private readonly IOrderService _orderService;

        private DateTime dateFrom;
        public DateTime DateFrom
        {
            get { return dateFrom; }
            set
            {
                if (dateFrom != value)
                {
                    dateFrom = value;
                    OnPropertyChanged();
                    OnSelectionChanged();
                }
            }
        }

        private DateTime timeFrom;
        public DateTime TimeFrom
        {
            get { return timeFrom; }
            set
            {
                if (timeFrom != value)
                {
                    timeFrom = value;
                    OnPropertyChanged();
                    OnSelectionChanged();
                }
            }
        }

        private DateTime dateTo;
        public DateTime DateTo
        {
            get { return dateTo; }
            set
            {
                if (dateTo != value)
                {
                    dateTo = value;
                    OnPropertyChanged();
                    OnSelectionChanged();
                }
            }
        }

        private DateTime timeTo;
        public DateTime TimeTo
        {
            get { return timeTo; }
            set
            {
                if (timeTo != value)
                {
                    timeTo = value;
                    OnPropertyChanged();
                    OnSelectionChanged();
                }
            }
        }

        private ObservableCollection<DateTime> timeFrom_Values;
        public ObservableCollection<DateTime> TimeFrom_Values
        {
            get { return timeFrom_Values; }
            set
            {
                timeFrom_Values = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<DateTime> timeTo_Values;
        public ObservableCollection<DateTime> TimeTo_Values
        {
            get { return timeTo_Values; }
            set
            {
                timeTo_Values = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<OrderPositionModel> orderPositions;
        public ObservableCollection<OrderPositionModel> OrderPositions
        {
            get { return orderPositions; }
            set
            {
                orderPositions = value;
                OnPropertyChanged();
            }
        }

        private decimal total;
        public decimal Total
        {
            get { return total; }
            set
            {
                total = value;
                OnPropertyChanged();
            }
        }

        private ISeries[] pieChartData;
        public ISeries[] PieChartData
        {
            get { return pieChartData; }
            set
            {
                pieChartData = value;
                OnPropertyChanged();
            }
        }

        public StatisticsViewModel(IOrderService orderService)
        {
            _orderService = orderService;

            // Build time lists once (static, never rebuilt)
            TimeFrom_Values = new ObservableCollection<DateTime>();
            TimeTo_Values = new ObservableCollection<DateTime>();

            for (int i = 0; i < 24; i++)
            {
                TimeFrom_Values.Add(TimeOfDay(i, 0));
                TimeTo_Values.Add(TimeOfDay(i, 0));
            }
            TimeTo_Values.Add(TimeOfDay(23, 59));

            // Set defaults via backing fields (no change events during init)
            dateFrom = DateTime.Today;
            dateTo = DateTime.Today;
            timeFrom = TimeFrom_Values[0];
            timeTo = TimeTo_Values[TimeTo_Values.Count - 1];

            OrderPositions = new ObservableCollection<OrderPositionModel>();

            // Initial data load
            LoadData();
            CreatePieChartData();
        }

        private static DateTime TimeOfDay(int hour, int minute) =>
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, hour, minute, 0);

        private void OnSelectionChanged()
        {
            // Validate: ensure end is not before start
            if (dateTo.Date < dateFrom.Date)
            {
                dateTo = dateFrom.Date;
                OnPropertyChanged(nameof(DateTo));
            }

            if (dateFrom.Date == dateTo.Date && timeTo.TimeOfDay <= timeFrom.TimeOfDay)
            {
                timeTo = TimeTo_Values[TimeTo_Values.Count - 1]; // 23:59
                OnPropertyChanged(nameof(TimeTo));
            }

            LoadData();
            CreatePieChartData();
        }

        private void LoadData()
        {
            DateTime from = new DateTime(DateFrom.Year, DateFrom.Month, DateFrom.Day, TimeFrom.Hour, TimeFrom.Minute, TimeFrom.Second);
            DateTime to = new DateTime(DateTo.Year, DateTo.Month, DateTo.Day, TimeTo.Hour, TimeTo.Minute, TimeTo.Second);

            var salesFigures = _orderService.GetSalesFigures(from, to);

            if (salesFigures == null || salesFigures.Count == 0)
            {
                OrderPositions = new ObservableCollection<OrderPositionModel>();
                Total = 0.0m;
                return;
            }

            var orderPositionModels = salesFigures
                .Where(sf => sf.TotalAmount > 0)
                .Select(sf => new OrderPositionModel
            {
                Amount = sf.TotalAmount,
                Total = sf.TotalRevenue,
                Product = new ProductModel
                {
                    ProductID = sf.ProductId,
                    Name = sf.ProductName,
                    ColorAsString = sf.ProductColor
                }
            }).ToList();

            decimal total = 0.0m;

            foreach (OrderPositionModel orderPosition in orderPositionModels) 
            {
                total += orderPosition.Total;
            }

            Total = total;
            OrderPositions = new ObservableCollection<OrderPositionModel>(orderPositionModels.OrderByDescending(o => o.Amount));
        }

        private void CreatePieChartData()
        {
            var series = new List<ISeries>();

            foreach (OrderPositionModel orderPos in OrderPositions)
            {
                var pieSeries = new PieSeries<double>
                {
                    Name = orderPos.Product.Name,
                    Values = new[] { (double)orderPos.Amount },
                    DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                    DataLabelsFormatter = point => orderPos.Product.Name,
                    ToolTipLabelFormatter = point => $"{orderPos.Amount}"
                };

                if (!string.IsNullOrEmpty(orderPos.Product.ColorAsString))
                {
                    if (SKColor.TryParse(orderPos.Product.ColorAsString, out var skColor))
                        pieSeries.Fill = new SolidColorPaint(skColor);
                }

                series.Add(pieSeries);
            }

            PieChartData = series.ToArray();
        }
    }
}
