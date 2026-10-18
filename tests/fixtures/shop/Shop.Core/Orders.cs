namespace Shop.Core;

public abstract class Entity
{
    public int Id { get; set; }
}

public class Order : Entity
{
    public decimal Total { get; set; }
    public string Customer { get; set; } = "";
}

public record OrderPlaced(int OrderId);

public interface IHandle<T>
{
    Task HandleAsync(T message);
}

public interface IOrderService
{
    Order PlaceOrder(string customer, decimal total);
}

public class OrderService : IOrderService
{
    public Order PlaceOrder(string customer, decimal total)
    {
        var order = new Order { Customer = customer, Total = total };
        Publish(new OrderPlaced(order.Id));
        return order;
    }

    static void Publish(object message) { }
}

public class SendReceiptOnOrderPlaced : IHandle<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced message) => Task.CompletedTask;
}

public class PaymentOptions
{
    public string ApiKey { get; set; } = "";
    public int Retries { get; set; }
}

public static class PluginLoader
{
    public static object? Load(Type type) => Activator.CreateInstance(type);
}
