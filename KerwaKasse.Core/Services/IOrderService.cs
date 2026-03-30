using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

public interface IOrderService
{
    void PlaceOrder(IEnumerable<OrderPosition> positions);
    List<Order> GetOrdersByDate(DateTime date);
    void UpdateOrderPositions(Order order);
    List<SalesFigure> GetSalesFigures(DateTime from, DateTime to);
}
