using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// Reworked order-history view. Shows the orders of a selected day in a master list on the left;
    /// the selected order is shown read-only in a receipt card on the right. Editing is explicit:
    /// "Bearbeiten" switches the card into edit mode where amounts can be changed, positions removed
    /// (greyed out, not deleted from view) or added (via a dialog). Entered prices (variable prices,
    /// free entries) and what a free entry is for can be changed too, with the dialogs of the till.
    /// Changes are highlighted and only persisted on "Speichern"; "Abbrechen" discards them. Deleting a
    /// whole order is confirmed via a flyout in the view, so the command itself just performs the deletion.
    /// </summary>
    public class OrderHistoryViewModel : PropertyChangedBase
    {
        // Number of recent descriptions the free entry dialog offers as suggestions.
        private const int RecentNoteCount = 5;

        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly IDialogService _dialogService;

        public RelayCommand BeginEditCommand { get; }
        public RelayCommand CancelEditCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteOrderCommand { get; }
        public RelayCommand AddPositionCommand { get; }
        public RelayCommand ToggleRemovePositionCommand { get; }
        public RelayCommand EditPriceCommand { get; }
        public RelayCommand EditPurposeCommand { get; }

        private readonly List<ProductModel> _allProducts;
        private readonly List<OrderModel> _ordersForSelectedDay = new();

        public OrderHistoryViewModel(IOrderService orderService, IProductService productService, IDialogService dialogService)
        {
            _orderService = orderService;
            _productService = productService;
            _dialogService = dialogService;

            _allProducts = _productService.GetAll()
                .Select(p => new ProductModel { ProductID = p.Id, Name = p.Name, Price = p.Price, VariablePrice = p.VariablePrice, ColorAsString = p.Color })
                .ToList();

            Orders = new ObservableCollection<OrderModel>();
            EditPositions = new ObservableCollection<OrderHistoryEditPosition>();

            BeginEditCommand = new RelayCommand(_ => BeginEdit());
            CancelEditCommand = new RelayCommand(_ => CancelEdit());
            SaveCommand = new RelayCommand(_ => Save());
            DeleteOrderCommand = new RelayCommand(_ => Delete());
            AddPositionCommand = new RelayCommand(async _ => await AddPosition());
            ToggleRemovePositionCommand = new RelayCommand(o =>
            {
                if (o is OrderHistoryEditPosition pos)
                    ToggleRemove(pos);
            });
            EditPriceCommand = new RelayCommand(async o =>
            {
                if (o is OrderHistoryEditPosition pos)
                    await EditPrice(pos);
            });
            EditPurposeCommand = new RelayCommand(async o =>
            {
                if (o is OrderHistoryEditPosition pos)
                    await EditPurpose(pos);
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

        // The name (with a description) and the kind ("Variabler Preis", "Sonderpreis", "Freie Eingabe").
        private static bool PositionMatchesSearch(OrderPositionModel position, string search)
        {
            return position.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                   || (position.Subline?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
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
                    UnitPrice = p.UnitPrice,
                    ProductId = p.ProductId,
                    Kind = p.Kind,
                    ListPrice = p.ListPrice,
                    Note = p.Note,
                    // None for a free entry without product.
                    Product = p.ProductId is int productId
                        ? new ProductModel
                        {
                            ProductID = productId,
                            Name = p.ProductName,
                            // The normal price at the time of the sale.
                            Price = p.ListPrice ?? p.UnitPrice,
                            // Colour comes from the current product (the order doesn't store it).
                            ColorAsString = ColorOf(productId)
                        }
                        : null
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
                    Kind = pos.Kind,
                    ProductId = pos.ProductId,
                    ProductName = pos.Product?.Name ?? string.Empty,
                    Note = pos.Note,
                    ListPrice = pos.ListPrice,
                    ColorBrush = pos.ColorBrush,
                    UnitPrice = pos.UnitPrice,
                    Amount = pos.Amount,
                    OriginalAmount = pos.Amount,
                    OriginalUnitPrice = pos.UnitPrice,
                    OriginalProductId = pos.ProductId,
                    OriginalNote = pos.Note,
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

        /// <summary>"Position nachtragen": a product, asking for the price of one with a variable price,
        /// or a free entry, asking for its amount and what it is for.</summary>
        private async Task AddPosition()
        {
            // A product is listed once at its normal price; one with a variable price can appear again
            // at another price.
            var usedIds = EditPositions.Where(p => p.Kind == OrderPositionKind.Regular).Select(p => p.ProductId).ToHashSet();
            var addable = _allProducts.Where(p => p.VariablePrice || !usedIds.Contains(p.ProductID)).ToList();

            var choice = await _dialogService.ShowAddPositionAsync(addable);
            if (choice == null)
                return;

            if (choice.IsFreeEntry)
            {
                var entry = await _dialogService.ShowFreeEntryAsync(_orderService.GetRecentNotes(RecentNoteCount), _allProducts);
                if (entry != null)
                    AddNewPosition(entry.Product, OrderPositionKind.FreeEntry, entry.Amount, entry.Note);
                return;
            }

            var product = choice.Product;
            if (!product.VariablePrice)
            {
                // A newly added position uses the product's current price.
                AddNewPosition(product, OrderPositionKind.Regular, product.Price, note: null);
            }
            else if (await _dialogService.ShowPriceEntryAsync(product.Name, product.Price) is decimal price)
            {
                AddNewPosition(product, OrderPositionKind.VariablePrice, price, note: null);
            }
        }

        private void AddNewPosition(ProductModel product, OrderPositionKind kind, decimal unitPrice, string note)
        {
            AddEditPosition(new OrderHistoryEditPosition
            {
                Kind = kind,
                ProductId = product?.ProductID,
                ProductName = product?.Name ?? string.Empty,
                Note = note,
                ListPrice = product?.Price,
                ColorBrush = product?.Color ?? OrderPositionModel.FreeEntryBrush,
                UnitPrice = unitPrice,
                Amount = 1,
                OriginalAmount = 0,
                IsNew = true
            });

            RecalculateEditTotal();
        }

        /// <summary>A new price for a variable-price position or a free entry, on the till's number pad.</summary>
        private async Task EditPrice(OrderHistoryEditPosition position)
        {
            if (!position.CanEditPrice)
                return;

            if (await _dialogService.ShowPriceEntryAsync(position.DisplayName, position.UnitPrice, change: true) is decimal price)
            {
                position.UnitPrice = price;
                RecalculateEditTotal();
            }
        }

        /// <summary>Another description and/or product for a free entry.</summary>
        private async Task EditPurpose(OrderHistoryEditPosition position)
        {
            if (!position.CanEditPurpose)
                return;

            var product = position.ProductId is int id ? _allProducts.FirstOrDefault(p => p.ProductID == id) : null;
            if (position.ProductId != null && product == null)
                // The product no longer exists; keep it selectable under its old name.
                product = new ProductModel { ProductID = position.ProductId.Value, Name = position.ProductName, Price = position.ListPrice ?? position.UnitPrice };

            var result = await _dialogService.ShowFreeEntryPurposeAsync(
                new FreeEntryResult(position.UnitPrice, position.Note, product),
                _orderService.GetRecentNotes(RecentNoteCount), _allProducts);
            if (result == null)
                return;

            position.SetPurpose(result.Product, result.Note);
            RecalculateEditTotal();
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
                // Rows that ended up alike (same product, kind, price and description) are saved as one,
                // like the till adds them up.
                Positions = EditPositions
                    .Where(p => !p.IsRemoved)
                    .GroupBy(p => (p.ProductId, p.Kind, p.UnitPrice, p.Note))
                    .Select(g => new OrderPosition
                    {
                        OrderId = SelectedOrder.OrderID,
                        ProductId = g.Key.ProductId,
                        Kind = g.Key.Kind,
                        Amount = g.Sum(p => p.Amount),
                        UnitPrice = g.Key.UnitPrice,
                        ListPrice = g.First().ListPrice,
                        Note = g.Key.Note
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
