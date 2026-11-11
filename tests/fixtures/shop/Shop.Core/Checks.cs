namespace Shop.Core;

/// <summary>Checks registered by an attribute naming the event that reruns them (D206).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RecheckOnAttribute(Type eventType) : Attribute
{
    public Type EventType { get; } = eventType;
}

public abstract class CheckBase
{
    public abstract string Check();
    public virtual bool OnStartup => false;
}

[RecheckOn(typeof(OrderPlaced))]
public class StockLevelCheck : CheckBase
{
    public override string Check() => "ok";
}

// Order is no event: nothing handles it, so this attribute registers no handler.
[RecheckOn(typeof(Order))]
public class OrderTotalCheck : CheckBase
{
    public override string Check() => "ok";
}

// An event no handler takes by name, carried by a catch-all handler of the kind it is.
public interface IShopEvent;

public class StockLow : IShopEvent;

public class AuditEveryEvent : IHandle<IShopEvent>
{
    public Task HandleAsync(IShopEvent message) => Task.CompletedTask;
}

[RecheckOn(typeof(StockLow))]
public class ReorderCheck : CheckBase
{
    public override string Check() => "ok";
}
