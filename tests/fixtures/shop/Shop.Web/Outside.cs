// Outside calls through clients other than System.Net.Http's (G4).
namespace Flurl.Http
{
    // A stand-in for Flurl's string extensions.
    public static class FlurlStand
    {
        public static string AppendPathSegment(this string url, object segment) => url + "/" + segment;
        public static Task<T> GetJsonAsync<T>(this string url) => Task.FromResult(default(T)!);
        public static Task PostJsonAsync(this string url, object body) => Task.CompletedTask;
        public static string WithHeader(this string url, string name, string value) => url;
    }
}

namespace Shop.Web
{
    using Flurl.Http;

    public interface IHttpClient { string Get(Request request); }
    public sealed record Request(string Url);

    public sealed class RatesProxy(IHttpClient http)
    {
        public string Rates(string currency) => http.Get(new Request("https://rates.example/" + currency));
        public Task<int> Fixed() => "https://rates.example/fixed".GetJsonAsync<int>();
        public Task Report(int id) => "https://audit.example".AppendPathSegment(id).PostJsonAsync(new { id });
        public Task<int> Ping() => "https://audit.example/ping".WithHeader("Accept", "json").GetJsonAsync<int>();
    }
}
