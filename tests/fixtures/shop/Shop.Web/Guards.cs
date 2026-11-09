// Early exits (D174, D176): the framework's own refusals under conditions, a throw, and a result that isn't an exit.
namespace Shop.Web;

public static class Guards
{
    public static IResult Find(int? id, bool closed)
    {
        if (id is null) return Results.NotFound();
        if (closed) return Results.StatusCode(503);
        return id > 100 ? Results.BadRequest() : Results.Ok(id);
    }

    public static string Name(string? name) => name ?? throw new ArgumentNullException(nameof(name));
}

// Z1: a guard inside a switch case conditions the rest of that case.
public static class CaseGuards
{
    public static IResult Pick(string mode, int? id) {
        switch (mode) { case "one": if (id is null) return Results.NotFound(); return Results.BadRequest(); default: return Results.Ok(); }
    }
}
