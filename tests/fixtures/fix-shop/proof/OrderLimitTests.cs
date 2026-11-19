using Shop.Core;
using Xunit;

namespace Shop.Tests;

public class OrderLimitTests
{
    [Fact]
    public void Orders_over_100_wait_for_approval()
    {
        var order = new OrderService().Place(150);
        Assert.True(order.NeedsApproval, "an order of 150 was placed without approval");
    }
}
