using Shop.Core;
using Shop.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payment"));
builder.Services.AddHttpClient<StockClient>(c => c.BaseAddress = new("http://stock-api"));

var app = builder.Build();
var orders = app.MapGroup("api/orders");
orders.MapPost("/", Endpoints.CreateOrder);
app.MapControllerRoute(name: "checkout", pattern: "checkout/", defaults: new { controller = "Checkout", action = "Index" });
app.Run();
