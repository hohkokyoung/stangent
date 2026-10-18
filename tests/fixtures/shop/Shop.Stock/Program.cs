var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/api/stock/{id:int}", (int id) => id * 10);
app.MapGet("/api/stock/summary", () => "summary");
app.Run();
