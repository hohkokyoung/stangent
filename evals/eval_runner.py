"""
Stangent eval harness.
Tests that agent prompt definitions behave correctly.

Usage:
    python evals/eval_runner.py                     # run all evals
    python evals/eval_runner.py --agent planner     # run one agent's evals
    python evals/eval_runner.py --verbose           # show full output

Each eval case is a directory under evals/<agent_name>/:
    case_01_input.md    — the input to the agent
    case_01_expect.md   — what a correct response should contain
    case_01_assert.py   — assertions to run against the response

The runner calls the Anthropic API with the agent's system prompt and
the case input, then runs the assertions against the response.
"""

import argparse
import importlib.util
import json
import os
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable


try:
    import anthropic
except ImportError:
    anthropic = None  # type: ignore

STANGENT_PATH = Path(__file__).parent.parent.resolve()
EVALS_PATH    = Path(__file__).parent.resolve()

# Default models per provider — fast/cheap models preferred for evals.
# Derived from PROVIDERS at import time; fallback dict used if import fails.
def _default_models_from_providers() -> dict[str, str]:
    try:
        from init import PROVIDERS as _p
        return {name: p["default_models"]["fast"] for name, p in _p.items()}
    except Exception:
        return {}

DEFAULT_MODELS: dict[str, str] = _default_models_from_providers() or {
    # Fallback used only when init.py import fails.
    # Use conservative, known-stable model IDs here.
    "anthropic":   "claude-3-5-haiku-20241022",
    "bedrock":     "us.anthropic.claude-3-haiku-20240307-v1:0",
    "vertex":      "claude-3-haiku@20240307",
    "openai":      "gpt-4o-mini",
    "groq":        "llama-3.1-8b-instant",
    "openrouter":  "meta-llama/llama-3.1-8b-instruct:free",
    "ollama":      "qwen2.5-coder:1.5b",
}
DEFAULT_PROVIDER = "anthropic"


# ─── Data classes ────────────────────────────────────────────────────────────

@dataclass
class EvalCase:
    name: str
    agent: str
    input_text: str
    expect_text: str
    assert_module_path: Path | None


@dataclass
class EvalResult:
    case: EvalCase
    response: str
    passed: bool
    failures: list[str] = field(default_factory=list)
    duration_s: float = 0.0
    tokens_in: int = 0
    tokens_out: int = 0


# ─── Provider client factory ─────────────────────────────────────────────────

# Load provider definitions from init.py so we have one source of truth
sys.path.insert(0, str(STANGENT_PATH))
try:
    from init import PROVIDERS as _PROVIDERS
except ImportError:
    _PROVIDERS = {}

# OpenAI SDK is used for all of these
_OPENAI_SDK_PROVIDERS = {
    name for name, p in _PROVIDERS.items() if p.get("sdk") == "openai"
}
# Anthropic SDK variants
_ANTHROPIC_SDK_PROVIDERS = {"anthropic"}
_BEDROCK_PROVIDERS       = {"bedrock"}
_VERTEX_PROVIDERS        = {"vertex"}


def build_client(provider: str, base_url: str | None = None):
    """
    Return a client object for the given provider.
    Raises ImportError with an install hint if the required SDK is missing.
    Raises EnvironmentError if required env vars are not set.
    """
    prov = _PROVIDERS.get(provider)
    if not prov:
        raise ValueError(
            f"Unknown provider: {provider!r}. "
            f"Valid: {' | '.join(_PROVIDERS or ['anthropic','openai','groq','ollama'])}"
        )

    sdk = prov.get("sdk", "anthropic")

    # ── Anthropic direct ──────────────────────────────────────────────────
    if sdk == "anthropic":
        if anthropic is None:
            raise ImportError("Run: pip install anthropic")
        key = os.environ.get("ANTHROPIC_API_KEY", "")
        if not key:
            raise EnvironmentError("ANTHROPIC_API_KEY not set")
        return anthropic.Anthropic(api_key=key)

    # ── AWS Bedrock ───────────────────────────────────────────────────────
    elif sdk == "anthropic-bedrock":
        if anthropic is None:
            raise ImportError("Run: pip install anthropic")
        try:
            return anthropic.AnthropicBedrock()
        except AttributeError:
            raise ImportError("Run: pip install 'anthropic[bedrock]'")

    # ── Google Vertex ─────────────────────────────────────────────────────
    elif sdk == "anthropic-vertex":
        if anthropic is None:
            raise ImportError("Run: pip install anthropic")
        try:
            project = os.environ.get("GOOGLE_CLOUD_PROJECT", "")
            region  = os.environ.get("GOOGLE_CLOUD_REGION", "us-east5")
            return anthropic.AnthropicVertex(project_id=project, region=region)
        except AttributeError:
            raise ImportError("Run: pip install 'anthropic[vertex]'")

    # ── OpenAI-compatible (openai, groq, openrouter, ollama, …) ──────────
    elif sdk == "openai":
        try:
            import openai as _openai
        except ImportError:
            raise ImportError("Run: pip install openai")

        api_key_env = prov.get("api_key_env")
        key = os.environ.get(api_key_env, "") if api_key_env else "ollama"
        if api_key_env and not key:
            raise EnvironmentError(f"{api_key_env} not set")

        # Priority: explicit arg → env var override → hardcoded in PROVIDERS
        url = (
            base_url
            or os.environ.get("OPENAI_BASE_URL", "")
            or prov.get("base_url")
        )
        # For Ollama, respect OLLAMA_HOST env var
        if provider == "ollama":
            host = os.environ.get("OLLAMA_HOST", "")
            if host:
                url = host.rstrip("/") + "/v1"

        kwargs: dict = {"api_key": key or "none"}
        if url:
            kwargs["base_url"] = url
        return _openai.OpenAI(**kwargs)

    else:
        raise ValueError(f"Unknown sdk type: {sdk!r} for provider {provider!r}")


def call_model(
    client,
    provider: str,
    model: str,
    system_prompt: str,
    user_text: str,
    max_tokens: int = 4096,
) -> tuple[str, int, int]:
    """
    Call the model and return (response_text, tokens_in, tokens_out).
    Abstracts the API differences between Anthropic and OpenAI SDKs.
    """
    prov = _PROVIDERS.get(provider, {})
    sdk  = prov.get("sdk", "anthropic")

    if sdk in ("anthropic", "anthropic-bedrock", "anthropic-vertex"):
        response = client.messages.create(
            model=model,
            max_tokens=max_tokens,
            system=system_prompt,
            messages=[{"role": "user", "content": user_text}],
        )
        return (
            response.content[0].text,
            response.usage.input_tokens,
            response.usage.output_tokens,
        )

    elif sdk == "openai":
        response = client.chat.completions.create(
            model=model,
            max_tokens=max_tokens,
            messages=[
                {"role": "system", "content": system_prompt},
                {"role": "user",   "content": user_text},
            ],
        )
        return (
            response.choices[0].message.content,
            response.usage.prompt_tokens,
            response.usage.completion_tokens,
        )

    else:
        raise ValueError(f"Unknown sdk: {sdk!r}")


# ─── Case discovery ──────────────────────────────────────────────────────────

def discover_cases(agent_filter: str | None) -> list[EvalCase]:
    cases = []
    agents_to_check = []

    if agent_filter:
        agent_dir = EVALS_PATH / agent_filter
        if not agent_dir.exists():
            print(f"No eval directory found for agent: {agent_filter}")
            sys.exit(1)
        agents_to_check = [agent_dir]
    else:
        agents_to_check = [
            d for d in EVALS_PATH.iterdir()
            if d.is_dir() and not d.name.startswith("_") and d.name != "__pycache__"
        ]

    for agent_dir in sorted(agents_to_check):
        # Find case groups by input files
        for input_file in sorted(agent_dir.glob("*_input.md")):
            case_prefix = input_file.stem.replace("_input", "")
            expect_file = agent_dir / f"{case_prefix}_expect.md"
            assert_file = agent_dir / f"{case_prefix}_assert.py"

            if not expect_file.exists():
                print(f"  Warning: {input_file.name} has no matching expect file, skipping")
                continue

            cases.append(EvalCase(
                name=f"{agent_dir.name}/{case_prefix}",
                agent=agent_dir.name,
                input_text=input_file.read_text(encoding="utf-8"),
                expect_text=expect_file.read_text(encoding="utf-8"),
                assert_module_path=assert_file if assert_file.exists() else None,
            ))

    return cases


# ─── Agent system prompt loading ─────────────────────────────────────────────

def load_agent_prompt(agent_name: str) -> str:
    """
    Load the agent's .md file as a system prompt.
    Frontmatter is preserved as-is — it contains bash_allowlist, bash_blocklist,
    and other constraints that are active in production and must also be active
    during evals. Stripping it would test a weaker agent than what ships.
    """
    agent_file = STANGENT_PATH / "agents" / f"{agent_name}.md"
    if not agent_file.exists():
        agent_file = STANGENT_PATH / "agents" / "subagents" / f"{agent_name}.md"
    if not agent_file.exists():
        raise FileNotFoundError(f"No agent file found for: {agent_name}")

    return agent_file.read_text(encoding="utf-8")


# ─── Assertion engine ─────────────────────────────────────────────────────────

def run_assertions(
    case: EvalCase,
    response: str,
    assert_module_path: Path,
) -> list[str]:
    """Load and run the case's assert.py. Returns list of failure messages."""
    spec = importlib.util.spec_from_file_location("assert_module", assert_module_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    failures = []

    # Every assert module must define: assertions = [callable(response) -> str | None]
    # A callable returns None on pass, or a failure message string on fail.
    if not hasattr(module, "assertions"):
        failures.append("assert module has no 'assertions' list")
        return failures

    for assertion_fn in module.assertions:
        try:
            result = assertion_fn(response)
            if result is not None:
                failures.append(str(result))
        except Exception as e:
            failures.append(f"assertion raised exception: {e}")

    return failures


def check_expect(response: str, expect_text: str) -> list[str]:
    """
    Check that the response contains the key phrases from the expect file.
    Lines starting with # are comments. Lines starting with ! are negations.
    """
    failures = []
    for line in expect_text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue

        if line.startswith("!"):
            phrase = line[1:].strip()
            if phrase.lower() in response.lower():
                failures.append(f"Response should NOT contain: {phrase!r}")
        else:
            if line.lower() not in response.lower():
                failures.append(f"Response should contain: {line!r}")

    return failures


# ─── Runner ──────────────────────────────────────────────────────────────────

def run_case(client, provider: str, case: EvalCase, model: str, verbose: bool) -> EvalResult:
    system_prompt = load_agent_prompt(case.agent)

    start = time.time()
    try:
        response_text, tokens_in, tokens_out = call_model(
            client, provider, model, system_prompt, case.input_text
        )
        duration = time.time() - start

    except Exception as e:
        return EvalResult(
            case=case,
            response="",
            passed=False,
            failures=[f"API call failed: {e}"],
            duration_s=time.time() - start,
        )

    failures = []
    failures.extend(check_expect(response_text, case.expect_text))
    if case.assert_module_path:
        failures.extend(run_assertions(case, response_text, case.assert_module_path))

    result = EvalResult(
        case=case,
        response=response_text,
        passed=len(failures) == 0,
        failures=failures,
        duration_s=duration,
        tokens_in=tokens_in,
        tokens_out=tokens_out,
    )

    if verbose:
        print(f"\n{'─'*60}")
        print(f"RESPONSE for {case.name}:")
        print(response_text[:2000])
        if len(response_text) > 2000:
            print("... [truncated]")

    return result


# ─── Output ───────────────────────────────────────────────────────────────────

def print_results(results: list[EvalResult]):
    passed = sum(1 for r in results if r.passed)
    failed = len(results) - passed

    print(f"\n{'═'*60}")
    print(f"  STANGENT EVAL RESULTS")
    print(f"{'═'*60}")

    for r in results:
        status = "✓ PASS" if r.passed else "✗ FAIL"
        print(f"\n  {status}  {r.case.name}  ({r.duration_s:.1f}s, "
              f"in:{r.tokens_in} out:{r.tokens_out})")
        if not r.passed:
            for f in r.failures:
                print(f"          → {f}")

    print(f"\n{'─'*60}")
    print(f"  {passed}/{len(results)} passed", end="")
    if failed:
        print(f"  ({failed} failed)")
    else:
        print("  — all passed ✓")
    print()


def save_results(results: list[EvalResult], output_path: Path):
    data = [
        {
            "case": r.case.name,
            "agent": r.case.agent,
            "passed": r.passed,
            "failures": r.failures,
            "duration_s": round(r.duration_s, 2),
            "tokens_in": r.tokens_in,
            "tokens_out": r.tokens_out,
        }
        for r in results
    ]
    output_path.write_text(json.dumps(data, indent=2))


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Run Stangent agent evals")
    parser.add_argument("--agent",    help="Only run evals for this agent")
    parser.add_argument(
        "--provider", default=DEFAULT_PROVIDER,
        help=f"LLM provider: anthropic | openai | bedrock | vertex (default: {DEFAULT_PROVIDER})",
    )
    parser.add_argument(
        "--model",
        help="Model to use. Defaults to the provider's fast model if not set.",
    )
    parser.add_argument(
        "--base-url",
        help="Base URL override for OpenAI-compatible endpoints (Groq, Together, Ollama…)",
    )
    parser.add_argument("--verbose", action="store_true",
                        help="Print full agent responses")
    parser.add_argument("--output",  help="Save results to JSON file")
    args = parser.parse_args()

    provider  = args.provider.lower()
    model     = args.model or DEFAULT_MODELS.get(provider, DEFAULT_MODELS[DEFAULT_PROVIDER])
    base_url  = args.base_url or os.environ.get("OPENAI_BASE_URL")

    try:
        client = build_client(provider, base_url)
    except (ImportError, EnvironmentError, ValueError) as e:
        print(f"Provider setup failed: {e}")
        sys.exit(1)

    cases = discover_cases(args.agent)

    if not cases:
        print("No eval cases found.")
        sys.exit(0)

    print(f"\nRunning {len(cases)} eval case(s)  [provider: {provider}  model: {model}]\n")

    results = []
    for case in cases:
        print(f"  Running: {case.name} ... ", end="", flush=True)
        result = run_case(client, provider, case, model, args.verbose)
        results.append(result)
        print("PASS" if result.passed else f"FAIL ({len(result.failures)} assertion(s))")

    print_results(results)

    if args.output:
        save_results(results, Path(args.output))
        print(f"Results saved to: {args.output}")

    failed = sum(1 for r in results if not r.passed)
    sys.exit(0 if failed == 0 else 1)


if __name__ == "__main__":
    main()
