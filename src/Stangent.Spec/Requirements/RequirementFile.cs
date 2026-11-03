using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Requirements;

/// <summary>
/// The structured spec's files (D99, contract <c>requirements</c> v1). Under <c>specs/requirements/</c>: <c>ids.yaml</c>
/// (each kind's last number, so an id is assigned once and never reused) and <c>runs.json</c>; under <c>&lt;doc&gt;/</c>:
/// one <c>F-&lt;id&gt;.yaml</c> per section, <c>questions.yaml</c>, and two views written by code from those:
/// <c>index.yaml</c> (one line per item, read first) and <c>glossary.yaml</c>. Code writes them all; people change them
/// through approval and the BA's decisions.
/// </summary>
public static class RequirementFile
{
    public const string Schema = "requirements.v1.json";

    public static string Dir(string root) => Path.Combine(root, "specs", "requirements");
    public static string DocDir(string root, string doc) => Path.Combine(Dir(root), doc);
    static string IdsFile(string root) => Path.Combine(Dir(root), "ids.yaml");
    public static string RunsFile(string root) => Path.Combine(Dir(root), "runs.json");
    static string FeatureFile(string root, string doc, string id) => Path.Combine(DocDir(root, doc), id + ".yaml");
    static string QuestionsFile(string root, string doc) => Path.Combine(DocDir(root, doc), "questions.yaml");

    public static FileStream Lock(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".stangent", "req"));
        var path = Path.Combine(root, ".stangent", "req", "files.lock");
        for (var i = 0; ; i++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(50); }
        }
    }

    public static List<JsonObject> Features(string root, string doc)
    {
        var dir = DocDir(root, doc);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.EnumerateFiles(dir, "F-*.yaml").Order(StringComparer.Ordinal).Select(f => YamlForm.Parse(File.ReadAllText(f), Path.GetFileName(f)))];
    }

    public static IEnumerable<string> Docs(string root) => Directory.Exists(Dir(root))
        ? Directory.EnumerateDirectories(Dir(root)).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal) : [];

    public static List<JsonObject> Questions(string root, string doc) => File.Exists(QuestionsFile(root, doc))
        ? [.. YamlForm.Parse(File.ReadAllText(QuestionsFile(root, doc)), "questions.yaml")["questions"]!.AsArray().OfType<JsonObject>()] : [];

    public static void Save(string root, JsonObject feature)
    {
        var errors = SchemaRegistry.Embedded.Validate(Schema, feature.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_requirements", $"{(string)feature["id"]!} broke requirements v1 (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(FeatureFile(root, (string)feature["doc"]!, (string)feature["id"]!),
            YamlForm.Write(feature, "A feature of the structured spec (requirements v1, D99): built from the FSD, written by code. The BA approves in a spec-only PR."));
    }

    /// <summary>Hands out the next id of a kind; the last number is saved at once, so it's never given twice.</summary>
    public static string NextId(string root, string kind)
    {
        var ids = File.Exists(IdsFile(root)) ? YamlForm.Parse(File.ReadAllText(IdsFile(root)), "ids.yaml") : new JsonObject();
        var next = (int.TryParse((string?)ids[kind], out var n) ? n : 0) + 1;
        ids[kind] = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sorted = new JsonObject(ids.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
        Quotes.WriteAtomic(IdsFile(root), YamlForm.Write(sorted, "The last id given out per kind (D99): assigned once, never reused."));
        return $"{kind}-{next:D4}";
    }

    /// <summary>The section's feature, creating it and its parents as needed: the feature tree follows the FSD's own
    /// section tree, by code (D99).</summary>
    public static JsonObject Ensure(string root, string doc, JsonObject section, SpecIndex index)
    {
        var id = (string)section["id"]!;
        var features = Features(root, doc);
        if (features.FirstOrDefault(f => (string)f["section"]! == id) is { } existing)
        {
            // A run killed between writing a child and its parent left the parent without it.
            if (features.FirstOrDefault(f => (string)f["id"]! == (string?)existing["parent"]) is { } above
                && above["children"]!.AsArray().All(c => (string)c! != (string)existing["id"]!))
            {
                above["children"]!.AsArray().Add((string)existing["id"]!);
                Save(root, above);
            }
            return existing;
        }
        JsonObject? parent = (string?)section["parent"] is { } p && index.Sections.TryGetValue((doc, p), out var ps) ? Ensure(root, doc, ps, index) : null;
        var feature = new JsonObject
        {
            ["version"] = 1, ["id"] = NextId(root, "F"), ["doc"] = doc, ["section"] = id, ["title"] = (string)section["title"]!,
            ["parent"] = (string?)parent?["id"], ["children"] = new JsonArray(), ["textHash"] = null,
            ["requirements"] = new JsonArray(), ["definitions"] = new JsonArray(), ["journeys"] = new JsonArray(), ["background"] = new JsonArray(), ["retired"] = new JsonArray(),
        };
        Save(root, feature);
        if (parent is not null)
        {
            parent["children"]!.AsArray().Add((string)feature["id"]!);
            Save(root, parent);
        }
        return feature;
    }

    /// <summary>The views, rebuilt from the feature files: same content, same bytes.</summary>
    public static void WriteViews(string root, string doc)
    {
        var features = Features(root, doc).OrderBy(f => TraceFile.SectionKey((string)f["section"]!), StringComparer.Ordinal).ThenBy(f => (string)f["id"]!, StringComparer.Ordinal).ToList();
        var index = new StringBuilder("# One line per item (D99): read this first, then `stangent req show <id>`. Written by code.\nversion: 1\nitems:\n");
        var glossary = new JsonArray();
        var any = false;
        void Line(JsonObject o) { index.Append("  - ").Append(YamlForm.Line(o)).Append('\n'); any = true; }
        foreach (var f in features)
        {
            Line(new JsonObject { ["id"] = (string)f["id"]!, ["type"] = "feature", ["section"] = (string)f["section"]!, ["title"] = (string)f["title"]!, ["parent"] = (string?)f["parent"] });
            foreach (var r in f["requirements"]!.AsArray().OfType<JsonObject>())
                Line(new JsonObject { ["id"] = (string)r["id"]!, ["type"] = "requirement", ["feature"] = (string)f["id"]!, ["section"] = (string)f["section"]!, ["title"] = (string)r["title"]!, ["kind"] = (string)r["kind"]!, ["status"] = (string)r["status"]!, ["check"] = (string)r["check"]! });
            foreach (var d in f["definitions"]!.AsArray().OfType<JsonObject>())
            {
                Line(new JsonObject { ["id"] = (string)d["id"]!, ["type"] = "definition", ["feature"] = (string)f["id"]!, ["section"] = (string)f["section"]!, ["title"] = (string)d["name"]!, ["kind"] = (string)d["kind"]!, ["check"] = (string)d["check"]! });
                if ((string)d["kind"]! == "glossary") glossary.Add((JsonNode)new JsonObject { ["id"] = (string)d["id"]!, ["name"] = (string)d["name"]!, ["text"] = (string)d["text"]!, ["feature"] = (string)f["id"]! });
            }
            foreach (var j in f["journeys"]!.AsArray().OfType<JsonObject>())
            {
                Line(new JsonObject { ["id"] = (string)j["id"]!, ["type"] = "journey", ["feature"] = (string)f["id"]!, ["section"] = (string)f["section"]!, ["title"] = (string)j["title"]!, ["check"] = (string)j["check"]! });
                foreach (var s in j["scenarios"]!.AsArray().OfType<JsonObject>())
                    Line(new JsonObject { ["id"] = (string)s["id"]!, ["type"] = "scenario", ["feature"] = (string)f["id"]!, ["section"] = (string)f["section"]!, ["title"] = (string)s["title"]!, ["kind"] = (string)s["kind"]!, ["check"] = (string)s["check"]! });
            }
        }
        foreach (var q in Questions(root, doc))
            Line(new JsonObject { ["id"] = (string)q["id"]!, ["type"] = "question", ["section"] = (string)q["section"]!, ["title"] = (string)q["text"]!, ["status"] = (string)q["status"]! });
        if (!any) index.Replace("items:\n", "items: []\n");
        Quotes.WriteAtomic(Path.Combine(DocDir(root, doc), "index.yaml"), index.ToString());
        Quotes.WriteAtomic(Path.Combine(DocDir(root, doc), "glossary.yaml"),
            YamlForm.Write(new JsonObject { ["version"] = 1, ["terms"] = glossary }, "The FSD's terms (D99): a view of the feature files, written by code."));
    }

    public static void SaveQuestions(string root, string doc, IEnumerable<JsonObject> questions) =>
        Quotes.WriteAtomic(QuestionsFile(root, doc), YamlForm.Write(
            new JsonObject { ["version"] = 1, ["questions"] = new JsonArray([.. questions.Select(q => (JsonNode)q.DeepClone())]) },
            "Questions for the BA (D99): ambiguities and clashes in the FSD, never resolved by the AI."));

    /// <summary>Any item by id, with the feature that holds it; null when there's none.</summary>
    public static JsonObject? Show(string root, string id)
    {
        foreach (var doc in Docs(root))
        {
            foreach (var q in Questions(root, doc).Where(q => (string)q["id"]! == id))
                return new JsonObject { ["type"] = "question", ["doc"] = doc, ["item"] = q.DeepClone() };
            foreach (var f in Features(root, doc))
            {
                if ((string)f["id"]! == id)
                    return new JsonObject
                    {
                        ["type"] = "feature", ["doc"] = doc, ["section"] = (string)f["section"]!, ["title"] = (string)f["title"]!,
                        ["parent"] = (string?)f["parent"], ["children"] = f["children"]!.DeepClone(),
                        ["items"] = new JsonArray([.. f["requirements"]!.AsArray().OfType<JsonObject>().Select(r => (JsonNode)new JsonObject { ["id"] = (string)r["id"]!, ["title"] = (string)r["title"]!, ["status"] = (string)r["status"]! })]),
                    };
                foreach (var (list, type) in new[] { ("requirements", "requirement"), ("definitions", "definition"), ("journeys", "journey") })
                    foreach (var item in f[list]!.AsArray().OfType<JsonObject>())
                    {
                        if ((string)item["id"]! == id)
                        {
                            var found = Found(type, doc, f, item);
                            if (type == "requirement") found["links"] = new JsonArray([.. Links(root, id).Select(l => l.DeepClone())]);
                            return found;
                        }
                        foreach (var s in item["scenarios"]?.AsArray().OfType<JsonObject>() ?? [])
                            if ((string)s["id"]! == id) return Found("scenario", doc, f, s);
                    }
            }
        }
        return null;
    }

    static JsonObject Found(string type, string doc, JsonObject feature, JsonObject item) => new()
    {
        ["type"] = type, ["doc"] = doc, ["feature"] = (string)feature["id"]!, ["section"] = (string)feature["section"]!, ["item"] = item.DeepClone(),
    };

    /// <summary>A requirement's links live in <c>specs/trace.yaml</c>, with their decisions: one fact in one place.</summary>
    public static IEnumerable<JsonObject> Links(string root, string id) =>
        TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["fsd"]?["requirement"] == id);
}
