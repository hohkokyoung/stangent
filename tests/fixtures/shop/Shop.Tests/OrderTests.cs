using Shop.Core;

namespace Shop.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute;

public class OrderTests
{
    [Fact]
    public void Placing_an_order_keeps_the_customer()
    {
        var order = new OrderService().PlaceOrder("ann", 5m);
        if (order.Customer != "ann") throw new InvalidOperationException();
    }
}
