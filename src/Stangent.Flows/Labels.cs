using System.Text.RegularExpressions;
using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>
/// What a line is called (D327, D333): the words a user sees, or <see cref="Words"/> null with the code name and why.
/// <see cref="Source"/> says where the words came from: <c>key</c> (a text key through the app's translation files), <c>text</c>
/// (words written in the markup), <c>model</c> (the property an <c>asp-for</c> field is bound to), <c>route</c>, <c>title</c>, <c>heading</c>.
/// </summary>
public sealed record Named(string? Words, string? Help, string Code, string Source, string Why = "no visible text found")
{
    public bool Visible => Words is not null;
    /// <summary>The words, or "❓ code: why" (D327 item 3).</summary>
    public string Line => Words ?? $"❓ {Code}: {Why}";
}

/// <summary>A field's choices by the names a user sees (D327 item 1): <see cref="Names"/> in order, or why there are none to list.</summary>
public sealed record Choices(IReadOnlyList<Named> Names, string Kind, string Note)
{
    public static readonly Choices None = new([], "", "");
    public bool Visible => Names.Count > 0 && Names.All(n => n.Visible);
}

/// <summary>
/// The label lookup (D327, D333): from the graph's text keys and words to what a user sees, through the app's own translation files
/// (<see cref="Translations"/>: English JSON, flat or nested, and .NET resource XML), by rules that name no app. Layer 3's click names
/// and the inventory both use it, so the two never disagree (D327 item 4).
/// </summary>
public sealed partial class Labels
{
    readonly FlowGraph g;
    readonly Translations texts;
    readonly Dictionary<string, Fact> serverFields = new(StringComparer.Ordinal);
    readonly ILookup<string, Fact> choicesByEnum;

    public Labels(CodeGraph graph, string root) : this(new FlowGraph(graph, root), root) { }

    internal Labels(FlowGraph g, string root)
    {
        this.g = g;
        texts = new Translations(root, resources: true);
        foreach (var f in g.AllFacts("form-field").Where(f => f["via"] == "server"))
        {
            var owner = f.Owner;
            var bar = owner.IndexOf('|', StringComparison.Ordinal);
            serverFields.TryAdd(bar >= 0 ? owner[(bar + 1)..] : owner, f);
        }
        choicesByEnum = g.AllFacts("field-choice").ToLookup(f => f["enum"], StringComparer.Ordinal);
    }

    /// <summary>The words for the first of these <c>|</c>-separated keys a translation file holds; null when none does.</summary>
    public string? Text(string keys)
    {
        var t = texts.Text(keys);
        // D333 (fresh check): a value that is only a reference to another key (transloco's `"cancel": "{{common.cancel}}"`) is that key's words.
        for (var hop = 0; hop < 3 && t is not null && Reference().Match(t.Trim()) is { Success: true } m; hop++)
        {
            if (texts.Text(m.Groups[1].Value) is not { } next) break;
            t = next;
        }
        // A reference to a key no file holds is no words (the user sees the braces).
        return t is not null && Reference().IsMatch(t.Trim()) ? null : Clean(t);
    }

    /// <summary>
    /// A key's words, else the key as written when no file holds it and it reads as words, not as a key (<c>Language Profiles</c>,
    /// <c>URL</c>): what translation libraries show for a key they don't have (D333). <paramref name="written"/> says which.
    /// </summary>
    string? KeyWords(string keys, out bool written)
    {
        written = false;
        if (Text(keys) is { } t) return t;
        foreach (var key in keys.Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (!LooksLikeKey(key.Trim()) && Words(key) is { } w) { written = true; return w; }
        return null;
    }

    // ---- buttons ---------------------------------------------------------------------------------------------------------

    /// <summary>Events a user sets off by pressing something: what the share of "buttons" counts (D333).</summary>
    public static bool IsButton(Fact uiEvent) => uiEvent["event"].ToLowerInvariant() is "click" or "submit" or "onclick" or "onsubmit" or "onpress" or "dblclick" or "ondoubleclick";

    /// <summary>A <c>ui-event</c>'s name: its text keys, else its words as written, else ❓ with the handler's short name.</summary>
    public Named Button(Fact uiEvent) => Button(uiEvent, 0);

    Named Button(Fact uiEvent, int depth)
    {
        var handler = uiEvent["handler"];
        var code = g.Nodes.TryGetValue(handler, out var node) ? FlowGraph.ShortName(node) : handler is "" or "inline" ? uiEvent["element"] : CodeName(handler);
        if (KeyWords(uiEvent["keys"], out var asWritten) is { } k) return new(k, null, code, asWritten ? "written" : "key");
        if (Words(uiEvent["text"]) is { } t) return new(t, null, code, "text");
        // D333: a handler handed to a component's prop (`<LogFiles onRefreshPress={…}>`) takes the words of that component's own button
        // whose handler is the prop (`onPress={onRefreshPress}`, labelled there), the component found by what the owner renders; two levels.
        if (depth < 2 && uiEvent["event"] is { Length: > 0 } prop)
            foreach (var child in g.Out(uiEvent.Owner, EdgeKind.Renders).Select(e => e.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!g.Nodes.TryGetValue(child, out var c) || FlowGraph.ShortName(c) != uiEvent["element"]) continue;
                foreach (var inner in g.Facts(child, "ui-event").Where(x => x["handler"] == "prop:" + prop).OrderBy(x => x.Line))
                    if (Button(inner, depth + 1) is { Visible: true } named) return named with { Code = code, Source = "prop" };
            }
        return new(null, null, code, "", uiEvent["keys"] != "" ? $"no visible text found (key {uiEvent["keys"]} is in no translation file)" : "no visible text found");
    }

    // ---- fields ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A <c>form-field</c>'s label and help: its label keys, else its label words; a field bound with <c>asp-for</c> takes the label and help
    /// of the property it reaches (that property's server <c>form-field</c>, D333); help also from a key beside the label key by the usual
    /// suffixes (<c>.Hint</c>, <c>Hint</c>, <c>HelpText</c>, <c>.HelpText</c>) when the field has none of its own.
    /// </summary>
    public Named Field(Fact field)
    {
        var code = field["via"] == "server" ? field["bind"] : field["to"] is { Length: > 0 } to ? to : field["id"] is { Length: > 0 } id ? id : field["element"];
        var server = field["via"] != "server" && field["property"] is { Length: > 0 } p && serverFields.TryGetValue(p, out var sf) ? sf : null;
        string? words = null, source = "";
        if (KeyWords(field["label"], out var asWritten) is { } k) (words, source) = (k, asWritten ? "written" : "key");
        else if (Words(field["labelText"]) is { } t) (words, source) = (t, "text");
        else if (server is not null && KeyWords(server["label"], out _) is { } m) (words, source) = (m, "model");
        var help = Text(field["help"]) ?? Words(field["helpText"])
            ?? (server is not null ? Text(server["help"]) : null)
            ?? HelpBeside(field["label"] != "" ? field["label"] : server?["label"] ?? "");
        if (words is not null) return new(words, help, code, source);
        var key = field["label"] != "" ? field["label"] : server?["label"] ?? "";
        return new(null, help, code, "", key != "" ? $"no visible text found (key {key} is in no translation file)" : "no visible text found");
    }

    string? HelpBeside(string labelKeys)
    {
        foreach (var key in labelKeys.Split('|', StringSplitOptions.RemoveEmptyEntries))
            foreach (var suffix in HelpSuffixes)
                if (Text(key + suffix) is { } h) return h;
        return null;
    }

    static readonly string[] HelpSuffixes = [".Hint", "Hint", "HelpText", ".HelpText"];

    /// <summary>A field's choices by visible names: an enum's members through their <c>field-choice</c> labels; a constant list's items through the lookup.</summary>
    public Choices ChoicesOf(Fact field)
    {
        var raw = field["choices"];
        if (raw == "" && field["property"] is { Length: > 0 } p && serverFields.TryGetValue(p, out var sf)) raw = sf["choices"];
        if (raw == "") return Choices.None;
        var colon = raw.IndexOf(':', StringComparison.Ordinal);
        var (kind, value) = colon < 0 ? (raw, "") : (raw[..colon], raw[(colon + 1)..]);
        switch (kind)
        {
            case "enum":
                var members = choicesByEnum[value].Concat(choicesByEnum.Where(x => x.Key.EndsWith("." + value, StringComparison.Ordinal)).SelectMany(x => x))
                    .Distinct().OrderBy(f => int.TryParse(f["value"], out var v) ? v : int.MaxValue).ThenBy(f => f.Line).ToList();
                if (members.Count == 0) return new([], kind, $"the members of {value} (no label on them)");
                return new([.. members.Select(f => KeyWords(f["label"], out _) is { } w ? new Named(w, Text(f["help"]) ?? Words(f["help"]), f["member"], "key")
                    : new Named(null, Text(f["help"]) ?? Words(f["help"]), f["member"], "", f["label"] != "" ? $"no visible text found (key {f["label"]} is in no translation file)" : "no visible text found"))], kind, "");
            case "list":
                // A placeholder option ("---", "") is no choice; a list of placeholders only has its real options made at runtime.
                var items = value.Split('|').Where(i => i.Any(char.IsLetterOrDigit)).ToList();
                if (items.Count == 0) return new([], "runtime", "only a placeholder option; the rest are made at runtime");
                return new([.. items.Select(item => Text(item) is { } w ? new Named(w, null, item, "key")
                    : LooksLikeKey(item) ? new Named(null, null, item, "", "no visible text found") : new Named(item, null, item, "text"))], kind, "");
            default:
                return new([], kind, kind switch { "runtime" => $"made at runtime ({value})", "server" => $"from the server ({value})", "type" => $"supplied by {value}", _ => raw });
        }
    }

    // ---- screens ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A screen's title with its route beside (D327 item 2, D333), in order: the route's own <c>title</c>; for a page, the screen component's
    /// outermost titled element, then its top heading; for an MVC action, its view's title slot (<c>ViewBag</c>/<c>ViewData</c>), then the
    /// view's top heading. <see cref="Named.Code"/> is the route (a page's path; an action's route, else its name).
    /// </summary>
    public Named Screen(string point)
    {
        if (!g.Nodes.TryGetValue(point, out var node)) return new(null, null, point, "");
        var mvc = node.Kind != NodeKind.Page;
        var route = mvc ? g.In(point, EdgeKind.RoutesTo).Select(e => g.Name(e.Source)).Order(StringComparer.Ordinal).FirstOrDefault() ?? node.Name : node.Name;
        var candidates = new List<(Fact Fact, int Rank)>();
        if (!mvc)
        {
            foreach (var f in g.Facts(point, "screen-title")) candidates.Add((f, 0));
            foreach (var target in g.Out(point, EdgeKind.RoutesTo).Select(e => e.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                foreach (var owner in Own(target))
                    foreach (var f in g.Facts(owner, "screen-title")) candidates.Add((f, Rank(f)));
                // A screen component that wraps a body in providers (Sonarr's SeriesIndex → SeriesIndexBody): a titled wrapping element
                // in what it renders, two levels down, ranks after its own; headings there are a part's, not the screen's.
                var seen = new HashSet<string>(StringComparer.Ordinal) { target };
                var level = new List<string> { target };
                for (var depth = 1; depth <= 2; depth++)
                {
                    level = [.. level.SelectMany(c => g.Out(c, EdgeKind.Renders)).Select(e => e.Target).Where(t => !t.EndsWith(".template", StringComparison.Ordinal) && seen.Add(t)).Order(StringComparer.Ordinal)];
                    foreach (var child in level)
                        foreach (var owner in Own(child))
                            foreach (var f in g.Facts(owner, "screen-title").Where(f => f["via"] == "title-attribute")) candidates.Add((f, 10 * depth + Rank(f)));
                }
            }
        }
        else
            foreach (var view in Views(point))
                foreach (var f in g.Facts(view, "screen-title")) candidates.Add((f, Rank(f)));
        foreach (var (f, rank) in candidates.OrderBy(c => c.Rank).ThenBy(c => c.Fact.File, StringComparer.Ordinal).ThenBy(c => c.Fact.Line))
        {
            if (f["via"] == "route")
            {
                if (Text(f["title"]) is { } t) return new(t, null, route, "route");
                if (!LooksLikeKey(f["title"]) && Words(f["title"]) is { } w) return new(w, null, route, "route");
                continue;
            }
            if (TitleText(f) is { } k) return new(k, null, route, f["via"] == "heading" ? "heading" : "title");
        }
        return new(null, null, route, "");
    }

    /// <summary>A heading's or titled element's words: its keys (the form without a placeholder first), else its words as written.</summary>
    string? TitleText(Fact f)
    {
        // A heading written in branches gives several keys: the words without a placeholder (`{0}`) are its plain form.
        var alternatives = f["label"].Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Text).OfType<string>().ToList();
        return (alternatives.FirstOrDefault(a => !Placeholder().IsMatch(a)) ?? alternatives.FirstOrDefault()) ?? Words(f["labelText"]);
    }

    /// <summary>
    /// A part's own title (D327, D334): from its own markup's <c>screen-title</c> facts (a titled element or title slot first, then
    /// the highest heading), through the same lookup as screens; else <see cref="Named.Words"/> null and the caller says its code name.
    /// </summary>
    public Named PartTitle(string part, string code)
    {
        var facts = Own(part).SelectMany(o => g.Facts(o, "screen-title")).Where(f => f["via"] != "route")
            .OrderBy(Rank).ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line);
        foreach (var f in facts)
            if (TitleText(f) is { } t) return new(t, null, code, f["via"] == "heading" ? "heading" : "title");
        return new(null, null, code, "", "no title in its own markup");
    }

    /// <summary>
    /// A menu entry's name (<c>menu-item</c>, D221, D332): its key through the lookup, else its name (a key, or words as written);
    /// help from its <c>help</c> key or words. <see cref="Named.Code"/> is its name as written, else its key, else its handler's short name.
    /// </summary>
    public Named MenuEntry(Fact item)
    {
        var name = item["name"];
        var code = name != "" ? name : item["key"] != "" ? item["key"] : HandlerName(item["handler"]) ?? "menu entry";
        var h = item["help"];
        var help = h == "" ? null : Text(h) ?? (LooksLikeKey(h) ? null : Words(h));
        if (item["key"] != "" && KeyWords(item["key"], out var written) is { } k) return new(k, help, code, written ? "written" : "key");
        if (name != "")
        {
            if (Text(name) is { } t) return new(t, help, code, "key");
            if (!LooksLikeKey(name) && Words(name) is { } w) return new(w, help, code, "text");
        }
        var key = item["key"] != "" ? item["key"] : name;
        return new(null, help, code, "", key != "" ? $"no visible text found (key {key} is in no translation file)" : "no visible text found");
    }

    /// <summary>
    /// A handler's short name, for the skip list (D334 item 5): a point id's member (<c>ts|a.ts#A.close</c> → <c>close</c>), a prop
    /// (<c>prop:onClose</c> → <c>onClose</c>), a data entry's first property (<c>callback=this.dismiss|action=…</c> → <c>dismiss</c>);
    /// null for none, or a function written in place.
    /// </summary>
    public static string? HandlerName(string handler)
    {
        if (handler is "" or "inline") return null;
        var first = handler;
        if (!first.Contains('#', StringComparison.Ordinal))
        {
            first = first.Split('|')[0];
            var eq = first.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0) first = first[(eq + 1)..];
            if (first is "" or "inline") return null;
            var paren = first.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0) first = first[..paren];
            first = first.Replace("prop:", "", StringComparison.Ordinal);
            return first[(first.LastIndexOf('.') + 1)..] is { Length: > 0 } last ? last : null;
        }
        return CodeName(handler);
    }

    /// <summary>Visible words as the skip list reads them: the name's words, else what the markup writes even with no letter (<c>×</c>).</summary>
    public static string? Shown(Named name, string writtenText) => name.Words ?? Clean(writtenText);

    /// <summary>A component and its Angular template, where its own markup's facts sit.</summary>
    IEnumerable<string> Own(string component) =>
        g.Out(component, EdgeKind.Renders).Select(e => e.Target).Where(t => t.EndsWith(".template", StringComparison.Ordinal)).Prepend(component);

    /// <summary>A title written for the purpose before a heading; a higher heading before a lower one.</summary>
    static int Rank(Fact f) => f["via"] switch { "route" => 0, "view-data" or "title-attribute" => 1, _ => 1 + (int.TryParse(f["level"], out var l) ? l : 6) };

    IEnumerable<string> Views(string action)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in g.Out(action, EdgeKind.Renders).Where(e => e.Target.StartsWith("view:", StringComparison.Ordinal)).OrderBy(e => e.Target, StringComparer.Ordinal))
            if (seen.Add(e.Target)) yield return e.Target;
    }

    /// <summary>A screen's line as D327 writes it: "All Series (/all-series)", or the route alone with ❓.</summary>
    public static string ScreenLine(Named screen) => screen.Visible ? $"{screen.Words} ({screen.Code})" : screen.Line;

    // ---- words -----------------------------------------------------------------------------------------------------------

    /// <summary>Words as written, when they hold a letter; whitespace folded.</summary>
    static string? Words(string text)
    {
        var t = Clean(text);
        return t is not null && t.Any(char.IsLetter) ? t : null;
    }

    static string? Clean(string? text)
    {
        if (text is null) return null;
        var t = Spaces().Replace(text, " ").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// A value written as a text key rather than words (D333): one token with a dot, dash or underscore between letters
    /// (<c>title.all-series</c>, <c>Admin.Common.Save</c>), or several capitalised humps run together (<c>IndexerSettingsApiPath</c>).
    /// Used only where a value may be either: a route's title, a list's item.
    /// </summary>
    /// D336: capitalised words joined by dashes (<c>Multi-Episode</c>, <c>Read-Only</c>) read as words: keys are lowercase-dashed, dotted, or run together.
    public static bool LooksLikeKey(string value) => !value.Contains(' ', StringComparison.Ordinal) && KeyLike().IsMatch(value) && !DashedWords().IsMatch(value);

    [GeneratedRegex(@"^[A-Z][a-z]+(?:-[A-Z][a-z]+)+$")]
    private static partial Regex DashedWords();

    /// <summary>A point id's short name, also from a list of several (<c>ts|a.ts#A.f|ts|b.ts#B.g</c> → <c>f</c>) or a prop handler.</summary>
    static string CodeName(string handler)
    {
        var h = handler.Replace("prop:", "", StringComparison.Ordinal);
        var hash = h.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0) return h;
        var end = h.IndexOf('|', hash);
        var member = end < 0 ? h[(hash + 1)..] : h[(hash + 1)..end];
        var paren = member.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0) member = member[..paren];
        return member[(member.LastIndexOf('.') + 1)..];
    }

    [GeneratedRegex(@"^\{\{\s*([A-Za-z0-9_.-]+)\s*\}\}$")]
    private static partial Regex Reference();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?:[A-Za-z0-9]+[._-][A-Za-z0-9._-]*[A-Za-z0-9]|(?:[A-Z][a-z0-9]+){2,}[A-Za-z0-9]*)$")]
    private static partial Regex KeyLike();
}
