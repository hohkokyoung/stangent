using Shop.Core;

namespace Shop.Web;

public static class Endpoints
{
    public static Order CreateOrder(IOrderService service, IConfiguration config)
    {
        var region = config["Shop:Region"];
        var missing = config.GetValue<string>("Shop:NotInAppSettings");
        return service.PlaceOrder(region ?? missing ?? "", 1m);
    }
}

public class StockClient(HttpClient http)
{
    const string Base = "api/stock/";

    public Task<string> GetStock(int id) => http.GetStringAsync($"{Base}{id}");
}
