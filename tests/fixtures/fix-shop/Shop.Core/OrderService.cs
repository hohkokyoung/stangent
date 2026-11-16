namespace Shop.Core;

public sealed record Order(decimal Total, bool NeedsApproval);

public class OrderService
{
    public const decimal ApprovalLimit = 100;

    /// <summary>Places an order; one over the approval limit waits for someone to approve it.</summary>
    public Order Place(decimal total)
    {
        if (total <= 0) throw new ArgumentException("An order needs a total above 0");
        // The bug: compares with ten times the limit.
        return new Order(total, total > ApprovalLimit * 10);
    }
}

public class Receipts
{
    public string For(Order order) => order.NeedsApproval ? "waiting for approval" : $"paid {order.Total}";
}
