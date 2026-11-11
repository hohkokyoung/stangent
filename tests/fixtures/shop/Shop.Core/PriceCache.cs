// D215: code in a function given to a cache's get-or-create call runs only when nothing is stored for the key.
// D216: a call after `?.` (and its arguments) runs only when what's before it is set.
namespace Shop.Core;

public interface IPriceCache
{
    T Get<T>(string key, Func<T> acquire);
}

public sealed class PriceCacheTask(IPriceCache cache, System.Collections.Concurrent.ConcurrentDictionary<int, int> seen, PriceLoader? loader) : IScheduleTask
{
    public Task ExecuteAsync()
    {
        var price = cache.Get("price", () => PriceLoader.Load(1));
        seen.GetOrAdd(price, id => PriceLoader.Load(id));
        PriceLoader.Load(price);
        loader?.Reload(PriceLoader.Load(price));
        return Task.CompletedTask;
    }
}

public sealed class PriceLoader
{
    public static int Load(int id) => id;
    public void Reload(int price) { }
}
