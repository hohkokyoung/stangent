"""
Stangent initializer.

TWO MODES:

  Global install (run ONCE, ever):
      python init.py --global
      Installs agents and commands into ~/.claude/ so they appear in
      every project automatically. No per-project setup needed for visibility.

  Project init (run once per project):
      python init.py
      Creates config.json + .stangent/ scaffolding for the current project.
      Agents must already be globally installed (or will be installed locally).

  Both at once:
      python init.py --global           # installs globally
      cd your-project && python init.py # then scaffold the project

  Remove from a project:
      python init.py --uninit           # remove tooling, keep .stangent/ data
      python init.py --uninit --hard    # remove everything including .stangent/

Options:
    --global    Install agents/commands to ~/.claude/ (user-level, all projects)
    --uninit    Remove Stangent from the current project (keeps feature data)
    --hard      Used with --uninit: also deletes .stangent/ and all feature history
    --profile   Override auto-detected profile (python | flutter)
    --dry-run   Show what would be done without writing anything
    --verify    Only run environment validation, no scaffolding
"""

import argparse
import json
import sys
from pathlib import Path

from init_constants import (
    STANGENT_PATH, VERSION, PROFILES, PROVIDERS,
    GLOBAL_AGENTS_DIR,
    C, ok, fail, warn, info, header,
)
from init_env import (
    check_python_version, check_git, check_credentials,
    detect_provider, detect_profiles, check_tools,
)
from init_config import build_config, validate_config
from init_scaffold import (
    create_stangent_dirs, init_registry,
    install_global, uninit_project,
    copy_profiles, copy_templates, copy_prompts, copy_gateway,
    write_settings_json, copy_commands, copy_claude_agents,
    create_srs, create_decisions, create_memory, create_env_example,
    update_gitignore, create_onboarding_doc,
    configure_dbhub, configure_supabase, setup_cross_stack_meta,
)


def run(args):
    project_root = Path.cwd()
    dry_run = args.dry_run
    verify_only = args.verify

    if dry_run:
        print(f"\n{C.WARN} DRY RUN — no files will be written\n")

    # ── Global install (short-circuits everything else) ────────────────────
    if getattr(args, "global_install", False):
        install_global(dry_run)
        return

    # ── Uninit (short-circuits everything else) ────────────────────────────
    if getattr(args, "uninit", False):
        hard = getattr(args, "hard", False)
        if not dry_run and not hard:
            print(f"\n{C.WARN} This will remove Stangent tooling from {project_root.name}.")
            print("  Your .stangent/ directory (features, SRS, decisions) will be kept.")
            print("  Use --hard to also delete .stangent/ and all feature history.\n")
            confirm = input("  Proceed? (yes/no): ").strip().lower()
            if confirm != "yes":
                print("  Uninit cancelled.")
                return
        elif not dry_run and hard:
            print(f"\n{C.WARN} WARNING — this will delete .stangent/ and ALL feature history in {project_root.name}.")
            confirm = input("  Type 'yes, delete everything' to confirm: ").strip().lower()
            if confirm != "yes, delete everything":
                print("  Uninit cancelled.")
                return
        uninit_project(project_root, hard=hard, dry_run=dry_run)
        return

    # ── 1. Environment checks ──────────────────────────────────────────────

    header("Environment Checks")
    env_ok = True
    env_ok &= check_python_version()
    env_ok &= check_git()

    # ── 1b. Provider detection + credential check ──────────────────────────

    header("Provider")

    if args.provider:
        if args.provider not in PROVIDERS:
            fail(f"Unknown provider: {args.provider}. "
                 f"Valid: {', '.join(PROVIDERS)}")
            sys.exit(1)
        provider_name = args.provider
        ok(f"Provider: {PROVIDERS[provider_name]['display']} (from --provider flag)")
    else:
        # Try existing config.json first, then env var auto-detect
        existing_config = project_root / ".stangent" / "config.json"
        if not existing_config.exists():
            existing_config = project_root / "config.json"  # legacy fallback
        if existing_config.exists():
            try:
                cfg_data = json.loads(existing_config.read_text(encoding="utf-8"))
                provider_name = cfg_data.get("provider", {}).get("name") or \
                                cfg_data.get("provider") or "anthropic"
                if isinstance(provider_name, str) and provider_name in PROVIDERS:
                    ok(f"Provider: {PROVIDERS[provider_name]['display']} (from config.json)")
                else:
                    provider_name = None
            except Exception:
                provider_name = None
        else:
            provider_name = None

        if not provider_name:
            provider_name = detect_provider()
            if provider_name:
                ok(f"Provider: {PROVIDERS[provider_name]['display']} (auto-detected)")
            else:
                warn("Could not auto-detect provider.")
                print("\n  Supported providers:")
                for name, p in PROVIDERS.items():
                    print(f"    {name:<12} — {p['display']}")
                provider_name = input("\n  Enter provider name: ").strip().lower()
                if provider_name not in PROVIDERS:
                    fail(f"Unknown provider: {provider_name}")
                    sys.exit(1)

    env_ok &= check_credentials(provider_name)

    # ── 2. Profile detection ───────────────────────────────────────────────

    header("Profile Detection")

    if args.profile:
        # Accept comma-separated: --profile python,flutter
        requested = [p.strip().lower() for p in args.profile.split(",") if p.strip()]
        unknown = [p for p in requested if p not in PROFILES]
        if unknown:
            fail(f"Unknown profile(s): {', '.join(unknown)}. "
                 f"Valid: {', '.join(PROFILES)}")
            sys.exit(1)
        profile_names = requested
        ok(f"Profile(s): {', '.join(profile_names)} (from --profile flag)")
    else:
        profile_names = detect_profiles(project_root)
        if profile_names:
            ok(f"Profile(s): {', '.join(profile_names)} (auto-detected)")
        else:
            warn("Could not auto-detect profile.")
            print("\n  Supported profiles:")
            for name in PROFILES:
                print(f"    {name}")
            print("  You can enter multiple, comma-separated (e.g. python,flutter)")
            raw = input("\n  Enter profile name(s): ").strip().lower()
            profile_names = [p.strip() for p in raw.split(",") if p.strip()]
            unknown = [p for p in profile_names if p not in PROFILES]
            if unknown or not profile_names:
                fail(f"Unknown or empty profile(s): {', '.join(unknown or ['(none)'])}")
                sys.exit(1)

    # ── 3. Tool checks ─────────────────────────────────────────────────────

    all_tools_ok  = True
    all_missing: list[str] = []
    checked_tools: set[str] = set()   # shared across profiles — deduplicates shared tools

    for pname in profile_names:
        header(f"Tool Checks ({pname})")
        t_ok, missing = check_tools(pname, checked_tools)
        if not t_ok:
            all_tools_ok = False
            all_missing.extend(f"{pname}:{m}" for m in missing)

    if not all_tools_ok:
        print(f"\n  Missing required tools: {', '.join(all_missing)}")
        print("  See profiles/<name>.md for install instructions.")
        if not verify_only:
            proceed = input("\n  Continue anyway? Some sub-agents may fail. (yes/no): ").strip().lower()
            if proceed != "yes":
                sys.exit(1)

    if verify_only:
        header("Verification Complete")
        if env_ok and all_tools_ok:
            ok("All checks passed.")
        else:
            warn("Some checks failed. See above.")
        return

    # ── 4. Scaffolding ─────────────────────────────────────────────────────

    header("Project Scaffolding")

    # .stangent/ must exist before we write config inside it
    create_stangent_dirs(project_root, dry_run)

    # Validate profile .md files exist before generating config.
    # Agents read these at runtime — a missing profile causes silent failures.
    profiles_ok = True
    for pname in profile_names:
        profile_md = STANGENT_PATH / "profiles" / f"{pname}.md"
        if profile_md.exists():
            ok(f"profiles/{pname}.md — found")
        else:
            fail(f"profiles/{pname}.md — NOT FOUND in stangent source")
            print(f"  Expected: {profile_md}")
            print(f"  This profile is missing from the stangent installation.")
            print(f"  Agents will fail at runtime when they try to read it.")
            profiles_ok = False
    if not profiles_ok:
        proceed = input("\n  Profile files missing. Continue anyway? (yes/no): ").strip().lower()
        if proceed != "yes":
            sys.exit(1)

    config      = build_config(project_root, profile_names, provider_name)
    config["stangent_source_path"] = str(STANGENT_PATH)
    config_path = project_root / ".stangent" / "config.json"

    # Migration: old stangent used config.json at project root
    old_config_path = project_root / "config.json"
    if old_config_path.exists() and not config_path.exists():
        warn("Found legacy config.json at project root — migrating to .stangent/config.json")
        if not dry_run:
            config_path.write_text(old_config_path.read_text(encoding="utf-8"), encoding="utf-8")
            old_config_path.unlink()
        info("Migrated — you can delete config.json from your project root")

    if config_path.exists():
        existing = json.loads(config_path.read_text(encoding="utf-8"))
        old_version = existing.get("_stangent_version", "0.0.0")

        # ── Always overwrite: structural fields the user never edits ─────────
        existing.pop("profile", None)       # removed in favour of profiles[0]
        existing.pop("stangent_path", None) # removed — projects are self-contained
        existing["_stangent_version"]    = VERSION
        existing["stangent_source_path"] = str(STANGENT_PATH)
        existing["profiles"]             = config["profiles"]
        existing["profile_roots"]        = config["profile_roots"]

        # Detect provider change — if provider switched, reset models to the
        # new provider's defaults (old model IDs are incompatible).
        old_provider_name = (existing.get("provider") or {}).get("name", "")
        provider_changed  = old_provider_name != provider_name
        existing["provider"] = config["provider"]
        if provider_changed and old_provider_name:
            existing["models"] = config["models"]
            warn(f"Provider changed ({old_provider_name} → {provider_name}) — "
                 f"model names reset to {provider_name} defaults")

        # ── Deep merge: add new keys from template, keep user values ─────────
        # For each dict section in the fresh config, copy over any keys that
        # are missing from the existing config. User-set values are untouched.
        # Skip models if we just reset them due to a provider change.
        new_keys: list[str] = []
        sections_to_merge = ("pipeline", "paths", "feature_id", "integrations") \
                            + (() if provider_changed else ("models",))
        for section in sections_to_merge:
            fresh_section = config.get(section, {})
            if section not in existing:
                existing[section] = fresh_section
                new_keys.append(section)
            else:
                for key, value in fresh_section.items():
                    if key not in existing[section]:
                        existing[section][key] = value
                        new_keys.append(f"{section}.{key}")

        # ── Key renames: migrate old names to new ones ────────────────────────
        # Format: (section, old_key, new_key)
        _RENAMES: list[tuple[str, str, str]] = [
            ("pipeline", "auto_pr_on_complete", "remind_pr_on_complete"),
        ]
        renamed: list[str] = []
        for section, old_key, new_key in _RENAMES:
            sec = existing.get(section, {})
            if old_key in sec and new_key not in sec:
                sec[new_key] = sec.pop(old_key)
                renamed.append(f"{section}.{old_key} → {new_key}")

        config = existing
        if not dry_run:
            config_path.write_text(json.dumps(config, indent=2))

        if provider_changed and old_provider_name:
            ok(f".stangent/config.json — provider switched "
               f"({old_provider_name} → {provider_name}), models reset to defaults")
        elif new_keys or renamed or old_version != VERSION:
            details = []
            if new_keys:
                details.append(f"added: {', '.join(new_keys)}")
            if renamed:
                details.append(f"renamed: {', '.join(renamed)}")
            ok(f".stangent/config.json — upgraded v{old_version} → v{VERSION}"
               + (f" ({'; '.join(details)})" if details else ""))
        else:
            ok(f".stangent/config.json — up to date ({provider_name}, {', '.join(profile_names)})")
    else:
        if not dry_run:
            config_path.write_text(json.dumps(config, indent=2))
        info(f".stangent/config.json — created ({provider_name}, {', '.join(profile_names)})")

    missing_fields = validate_config(config)
    if missing_fields:
        for f in missing_fields:
            warn(f"config.json — missing required field: {f}")
        warn("Config is incomplete. Some agents may fail. Re-run init to repair.")

    configure_dbhub(config, config_path, project_root, dry_run)
    configure_supabase(config, config_path, profile_names, dry_run)
    setup_cross_stack_meta(project_root, profile_names, dry_run)
    init_registry(project_root, config, dry_run)
    copy_profiles(project_root, dry_run)
    copy_templates(project_root, dry_run)
    copy_prompts(project_root, dry_run)
    copy_gateway(project_root, dry_run)
    write_settings_json(project_root, dry_run)
    copy_commands(project_root, config, dry_run)
    copy_claude_agents(project_root, dry_run)
    create_srs(project_root, config, dry_run)
    create_decisions(project_root, config, dry_run)
    create_memory(project_root, config, dry_run)
    create_env_example(project_root, dry_run)
    update_gitignore(project_root, dry_run)
    create_onboarding_doc(project_root, config, dry_run)

    # ── 5. Summary ─────────────────────────────────────────────────────────

    header("Done")

    global_installed = GLOBAL_AGENTS_DIR.exists() and any(GLOBAL_AGENTS_DIR.glob("stangent*.md"))
    global_hint = (
        "" if global_installed else
        f"\n  Tip: Run 'python {STANGENT_PATH}/init.py --global' once to make\n"
        "  agents available in ALL projects without per-project init.\n"
    )

    roots_display = "  ".join(
        f"{n}: {r}" for n, r in config["profile_roots"].items()
    )
    print(f"""
  Project:   {project_root.name}
  Profiles:  {', '.join(config['profiles'])}
  Roots:     {roots_display}
{global_hint}
  Open Claude Code and use the Stangent agent from the mode selector,
  or type /feature <describe what you want to build>

  See .stangent/HOW_THIS_WORKS.md for full documentation.
""")

    if not env_ok or not all_tools_ok:
        warn("Some environment checks failed. Fix them before running the pipeline.")


def main():
    parser = argparse.ArgumentParser(
        description="Initialize Stangent — global install or per-project scaffold.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--global", dest="global_install", action="store_true",
        help=(
            "Install agents and commands to ~/.claude/ so they appear in "
            "every project without per-project init. Run once, ever."
        ),
    )
    parser.add_argument(
        "--uninit", action="store_true",
        help=(
            "Remove Stangent tooling from the current project. "
            "Keeps .stangent/ data (features, SRS, decisions) intact. "
            "Add --hard to also delete .stangent/ and all feature history."
        ),
    )
    parser.add_argument(
        "--hard", action="store_true",
        help="Used with --uninit: also deletes .stangent/ directory and all feature history.",
    )
    parser.add_argument(
        "--provider",
        help=(
            "LLM provider to use. "
            "Options: anthropic | openai | bedrock | vertex. "
            "Auto-detected from environment if not set."
        ),
    )
    parser.add_argument(
        "--profile",
        help="Override auto-detected profile(s). Single: python  Multiple: python,flutter",
    )
    parser.add_argument("--dry-run", action="store_true",
                        help="Show what would be done without writing anything")
    parser.add_argument("--verify", action="store_true",
                        help="Only run environment validation, no scaffolding")
    args = parser.parse_args()
    run(args)


if __name__ == "__main__":
    main()
