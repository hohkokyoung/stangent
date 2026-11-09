// System jobs and the code that queues them (D171, D173), the way an app writes its own: a generic command handler
// interface whose type parameter is constrained to a command base, a scheduled task, and a queue taking any command.
namespace Shop.Core;

public abstract class Command { }

public interface IExecute<TCommand> where TCommand : Command
{
    void Execute(TCommand command);
}

public interface IScheduleTask
{
    Task ExecuteAsync();
}

public sealed class RefreshStockCommand(int shelfId) : Command
{
    public int ShelfId { get; } = shelfId;
}

public sealed class CommandQueue
{
    public void Push<TCommand>(TCommand command) where TCommand : Command { }

    // Passes its own items along: the caller's call is the one that queues them.
    public void PushMany<TCommand>(List<TCommand> commands) where TCommand : Command { foreach (var c in commands) Push(c); }

    public void Report(Command command) { }
}

public sealed class RefreshStockService(CommandQueue queue) : IExecute<RefreshStockCommand>
{
    public void Execute(RefreshStockCommand command)
    {
        queue.Report(command); // passed along from a parameter: not queued here
        foreach (var id in new[] { command.ShelfId })
        {
            switch (id)
            {
                case 0:
                    Recount(id);
                    break;
                default:
                    try { Recount(id); }
                    catch (InvalidOperationException e) when (e.Message.Length > 0) { Recount(-1); }
                    break;
            }
        }
    }

    static void Recount(int id) { }
}

public sealed class CleanupTask : IScheduleTask
{
    public Task ExecuteAsync() => Task.CompletedTask;
}

public sealed class ShelfAddedHandler(CommandQueue queue)
{
    public void Added(int shelfId, bool quiet)
    {
        if (!quiet) queue.Push(new RefreshStockCommand(shelfId));
        else queue.PushMany(new[] { shelfId }.Select(s => new RefreshStockCommand(s)).ToList());
    }

    // The command's type is whatever the body says: known only at runtime.
    public void FromApi(string body) => queue.Push(System.Text.Json.JsonSerializer.Deserialize<Command>(body)!);
}
