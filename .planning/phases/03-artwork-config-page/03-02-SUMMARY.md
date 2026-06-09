---
phase: 03-artwork-config-page
plan: 02
subsystem: configuration-page
tags: [jellyfin, dashboard, config-page, html, javascript, apiclient, ihaswebpages, plug-03]
status: complete
requires:
  - "Plugin.cs IHasWebPages.GetPages() registering Configuration.configPage.html as EmbeddedResource (01-02)"
  - "Plugin.StaticId GUID 80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69 (01-02)"
  - "PluginConfiguration fields YearSeasons / EpisodeNumberingScheme / MaxDescriptionLength (present on disk from Phase 2 work)"
provides:
  - "Jellyfin.Plugin.Youtarr/Configuration/configPage.html — Dashboard config page with three controls wired to ApiClient.getPluginConfiguration / updatePluginConfiguration"
  - "YearSeasons checkbox, EpisodeNumberingScheme select (Default/YYYYMMDD), MaxDescriptionLength number input — all bound to real persisted PluginConfiguration fields"
affects:
  - "03-03 live verify confirms the page renders in the Dashboard and that all three settings persist across a server restart (requires Docker harness)"
  - "PLUG-03 is now code-complete; admins can configure year-seasons, numbering scheme, and description length from Dashboard -> Plugins -> YoutarrMetadata -> Settings"
tech-stack:
  added: []
  patterns:
    - "Standard Jellyfin Dashboard config-page pattern: div[data-role=page] + data-require web components + ApiClient get/updatePluginConfiguration keyed by the plugin GUID"
    - "pageshow event loads config and populates controls; form submit re-fetches config, applies control values, then updatePluginConfiguration + Dashboard.processPluginConfigurationUpdateResult"
    - "Enum bound by NAME: select option values are the literal C# enum member names (Default / YYYYMMDD) because EpisodeNumberingScheme serializes to config XML by name, not integer (RESEARCH Pitfall 4)"
    - "int field coerced client-side via parseInt(value, 10) before assignment so the server receives an int, not a string (RESEARCH Pitfall 5 / threat T-03-01)"
key-files:
  created:
    - ".planning/phases/03-artwork-config-page/03-02-SUMMARY.md"
  modified:
    - "Jellyfin.Plugin.Youtarr/Configuration/configPage.html (stub -> three working controls + load/save script)"
decisions:
  - "EpisodeNumberingScheme bound by enum NAME (option values \"Default\"/\"YYYYMMDD\"), not integer index. The C# enum serializes to the per-GUID config XML by name; sending \"0\"/\"1\" would fail deserialization and silently reset the setting (RESEARCH Pitfall 4). The select .value round-trips the exact enum-member string in both directions."
  - "MaxDescriptionLength wrapped in parseInt(value, 10) on save. The number input's .value is always a string; without coercion the server may receive \"500\" and fail int deserialization (RESEARCH Pitfall 5). This is the client-side mitigation for threat T-03-01; the server-side >=0 clamp lives in the Phase 2 consumer, not this page."
  - "Task 1 (PluginConfiguration fields) was a no-op: YearSeasons (bool, default true), EpisodeNumberingScheme (enum Default=0/YYYYMMDD=1, default Default), and MaxDescriptionLength (int, default 500) already existed on disk with the exact required names/defaults from prior Phase 2 work. Per the plan's idempotency clause, the existing definitions were left untouched — no duplication, no commit for Task 1."
  - "No Plugin.cs change: GetPages() already serves Configuration.configPage.html as an EmbeddedResource and the csproj already includes it (<EmbeddedResource Include=\"Configuration/configPage.html\" />). Only the HTML body was replaced."
  - "Added emby-select to data-require (the stub omitted it) so the new <select is=\"emby-select\"> upgrades and styles correctly in the Dashboard SPA."
  - "No user-supplied strings rendered into the DOM (threat T-03-02 / XSS): the page only reads framework values into form controls and writes control values back; all literal copy is static."
requirements: [PLUG-03]
metrics:
  duration: "~4 min"
  completed: "2026-06-10"
  tasks: "2 of 2"
  files: 1
---

# Phase 3 Plan 02: Dashboard Configuration Page (PLUG-03) Summary

Replaced the Phase 1 placeholder `configPage.html` with a working Jellyfin Dashboard plugin
configuration page exposing three settings wired to `PluginConfiguration` via the standard
`ApiClient.getPluginConfiguration` / `updatePluginConfiguration` pattern keyed by the permanent
plugin GUID `80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69`. Release build succeeds with the updated
embedded resource and the full suite stays **85/85 green**. Live render + persistence-across-restart
is verified separately in plan 03-03 (Docker harness).

## What Was Built

| File | Provides |
|------|----------|
| `Configuration/configPage.html` | `div#YoutarrConfigPage[data-role=page]` with `data-require="emby-input,emby-button,emby-select,emby-checkbox"`. A `form#YoutarrConfigForm` containing three controls plus a submit button, and a script block that loads config on `pageshow` and saves on `submit`. |

### The three controls and their config bindings

| Control | Element | Binds to (C# property) | Type / default |
|---------|---------|------------------------|----------------|
| Year seasons | `input#YearSeasons[is=emby-checkbox][type=checkbox]` | `PluginConfiguration.YearSeasons` | `bool`, default `true` |
| Episode numbering | `select#EpisodeNumberingScheme[is=emby-select]` with options `value="Default"` / `value="YYYYMMDD"` | `PluginConfiguration.EpisodeNumberingScheme` | enum (`Default=0`/`YYYYMMDD=1`), default `Default` |
| Max description length | `input#MaxDescriptionLength[is=emby-input][type=number][min=0]` | `PluginConfiguration.MaxDescriptionLength` | `int`, default `500` (0 = no truncation) |

JS property names match the C# property names case-sensitively in both load and save directions.

### Enum serialization choice

`EpisodeNumberingScheme` is bound by **enum member name**, not integer index. `BasePlugin<T>`
serializes the enum to the per-GUID config XML by its name, so the `<select>` option values are
the literal strings `"Default"` and `"YYYYMMDD"` (matching the C# member names verbatim). On load,
`config.EpisodeNumberingScheme` is the string `"Default"`/`"YYYYMMDD"` and is assigned directly to
the select's `.value`; on save the select's `.value` is written straight back. Using integer option
values (`"0"`/`"1"`) would fail deserialization and silently reset the setting (RESEARCH Pitfall 4).

### How load / save works

- **Load (`pageshow` on `#YoutarrConfigPage`):** `Dashboard.showLoadingMsg()` → `ApiClient.getPluginConfiguration(guid)` → set `#YearSeasons.checked`, `#EpisodeNumberingScheme.value`, `#MaxDescriptionLength.value` from the returned config → `Dashboard.hideLoadingMsg()`.
- **Save (`submit` on `#YoutarrConfigForm`):** `Dashboard.showLoadingMsg()` → re-fetch via `getPluginConfiguration(guid)` (so unrelated fields are preserved) → assign `config.YearSeasons` from the checkbox `.checked`, `config.EpisodeNumberingScheme` from the select `.value`, `config.MaxDescriptionLength` from `parseInt(input.value, 10)` → `ApiClient.updatePluginConfiguration(guid, config)` → on resolve `Dashboard.processPluginConfigurationUpdateResult(result)`. The handler calls `e.preventDefault()` and `return false` to suppress the native form post.

Persistence itself needs no plugin code: `BasePlugin<PluginConfiguration>` writes the config to
`config/plugins/<GUID>/<GUID>.xml` and reloads it on startup automatically.

## Deviations from Plan

### Task 1 — idempotent no-op (anticipated by the plan)

The plan's Task 1 said to add `EpisodeNumberingScheme` and `MaxDescriptionLength` to
`PluginConfiguration.cs` *only if absent*, leaving any pre-existing definitions untouched. All three
fields were already present on disk (from prior Phase 2 work) with the exact required names and
defaults — `YearSeasons` (`bool`, `true`), `EpisodeNumberingScheme` (enum `Default`/`YYYYMMDD`,
default `Default`), `MaxDescriptionLength` (`int`, `500`). Per the idempotency clause, no edit was
made and no commit was created for Task 1. Verification (Release build + grep for all three
identifiers) passed against the existing file. This is the documented self-sufficient/Phase-2-already-ran
path, not an unexpected deviation.

No auto-fixed bugs, no auth gates, no architectural changes.

## Verification Results

- `dotnet build -c Release` (Jellyfin.Plugin.Youtarr.csproj): **Build succeeded** with the updated embedded resource.
- `dotnet test -c Release` (Jellyfin.Plugin.Youtarr.Tests.csproj): **Passed — Failed: 0, Passed: 85, Skipped: 0** (no regression).
- grep confirms in `configPage.html`: the GUID `80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69`, `updatePluginConfiguration`, `value="YYYYMMDD"`, `parseInt`, and `emby-select` all present.
- Live render + persistence-across-restart: deferred to plan 03-03 (requires Docker harness; out of scope for this plan).

## Commits

- `b5b4f54` feat(03-02): wire config page to three plugin settings

## Known Stubs

None. The page binds to real, persisted `PluginConfiguration` fields; no placeholder/empty data sources remain.

## Self-Check: PASSED

- FOUND: `Jellyfin.Plugin.Youtarr/Configuration/configPage.html`
- FOUND: `.planning/phases/03-artwork-config-page/03-02-SUMMARY.md`
- FOUND: commit `b5b4f54`
