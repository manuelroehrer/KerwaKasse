using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KerwaKasse.MVVM.ViewModel
{
    public class ProductsViewModel : PropertyChangedBase
    {
        public RelayCommand AddNewProductCommand { get; }
        public RelayCommand SaveNewProductCommand { get; }
        public RelayCommand DiscardNewProductCommand { get; }
        public RelayCommand SaveEditingProductCommand { get; }
        public RelayCommand DiscardEditingProductCommand { get; }

        private readonly IProductService _productService;
        private readonly ISettingsService _settings;

        private List<ProductModel> products;
        public List<ProductModel> Products
        {
            get { return products; }
            set
            {
                products = value;
                OnPropertyChanged();
            }
        }

        private ProductModel selectedProduct;
        public ProductModel SelectedProduct
        {
            get { return selectedProduct; }
            set
            {
                selectedProduct = value;
                OnPropertyChanged();
                OnSelectedProductChanged();
            }
        }

        private ProductModel newProduct;
        public ProductModel NewProduct
        {
            get { return newProduct; }
            set
            {
                newProduct = value;
                OnPropertyChanged();
            }
        }

        private ProductModel editingProduct;
        public ProductModel EditingProduct
        {
            get { return editingProduct; }
            set
            {
                editingProduct = value;
                OnPropertyChanged();
            }
        }

        private double sidePanelWidth;
        public double SidePanelWidth
        {
            get { return sidePanelWidth; }
            set
            {
                sidePanelWidth = value;
                OnPropertyChanged();
            }
        }

        private List<int> cmbItems;
        public List<int> CmbItems
        {
            get { return cmbItems; }
            set
            {
                cmbItems = value;
                OnPropertyChanged();
            }
        }

        private int cmbSelection;
        public int CmbSelection
        {
            get { return cmbSelection; }
            set
            {
                if (cmbSelection != value)
                {
                    cmbSelection = value;
                    OrderChanged = true;
                    OnPropertyChanged();
                }
            }
        }

        private bool mode_AddNew;
        public bool Mode_AddNew
        {
            get { return mode_AddNew; }
            set
            {
                mode_AddNew = value;
                OnPropertyChanged();
            }
        }

        private bool mode_Editing;
        public bool Mode_Editing
        {
            get { return mode_Editing; }
            set
            {
                mode_Editing = value;
                OnPropertyChanged();
            }
        }

        private bool mode_None;
        public bool Mode_None
        {
            get { return mode_None; }
            set
            {
                mode_None = value;
                OnPropertyChanged();
            }
        }

        private bool orderChanged;
        public bool OrderChanged
        {
            get { return orderChanged; }
            set
            {
                orderChanged = value;
                OnPropertyChanged();
            }
        }

        public ProductsViewModel(IProductService productService, ISettingsService settings)
        {
            _productService = productService;
            _settings = settings;

            AddNewProductCommand = new RelayCommand(o => AddNewProduct());
            SaveNewProductCommand = new RelayCommand(o => SaveNewProduct());
            DiscardNewProductCommand = new RelayCommand(o => DiscardProduct());
            SaveEditingProductCommand = new RelayCommand(o => SaveEditingProduct());
            DiscardEditingProductCommand = new RelayCommand(o => DiscardEditingProduct());

            SidePanelWidth = _settings.Get("productsSidePanelWidth", 300.0);

            Mode_AddNew = false;
            Mode_Editing = false;
            Mode_None = true;
            OrderChanged = false;

            NewProduct = null;
            Products = new List<ProductModel>();
            LoadData();
        }

        public void Reload()
        {
            DiscardProduct();
            SelectedProduct = null;
            LoadData();
        }

        private void LoadData()
        {
            var coreProducts = _productService.GetAll();
            Products = coreProducts.Select(p => new ProductModel
            {
                ProductID = p.Id,
                Description = p.Description,
                Price = p.Price,
                Available = p.Available,
                ColorAsString = p.Color,
                ImgPath = p.ImagePath,
                PositionNumber = p.SortOrder
            }).OrderBy(o => o.PositionNumber).ToList();
        }

        private void AddNewProduct()
        {
            SelectedProduct = null;

            Mode_AddNew = true;
            Mode_Editing = false;
            Mode_None = false;

            NewProduct = new ProductModel()
            {
                Available = true
            };
        }

        private void SaveNewProduct()
        {
            _productService.Add(new Product
            {
                Description = NewProduct.Description,
                Price = NewProduct.Price,
                Available = NewProduct.Available,
                Color = NewProduct.ColorAsString,
                ImagePath = NewProduct.ImgPath
            });
            Reload();
        }

        private void DiscardEditingProduct()
        {
            DiscardProduct();
            SelectedProduct = null;
        }

        private void DiscardProduct()
        {
            EditingProduct = null;
            NewProduct = null;

            Mode_AddNew = false;
            Mode_Editing = false;
            Mode_None = true;
            OrderChanged = false;
        }

        private void SaveEditingProduct()
        {
            _productService.Update(new Product
            {
                Id = EditingProduct.ProductID,
                Price = EditingProduct.Price,
                Available = EditingProduct.Available,
                Color = EditingProduct.ColorAsString,
                ImagePath = EditingProduct.ImgPath,
                SortOrder = EditingProduct.PositionNumber
            });

            if (OrderChanged == true)
            {
                OrderChanged = false;

                // Build list of product IDs in current display order
                var ids = Products.Select(p => p.ProductID).ToList();

                int currentIndex = ids.IndexOf(EditingProduct.ProductID);
                int newIndex = CmbSelection - 1;

                if (currentIndex >= 0 && currentIndex != newIndex)
                {
                    // Remove from old position, insert at new position
                    int id = ids[currentIndex];
                    ids.RemoveAt(currentIndex);
                    ids.Insert(newIndex, id);

                    // Assign sequential sort orders (1-based) and persist
                    _productService.UpdateSortOrder(
                        ids.Select((productId, i) => new Product
                        {
                            Id = productId,
                            SortOrder = i + 1
                        }));
                }
            }

            Reload();
        }

        private void OnSelectedProductChanged()
        {
            if (SelectedProduct != null)
            {
                DiscardProduct();

                EditingProduct = new ProductModel()
                {
                    ProductID = SelectedProduct.ProductID,
                    Description = SelectedProduct.Description,
                    Price = SelectedProduct.Price,
                    Available = SelectedProduct.Available,
                    AvailableIcon = SelectedProduct.AvailableIcon,
                    ColorAsString = SelectedProduct.ColorAsString,
                    PositionNumber = SelectedProduct.PositionNumber,
                    Color = SelectedProduct.Color,
                    ImgPath = SelectedProduct.ImgPath
                };

                Mode_AddNew = false;
                Mode_Editing = true;
                Mode_None = false;

                CmbItems = Enumerable.Range(1, Products.Count).ToList();
                CmbSelection = CmbItems.Find(e => e == SelectedProduct.PositionNumber);
                OrderChanged = false; // Reset – was set by CmbSelection setter during initialization
            }
        }

        public void SaveOrderPanelWidth(double actualWidth)
        {
            _settings.Set("productsSidePanelWidth", actualWidth);
            _settings.Save();
        }
    }
}
