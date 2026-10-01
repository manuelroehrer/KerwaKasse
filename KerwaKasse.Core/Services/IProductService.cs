using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

public interface IProductService
{
    List<Product> GetAll();
    List<Product> GetAvailable();
    void Add(Product product);
    void Update(Product product);
    void UpdateAvailability(int productId, bool available);
    void UpdateSortOrder(IEnumerable<Product> products);

    /// <summary>Moves an available product to a new index among the available products, the order
    /// the order panel shows. Unavailable products keep their positions: the available ones only
    /// trade the places they already hold.</summary>
    void MoveAvailable(int productId, int newIndex);
    int GetUsageCount(int productId);
}
