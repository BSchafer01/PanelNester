# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, scope, sequencing | Ripley | System boundaries, feature slicing, cross-layer trade-offs, reviewer gates |
| Web UI and sheet viewer | Dallas | React screens, results dashboard, Three.js sheet viewer, interaction polish |
| Desktop host and local platform wiring | Bishop | WPF shell, WebView2 bridge, file dialogs, local persistence wiring, PDF/export integration |
| Import, validation, domain services, nesting engine | Parker | CSV/XLSX parsing, validation rules, project/material services, nesting heuristics, report data shaping |
| Testing and review | Hicks | Test plans, regression coverage, edge cases, reviewer verdicts, acceptance checks |
| Code review | Ripley | Review PRs, challenge risky changes, check architectural fit |
| Testing | Hicks | Write tests, find edge cases, verify fixes |
| Scope & priorities | Ripley | What to build next, trade-offs, decisions |
| Async issue work (bugs, tests, small features) | @copilot 🤖 | Well-defined tasks matching capability profile |
| Session logging | Scribe | Automatic — never needs routing |

## Squad Member Labels

- `squad:ripley`
- `squad:dallas`
- `squad:bishop`
- `squad:parker`
- `squad:hicks`
- `squad:copilot`

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, evaluate @copilot fit, assign `squad:{member}` label | Ripley |
| `squad:{name}` | Pick up issue and complete the work | Named member |
| `squad:copilot` | Assign to @copilot for autonomous work (if enabled) | @copilot 🤖 |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Ripley** triages it — analyzing content, evaluating @copilot's capability profile, and assigning the right `squad:{member}` label.
2. **@copilot evaluation:** route well-defined bounded tasks to `squad:copilot`; keep architecture, bridge, algorithm, and ambiguous work with squad members.
3. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
4. When `squad:copilot` is applied and auto-assign is enabled, `@copilot` is assigned on the issue and picks it up autonomously.
5. Members can reassign by removing their label and adding another member's label.
6. The `squad` label is the inbox — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.**
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** When implementation starts, launch Hicks early for tests from the PRD slice.
7. **Issue-labeled work** — `squad:{member}` routes directly to that member; Ripley handles `squad` triage.
8. **@copilot routing** — use it for crisp, bounded follow-up work, not for primary architectural choices.
