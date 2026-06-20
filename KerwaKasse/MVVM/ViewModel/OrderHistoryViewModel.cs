using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// Reworked order-history view. Shows the orders of a selected day in a master list on the left;
    /// the selected order is shown read-only in a receipt card on the right. Editing is explicit:
    /// "Bearbeiten" switches the card into edit mode where amounts can be changed, positions removed
    /// (greyed out, not deleted from view) or added (via a dialog). Changes are highlighted and only
    /// persisted on "Speichern"; "Abbrechen" discards them. Deleting a whole order is confirmed via a
    /// flyout in the view, so the command itself just performs the deletion.
    /// </summary>
    public class OrderHistoryViewModel : PropertyChangedBase
    {
        private readonly IOrderService _orderService;
        private readonly IProductService _productService;

        public RelayCommand BeginEditCommand { get; }
        public RelayCommand CancelEditCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteOrderCommand { get; }
        public RelayCommand AddPositionCommand { get; }
        public RelayCommand ToggleRemovePositionCommand { get; }

        /// <summary>Set by the view: shows the add-product dialog with the given candidates and
        /// invokes the callback with the chosen product (or is never called on cancel).</summary>
        public Action<IReadOnlyList<ProductModel>, Action<ProductModel>> ShowAddPositionPicker { get; set; }

        private readonly List<ProductModel> _allProducts;
        private readonly List<OrderModel> _ordersForSelectedDay = new();

        public OrderHistoryViewModel(IOrderService orderService, IProductService productService)
        {
            _orderService = orderService;
            _productService = productService;

            _allProducts = _productService.GetAll()
                .Select(p => new ProductModel { ProductID = p.Id, Name = p.Name, Price = p.Price, ColorAsString = p.Color })
                .ToList();

            Orders = new ObservableCollection<OrderModel>();
            EditPositions = new ObservableCollection<OrderHistoryEditPosition>();

            BeginEditCommand = new RelayCommand(_ => BeginEdit());
            CancelEditCommand = new RelayCommand(_ => CancelEdit());
            SaveCommand = new RelayCommand(_ => Save());
            DeleteOrderCommand = new RelayCommand(_ => Delete());
            AddPositionCommand = new RelayCommand(_ => AddPosition());
            ToggleRemovePositionCommand = new RelayCommand(o =>
            {
                if (o is OrderHistoryEditPosition pos)
                    ToggleRemove(pos);
            });

            Date = DateTime.Today;
        }

        private DateTime date;
        public DateTime Date
        {
            get => date;
            set
            {
                date = value;
                OnPropertyChanged();
                LoadOrders();
            }
        }

        public ObservableCollection<OrderModel> Orders { get; }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                string newValue = value ?? string.Empty;
                if (searchText == newValue)
                    return;

                searchText = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSearchActive));
                ApplyOrderFilter();
            }
        }

        public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

        public string EmptyStateTitle =>
            IsSearchActive ? "Keine passenden Verkäufe" : "Keine Verkäufe an diesem Tag";

        public string EmptyStateText =>
            IsSearchActive
                ? "Für den ausgewählten Tag passt kein Verkauf zu deiner Suche."
                : "Für den ausgewählten Tag wurden noch keine Eingaben erfasst.";

        private OrderModel selectedOrder;
        public OrderModel SelectedOrder
        {
            get => selectedOrder;
            set
            {
                if (ReferenceEquals(selectedOrder, value))
                    return;

                selectedOrder = value;
                // Switching the selection always leaves edit mode (discarding unsaved edits).
                IsEditing = false;
                EditPositions.Clear();
                RaiseEditStateChanged();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        /// <summary>True when an order is selected, i.e. the detail card should be shown.</summary>
        public bool HasSelection => SelectedOrder != null;

        private bool isEditing;
        public bool IsEditing
        {
            get => isEditing;
            set
            {
                isEditing = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Working copy of the selected order's positions, only populated in edit mode.</summary>
        public ObservableCollection<OrderHistoryEditPosition> EditPositions { get; }

        private decimal editTotal;
        public decimal EditTotal
        {
            get => editTotal;
            set
            {
                editTotal = value;
                OnPropertyChanged();
            }
        }

        /// <summary>True when at least one position is not marked for removal.</summary>
        public bool HasActivePositions => EditPositions.Any(p => !p.IsRemoved);

        /// <summary>True when anything was changed (amount, added or removed) since editing started.</summary>
        public bool HasChanges => EditPositions.Any(p => p.IsChanged);

        private bool hasInputError;
        /// <summary>Set by the view when an amount field currently holds an invalid value.</summary>
        public bool HasInputError
        {
            get => hasInputError;
            set
            {
                hasInputError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
            }
        }

        /// <summary>Saving needs at least one active position, an actual change, and no input error.</summary>
        public bool CanSave => HasActivePositions && HasChanges && !HasInputError;

        public void Reload() => LoadOrders();

        private void LoadOrders()
        {
            int? previouslySelectedId = SelectedOrder?.OrderID;

            _ordersForSelectedDay.Clear();
            var coreOrders = _orderService.GetOrdersByDate(Date);

            if (coreOrders != null)
            {
                foreach (var om in coreOrders
                             .Select(MapToModel)
                             .OrderByDescending(o => o.OrderID))
                {
                    _ordersForSelectedDay.Add(om);
                }
            }

            ApplyOrderFilter(previouslySelectedId);
        }

        private void ApplyOrderFilter(int? preferredSelectionId = null)
        {
            int? selectionId = preferredSelectionId ?? SelectedOrder?.OrderID;

            Orders.Clear();
            foreach (var order in _ordersForSelectedDay.Where(OrderMatchesSearch))
                Orders.Add(order);

            // Restore the previous selection if it is still visible, otherwise clear the detail card.
            SelectedOrder = selectionId.HasValue
                ? Orders.FirstOrDefault(s => s.OrderID == selectionId.Value)
                : null;

            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateText));
        }

        private bool OrderMatchesSearch(OrderModel order)
        {
            string search = SearchText.Trim();
            if (string.IsNullOrEmpty(search))
                return true;

            return order.OrderPositions.Any(position => PositionMatchesSearch(position, search))
                   || PriceMatchesSearch(order.Total, search);
        }

        private static bool PositionMatchesSearch(OrderPositionModel position, string search)
        {
            return position.Product.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PriceMatchesSearch(decimal price, string search)
        {
            var culture = CultureInfo.CurrentCulture;
            string normalizedSearch = search
                .Replace(culture.NumberFormat.CurrencySymbol, string.Empty)
                .Trim();

            bool exactPriceMatch = decimal.TryParse(search, NumberStyles.Currency, culture, out var parsedPrice)
                                   || decimal.TryParse(normalizedSearch, NumberStyles.Number, culture, out parsedPrice);

            return (exactPriceMatch && price == parsedPrice)
                   || price.ToString("C", culture).Contains(search, StringComparison.OrdinalIgnoreCase)
                   || price.ToString("N2", culture).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);
        }

        private OrderModel MapToModel(Order o)
        {
            var om = new OrderModel
            {
                OrderID = o.Id,
                OrderTime = o.OrderTime,
                OrderPositions = o.Positions.Select(p => new OrderPositionModel
                {
                    Amount = p.Amount,
                    Product = new ProductModel
                    {
                        ProductID = p.ProductId,
                        Name = p.ProductName,
                        // The order's snapshotted unit price, kept on edit.
                        Price = p.UnitPrice,
                        // Colour comes from the current product (the order doesn't store it).
                        ColorAsString = ColorOf(p.ProductId)
                    }
                }).ToList()
            };
            om.UpdateDescAndTotal();
            return om;
        }

        /// <summary>The current colour of a product (hex), or null if the product no longer exists.</summary>
        private string ColorOf(int productId) =>
            _allProducts.FirstOrDefault(p => p.ProductID == productId)?.ColorAsString;

        private void BeginEdit()
        {
            if (SelectedOrder == null)
                return;

            EditPositions.Clear();
            foreach (var pos in SelectedOrder.OrderPositions)
            {
                AddEditPosition(new OrderHistoryEditPosition
                {
                    ProductId = pos.Product.ProductID,
                    ProductName = pos.Product.Name,
                    UnitPrice = pos.Product.Price,
                    ColorBrush = pos.Product.Color,
                    OriginalAmount = pos.Amount,
                    Amount = pos.Amount,
                    IsNew = false
                });
            }

            IsEditing = true;
            RecalculateEditTotal();
        }

        private void CancelEdit()
        {
            EditPositions.Clear();
            IsEditing = false;
            RaiseEditStateChanged();
        }

        // Re-evaluates the edit-state flags after the working set changed wholesale (e.g. cleared),
        // so the "Geändert" chip and the save button update immediately.
        private void RaiseEditStateChanged()
        {
            OnPropertyChanged(nameof(HasActivePositions));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(CanSave));
        }

        private void AddPosition()
        {
            var usedIds = EditPositions.Select(p => p.ProductId).ToHashSet();
            var addable = _allProducts.Where(p => !usedIds.Contains(p.ProductID)).ToList();

            if (addable.Count == 0)
                return;

            ShowAddPositionPicker?.Invoke(addable, product =>
            {
                if (product == null)
                    return;

                AddEditPosition(new OrderHistoryEditPosition
                {
                    ProductId = product.ProductID,
                    ProductName = product.Name,
                    // A newly added position uses the product's current price.
                    UnitPrice = product.Price,
                    ColorBrush = product.Color,
                    OriginalAmount = 0,
                    Amount = 1,
                    IsNew = true
                });

                RecalculateEditTotal();
            });
        }

        private void ToggleRemove(OrderHistoryEditPosition position)
        {
            if (position.IsNew && position.IsRemoved == false)
            {
                // A freshly added position is just dropped entirely rather than greyed out.
                EditPositions.Remove(position);
            }
            else
            {
                position.IsRemoved = !position.IsRemoved;
            }

            RecalculateEditTotal();
        }

        // Adds an edit row and keeps the running total in sync when its amount changes (via the
        // touch stepper, the +/- buttons or keyboard entry).
        private void AddEditPosition(OrderHistoryEditPosition position)
        {
            position.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OrderHistoryEditPosition.Amount))
                    RecalculateEditTotal();
            };
            EditPositions.Add(position);
        }

        /// <summary>Recomputes per-position and overall totals. Called when an amount changes and
        /// internally on add/remove.</summary>
        public void RecalculateEditTotal()
        {
            decimal total = 0m;
            foreach (var pos in EditPositions.Where(p => !p.IsRemoved))
                total += pos.Total;

            EditTotal = total;
            OnPropertyChanged(nameof(HasActivePositions));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(CanSave));
        }

        private void Save()
        {
            if (SelectedOrder == null || !CanSave)
                return;

            var coreOrder = new Order
            {
                Id = SelectedOrder.OrderID,
                OrderTime = SelectedOrder.OrderTime,
                Positions = EditPositions
                    .Where(p => !p.IsRemoved)
                    .Select(p => new OrderPosition
                    {
                        OrderId = SelectedOrder.OrderID,
                        ProductId = p.ProductId,
                        Amount = p.Amount,
                        UnitPrice = p.UnitPrice
                    }).ToList()
            };

            _orderService.ReplaceOrderPositions(coreOrder);
            IsEditing = false;
            EditPositions.Clear();
            RaiseEditStateChanged();
            LoadOrders();
        }

        private void Delete()
        {
            if (SelectedOrder == null)
                return;

            _orderService.DeleteOrder(SelectedOrder.OrderID);
            SelectedOrder = null;
            LoadOrders();
        }
    }
}
