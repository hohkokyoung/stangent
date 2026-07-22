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
| Token | Light | Dark | Role |
|---|---|---|---|
| `--color-bg` | `#…` | `#…` | app canvas |
| `--color-surface` | `#…` | `#…` | cards, sheets |
| `--color-text` | `#…` | `#…` | primary text |
| `--color-text-muted` | `#…` | `#…` | secondary text |
| `--color-border` | `#…` | `#…` | dividers, outlines |
| `--color-accent` | `#…` | `#…` | primary action, focus |
| `--color-danger` | `#…` | `#…` | destructive / error |

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
