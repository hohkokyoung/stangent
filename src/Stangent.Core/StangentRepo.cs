using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Stangent.Core;

/// <summary><c>stangent.yaml</c>, at the root of a repo stangent works on.</summary>
public sealed class StangentConfig
{
    public int Version { get; set; } = 1;
    public StoreConfig Store { get; set; } = new();
    public AiConfig Ai { get; set; } = new();
    /// <summary>
    /// Code-graph link detectors to run (D17). Empty means every built-in detector; list names to
    /// run only those; <c>[none]</c> runs none.
    /// </summary>
    public List<string> Detectors { get; set; } = [];
    /// <summary>Per-detector settings, e.g. <c>publish-handle: { handlerInterfaces: [...] }</c>.</summary>
    public Dictionary<string, Dictionary<string, List<string>>> DetectorSettings { get; set; } = [];
    public AdaptersConfig Adapters { get; set; } = new();
    /// <summary>📜 "usually changes together" evidence from git history (D35–D42).</summary>
    public HistoryConfig History { get; set; } = new();

    public sealed class StoreConfig
    {
        public string Root { get; set; } = ".stangent/store";
    }

    public sealed class AiConfig
    {
        /// <summary>False turns off every AI step; only code-only parts run (D24).</summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// For each file, its last <see cref="Window"/> commits that survive the noise filters; a partner
    /// is 📜 when it changed in at least <see cref="MinShared"/> of them and <see cref="MinShare"/> of them (D35).
    /// </summary>
    public sealed class HistoryConfig
    {
        public bool Enabled { get; set; } = true;
        public int Window { get; set; } = 20;
        public int MinShared { get; set; } = 3;
        public double MinShare { get; set; } = 0.3;
        /// <summary>Commits touching more files on the map than this are dropped (D37).</summary>
        public int MaxFiles { get; set; } = 30;
        /// <summary>Bot authors (name or email) beyond those ending in <c>[bot]</c> (D37).</summary>
        public List<string> BotAuthors { get; set; } = [];
        /// <summary>Join a moved file's old history to its current path (D38).</summary>
        public bool FollowRenames { get; set; } = true;
    }

    /// <summary>Reserved in Phase 0; each adapter is built by the phase that first needs it (D24).</summary>
    public sealed class AdaptersConfig
    {
        public string? Tickets { get; set; }
        public string? CodeHost { get; set; }
        public string? Ci { get; set; }
        public string? Archive { get; set; }
    }
}

/// <summary>A repo stangent has been initialised in, and where its files live.</summary>
public sealed class StangentRepo
{
    public const string ConfigFile = "stangent.yaml";

    /// <summary>Lines <c>stangent init</c> adds to the repo's .gitignore: derived and run data, never committed (D7, D21).</summary>
    public static readonly string[] GitIgnoreLines = [".stangent/index.sqlite*", ".stangent/telemetry/"];

    static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    StangentRepo(string root, StangentConfig config)
    {
        Root = root;
        Config = config;
    }

    public string Root { get; }
    public StangentConfig Config { get; }
    public string StoreRoot => Path.GetFullPath(Path.Combine(Root, Config.Store.Root));
    public string IndexPath => Path.Combine(Root, ".stangent", "index.sqlite");
    public string TelemetryDir => Path.Combine(Root, ".stangent", "telemetry");

    /// <summary>Identifies the repo in telemetry without revealing its path.</summary>
    public string Id => CanonicalJson.Hash(Root)[..16];

    /// <summary>Finds the nearest <c>stangent.yaml</c> at or above <paramref name="startDir"/>.</summary>
    public static StangentRepo Discover(string startDir) =>
        TryDiscover(startDir) ?? throw new StangentException("not_initialised",
            $"No {ConfigFile} at or above {startDir}. Run `stangent init` first.", ExitCodes.Usage);

    public static StangentRepo? TryDiscover(string startDir)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(startDir)); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ConfigFile);
            if (File.Exists(path)) return new StangentRepo(dir.FullName, Load(path));
        }
        return null;
    }

    /// <summary>
    /// Writes a default <c>stangent.yaml</c> (never overwrites one), creates the store folder and
    /// adds the git-ignore lines. Safe to run twice.
    /// </summary>
    public static (StangentRepo Repo, bool Created) Init(string dir)
    {
        var root = Path.GetFullPath(dir);
        if (!Directory.Exists(root))
            throw new StangentException("not_found", $"Directory {root} does not exist.", ExitCodes.NotFound);

        var configPath = Path.Combine(root, ConfigFile);
        var created = !File.Exists(configPath);
        if (created) File.WriteAllText(configPath, Writer.Serialize(new StangentConfig()));

        var repo = new StangentRepo(root, Load(configPath));
        Directory.CreateDirectory(repo.StoreRoot);
        AddGitIgnoreLines(Path.Combine(root, ".gitignore"));
        return (repo, created);
    }

    static StangentConfig Load(string path)
    {
        try
        {
            var config = Reader.Deserialize<StangentConfig?>(File.ReadAllText(path)) ?? new StangentConfig();
            if (config.Version != 1)
                throw new StangentException("unsupported_config", $"{path}: version {config.Version} is not supported (expected 1).");
            return config;
        }
        catch (YamlException e)
        {
            throw new StangentException("invalid_config", $"{path}: {e.Message}");
        }
    }

    static void AddGitIgnoreLines(string path)
    {
        var existing = File.Exists(path) ? File.ReadAllLines(path) : [];
        var missing = GitIgnoreLines.Where(l => !existing.Contains(l)).ToList();
        if (missing.Count == 0) return;
        var prefix = existing.Length > 0 && !File.ReadAllText(path).EndsWith('\n') ? "\n" : "";
        File.AppendAllText(path, prefix + string.Join("\n", ["# stangent: derived and run data", .. missing]) + "\n");
    }
}

/// <summary>The gate every AI step calls first (D24).</summary>
public static class AiPolicy
{
    public static void EnsureAllowed(StangentConfig config, string step)
    {
        if (!config.Ai.Enabled)
            throw new StangentException("ai_disabled",
                $"AI is switched off for this repo (ai.enabled: false), so '{step}' can't run.", ExitCodes.AiDisabled);
    }
}
