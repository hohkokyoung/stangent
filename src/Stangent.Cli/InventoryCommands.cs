using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Flows;

/// <summary><c>stangent inventory …</c>: layer 5's PM inventory, put together from the code graph by code, no AI (D325–D329, D334, docs/inventory.md).</summary>
static class InventoryCommands
{
    public static void Add(RootCommand root)
    {
        var inventory = new Command("inventory", "Build the PM inventory (D325–D328, D334): every control a user can touch, named by the words they see, grouped per screen with its dialogs and menus, shared parts once, Not placed kept; writes specs/inventory.json and specs/inventory.md.");
        inventory.SetAction(pr => Cli.Run(pr, "inventory", ctx =>
        {
            var set = Inventory.Build(Cli.LoadGraph(ctx), ctx.Repo.Root);
            var (written, unchanged) = InventoryFiles.Write(ctx.Repo.Root, set);
            var counts = InventoryFiles.Counts(set);
            ctx.Span.Attributes["stangent.artifact.count"] = (int)counts["lines"]!["total"]!;
            return new JsonObject
            {
                ["commit"] = set.Commit, ["counts"] = counts,
                ["files"] = new JsonObject { ["json"] = InventoryFiles.JsonFile, ["md"] = InventoryFiles.MarkdownFile, ["written"] = written, ["unchanged"] = unchanged },
            };
        }));
        root.Subcommands.Add(inventory);

        var check = new Command("check", "D334 item 7 by code: the files byte for byte what the graph builds now; every ❓ says why; every skip reason on D326's fixed list; every control fact outside tests is exactly one line, skipped or set-aside entry.");
        check.SetAction(pr => Cli.Run(pr, "inventory check", ctx =>
        {
            var report = InventoryFiles.Check(ctx.Repo.Root, Cli.LoadGraph(ctx));
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["controls"]!;
            if (!(bool)report["ok"]!) throw new CheckFailed("inventory_check_failed", "The inventory failed a check: see problems.", report);
            return report;
        }));
        inventory.Subcommands.Add(check);
    }
}
