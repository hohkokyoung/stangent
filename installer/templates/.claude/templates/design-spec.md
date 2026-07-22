---
title: "<product name> — UI Design Specification"
status: active            # active | superseded
authored: YYYY-MM-DD
mode: <greenfield | brownfield>   # how this spec was first produced
tokens: tokens.md         # sibling file holding the machine-diffable token table
---

# <product name> — UI Design Specification

<!--
This is the durable, committed house style for the project's UI. The design-critic
enforces it (/agentic-review-ui) and the sketcher honours it when drawing mockups.
Amend it by re-running /agentic-design — never hand-edit it into contradiction with
tokens.md. Every rule here should be observable in the built UI: if it can't be
checked against real code or a screenshot, it's a principle, not a spec line.
-->

## 1. Brand & voice
<!-- One paragraph. What should the product FEEL like in three adjectives, and the
     one-line reason. e.g. "Calm, precise, unhurried — a tool for focused work." -->

## 2. Design principles
<!-- 3–6 rules that break ties. Each must be actionable, not a platitude.
     Good:  "Motion is feedback, never decoration — nothing animates without a cause."
     Bad:   "Make it beautiful." -->
- ...

## 3. Color system
<!-- Roles, not raw hex dumps — map every colour to a purpose. Reference token names
     from tokens.md (e.g. `--color-bg`, `--color-accent`). State the intended
     light/dark behaviour. Note the contrast floor (see §9). -->
| Role | Token | Value | Usage |
|---|---|---|---|
| Background | `--color-bg` | `#…` | app canvas |
| Surface | `--color-surface` | `#…` | cards, sheets |
| Text primary | `--color-text` | `#…` | body copy |
| Accent | `--color-accent` | `#…` | primary CTA, focus ring |
| Danger | `--color-danger` | `#…` | destructive actions, errors |

## 4. Typography
<!-- Families, the type scale (as tokens), weights in use, line-height rules.
     Name the ONE display face and the ONE body face — resist a third. -->
| Level | Token | Size / line-height | Weight | Use |
|---|---|---|---|---|
| Display | `--text-3xl` | … | … | page titles |
| Body | `--text-base` | … | … | default copy |
| Caption | `--text-sm` | … | … | metadata, hints |

## 5. Spacing & layout grid
<!-- The spacing scale (tokens), the base unit, container widths, grid columns,
     and the corner-radius scale. This is what makes screens feel like one system. -->
- Base unit: `<4px | 8px>` · scale: `--space-1 … --space-N`
- Radius scale: `--radius-sm/md/lg`
- Container max-width(s): …
- Grid: … columns, … gutter

## 6. Component inventory & states
<!-- The canonical components and the states each MUST render. The critic checks
     that built components honour these states — a button with no disabled/focus
     style is a finding. -->
| Component | Required states | Notes |
|---|---|---|
| Button (primary/secondary/ghost) | default · hover · active · focus-visible · disabled · loading | … |
| Input | default · focus · error · disabled · with-help-text | … |
| Card | default · hover (if interactive) | … |
| … | … | … |

## 7. Motion & interaction
<!-- The motion budget: durations, easing tokens, what may animate and what must
     not. Honour prefers-reduced-motion. Tie to the recommended motion library
     in plugins.md if one was chosen. -->
- Durations: `--motion-fast` … / `--motion-base` … / `--motion-slow` …
- Easing: `--ease-standard` (…)
- Allowed: <enter/exit transitions, feedback on interaction, …>
- Forbidden: <autoplaying loops, motion without user cause, …>
- Reduced motion: all non-essential motion disabled under `prefers-reduced-motion`.

## 8. Iconography & imagery
<!-- Icon set + size grid + stroke weight; image treatment (radius, aspect ratios,
     placeholder rules). -->

## 9. Accessibility bar
<!-- The non-negotiable floor. The critic treats violations here as High severity. -->
- Contrast: text ≥ 4.5:1, large text / UI ≥ 3:1 (WCAG AA).
- Every interactive element has a visible `:focus-visible` state.
- Hit targets ≥ 44×44px on touch.
- Colour is never the sole carrier of meaning.
- Respects `prefers-reduced-motion` and `prefers-color-scheme`.

## 10. Responsive breakpoints
<!-- Named breakpoints as tokens + what changes at each. -->
| Name | Min-width | Layout change |
|---|---|---|
| sm | … | … |
| md | … | … |
| lg | … | … |

## 11. Recommended stack
<!-- Summary only — the full rationale + install pointers live in plugins.md.
     Name the framework, the component/styling approach, and the motion library. -->
See [plugins.md](plugins.md) for the chosen stack and where to install each piece.

## 12. Do / Don't
<!-- A short table of concrete, checkable pairs. These are the critic's sharpest
     signals — write the ones that map to real mistakes this project could make. -->
| Do | Don't |
|---|---|
| Use `--color-accent` for the single primary action per view | Put two primary buttons in one view |
| Pull every spacing value from the `--space-*` scale | Hard-code `margin: 13px` |
| … | … |

## 13. Enforcement checklist
<!-- The exact things /agentic-review-ui checks. Keep each line binary/observable. -->
- [ ] All colours resolve to a `--color-*` token (no raw hex in components)
- [ ] All spacing resolves to the `--space-*` scale
- [ ] Type sizes come from the type-scale tokens
- [ ] Every interactive element renders focus + disabled states
- [ ] Motion respects the budget in §7 and `prefers-reduced-motion`
- [ ] Contrast meets the §9 floor
