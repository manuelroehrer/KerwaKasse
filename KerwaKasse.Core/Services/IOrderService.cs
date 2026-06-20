using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

public interface IOrderService
{
    void PlaceOrder(IEnumerable<OrderPosition> positions);
    List<Order> GetOrdersByDate(DateTime date);

    /// <summary>Replaces all positions of an existing order with the given set, so positions can be
    /// added, removed or have their amount changed in one go. The order's positions are deleted and
    /// re-inserted in a single transaction.</summary>
    void ReplaceOrderPositions(Order order);

    /// <summary>Deletes an order and all of its positions. This also removes the order from the
    /// sales figures, since those aggregate over the same positions.</summary>
    void DeleteOrder(int orderId);

    List<SalesFigure> GetSalesFigures(DateTime from, DateTime to);
}
