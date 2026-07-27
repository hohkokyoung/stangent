---
title: "<product name> — Design Tokens"
status: active
authored: YYYY-MM-DD
---

# Design Tokens

<!--
The single machine-diffable source of the design system's primitive values. The
design-critic diffs built CSS/theme values against this table; the sketcher pulls
from it so mockups match. Keep token NAMES stable across amendments — renaming a
token is a breaking change the critic will read as drift everywhere it was used.

If the project already has a token file (tailwind.config, a `:root` block, a theme
constants file), this table MIRRORS it — record the real values, and note the
source path in "Source" below so the critic reads from the code, not a copy that
can rot.
-->

Source of truth in code: `<path to tailwind.config / :root block / theme file, or "this file">`

## Color

<!--
CONTRAST IS A COLUMN, NOT A FOOTNOTE. Every token that can carry or sit behind
text gets a measured ratio here, light and dark, against the surface it is
actually used on. Write `n/a — decorative only` when a token never touches text,
and say what it *is* used for.

Leave no cell blank and never delete the column. A token whose ratio was never
computed then shows as an empty cell instead of looking like every other row —
which is the whole point. Observed in a real project: `error` shipped at 3.76:1
as body text and 3.24:1 behind white labels, below the 4.5:1 floor, across six
screens. Neighbouring tokens carried ratios in prose parentheses; `error` simply
had none, and nothing made that absence visible, so no review ever questioned it.

Ratio is (L1+0.05)/(L2+0.05) on WCAG relative luminance. Compute it — do not
estimate it by eye, and do not carry a number over from a similar-looking colour.
-->

| Token | Light | Dark | Contrast (light / dark) | Role |
|---|---|---|---|---|
| `--color-bg` | `#…` | `#…` | n/a — surface | app canvas |
| `--color-surface` | `#…` | `#…` | n/a — surface | cards, sheets |
| `--color-text` | `#…` | `#…` | `…:1` / `…:1` on `--color-bg` | primary text |
| `--color-text-muted` | `#…` | `#…` | `…:1` / `…:1` on `--color-bg` | secondary text |
| `--color-border` | `#…` | `#…` | `…:1` / `…:1` (≥3:1 if it carries meaning) | dividers, outlines |
| `--color-accent` | `#…` | `#…` | `…:1` / `…:1` for white-on-fill | primary action, focus |
| `--color-danger` | `#…` | `#…` | `…:1` / `…:1` as text AND white-on-fill | destructive / error |

<!--
A token used both ways (coloured text on a surface, and white text on it as a
fill) needs BOTH ratios. They differ, and one passing says nothing about the
other: `#EF4444` gives 3.76:1 either way, but a token can clear one and fail the
other comfortably.
-->

**Every token above that carries text must meet §9's floor.** If one does not,
that is a spec defect to fix here — not a usage rule for components to work
around.

## Spacing (base unit: `<8px>`)
| Token | Value |
|---|---|
| `--space-1` | 4px |
| `--space-2` | 8px |
| `--space-3` | 12px |
| `--space-4` | 16px |
| `--space-6` | 24px |
| `--space-8` | 32px |

## Radius
| Token | Value |
|---|---|
| `--radius-sm` | … |
| `--radius-md` | … |
| `--radius-lg` | … |

## Type scale
| Token | Size | Line-height |
|---|---|---|
| `--text-sm` | … | … |
| `--text-base` | … | … |
| `--text-lg` | … | … |
| `--text-xl` | … | … |
| `--text-2xl` | … | … |
| `--text-3xl` | … | … |

## Shadow / elevation
| Token | Value |
|---|---|
| `--shadow-sm` | … |
| `--shadow-md` | … |

## Motion
| Token | Value |
|---|---|
| `--motion-fast` | … |
| `--motion-base` | … |
| `--motion-slow` | … |
| `--ease-standard` | … |
