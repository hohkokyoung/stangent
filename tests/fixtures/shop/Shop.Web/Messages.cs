// SignalR messages and background jobs (G10, G11), and constants (G9). Stand-ins: the detectors match them by name and namespace.
namespace Microsoft.AspNetCore.SignalR
{
    public interface IClientProxy { Task SendAsync(string method, object? arg, CancellationToken ct = default); }
    public interface IHubClients { IClientProxy All { get; } }
    public interface IHubContext { IHubClients Clients { get; } }
}

namespace Hangfire
{
    public static class BackgroundJob { public static string Enqueue(System.Linq.Expressions.Expression<Action> job) => ""; }
    public static class RecurringJob { public static void AddOrUpdate(string id, System.Linq.Expressions.Expression<Action> job, string cron) { } }
}

namespace Shop.Web
{
    using Microsoft.AspNetCore.SignalR;

    public static class Messages
    {
        public const string ShelfChanged = "ShelfChanged";
        public static readonly string Restocked = "Restocked";
        public const int MaxShelves = 12;
    }

    public interface IEventHub { Task SendMessageAsync(string method, object body); }

    public sealed class EventHub(IHubContext hub) : IEventHub
    {
        public Task SendMessageAsync(string method, object body) => hub.Clients.All.SendAsync(method, body);
        public Task Ping() => hub.Clients.All.SendAsync("ping", null);
    }

    public sealed class ShelfNotifier(IEventHub events)
    {
        public Task Changed() => events.SendMessageAsync(Messages.ShelfChanged, new { });
        public void Later() => Hangfire.BackgroundJob.Enqueue(() => Recount());
        public void Nightly() => Hangfire.RecurringJob.AddOrUpdate("recount", () => Recount(), "0 3 * * *");
        public void Recount() { }
    }
}
