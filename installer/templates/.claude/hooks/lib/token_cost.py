#!/usr/bin/env python3
"""Token accounting + cost estimation.

Prices a `usage` dict (as found in a Claude Code transcript's
`message.usage`) for a given model. Rates are **estimates** in USD per million
tokens and can be overridden per project via `.agentic.yml`:

    pricing:
      claude-opus-4-8:   { input: 15, output: 75, cache_read: 1.5, cache_write: 18.75 }
      claude-sonnet-4-6: { input: 3,  output: 15, cache_read: 0.3, cache_write: 3.75 }

Matched by longest model-id prefix, so future point releases inherit a family's
rate until you add an explicit entry.
"""
from __future__ import annotations

from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

AGENTIC_YML = Path.cwd().resolve() / ".claude" / ".agentic.yml"

# (input, output, cache_read, cache_write) USD per 1M tokens. Estimates.
_DEFAULT_RATES: dict[str, tuple] = {
    "claude-opus-4":   (15.0, 75.0, 1.5, 18.75),
    "claude-sonnet-4": (3.0, 15.0, 0.30, 3.75),
    "claude-haiku-4":  (1.0, 5.0, 0.10, 1.25),
    "claude-opus":     (15.0, 75.0, 1.5, 18.75),
    "claude-sonnet":   (3.0, 15.0, 0.30, 3.75),
    "claude-haiku":    (0.80, 4.0, 0.08, 1.0),
}
_FALLBACK = (3.0, 15.0, 0.30, 3.75)  # sonnet-ish


def _config_rates() -> dict:
    if yaml is None or not AGENTIC_YML.exists():
        return {}
    try:
        cfg = yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or {}
    except Exception:
        return {}
    out = {}
    for model, r in (cfg.get("pricing") or {}).items():
        if isinstance(r, dict):
            out[model] = (
                float(r.get("input", 0)), float(r.get("output", 0)),
                float(r.get("cache_read", 0)), float(r.get("cache_write", 0)),
            )
    return out


def rates_for(model: str) -> tuple:
    """Longest-prefix match against config overrides then built-in defaults."""
    if not isinstance(model, str):
        return _FALLBACK
    table = {**_DEFAULT_RATES, **_config_rates()}
    best = None
    for prefix, r in table.items():
        if model.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, r)
    return best[1] if best else _FALLBACK


def tokens_of(usage: dict) -> dict:
    """Normalize a transcript usage dict to the four counts we price."""
    u = usage or {}
    return {
        "input": int(u.get("input_tokens", 0) or 0),
        "output": int(u.get("output_tokens", 0) or 0),
        "cache_read": int(u.get("cache_read_input_tokens", 0) or 0),
        "cache_write": int(u.get("cache_creation_input_tokens", 0) or 0),
    }


def cost_usd(model: str, tokens: dict) -> float:
    ri, ro, rcr, rcw = rates_for(model)
    return round(
        (tokens.get("input", 0) * ri
         + tokens.get("output", 0) * ro
         + tokens.get("cache_read", 0) * rcr
         + tokens.get("cache_write", 0) * rcw) / 1_000_000.0,
        4,
    )
