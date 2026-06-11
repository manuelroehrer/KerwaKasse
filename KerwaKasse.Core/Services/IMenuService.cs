using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

public interface IMenuService
{
    List<Menu> GetAll();
    List<int> GetProductIds(int menuId);
    int Add(string name);
    void Rename(int menuId, string newName);
    void Delete(int menuId);
    void SetProducts(int menuId, IEnumerable<int> productIds);
    void ApplyMenu(int menuId);
}
