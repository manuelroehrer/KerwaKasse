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
    int GetUsageCount(int productId);
}
