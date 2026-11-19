using Shop.Core;
using Xunit;

namespace Shop.Tests;

public class ReceiptTests
{
    [Fact]
    public void A_paid_order_says_paid() => Assert.Equal("paid 20", new Receipts().For(new Order(20, false)));
}
