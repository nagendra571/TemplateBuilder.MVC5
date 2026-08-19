# Plan: Porting fork-only editor features into TemplateBuilder.Editor (.NET 8/10)

**Date:** 2026-08-19
**Spec:** `docs/superpowers/specs/2026-08-19-origin-editor-feature-port-design.md`
**Target repo:** `TemplateBuilder` (net8.0/net10.0, ASP.NET Core, EF Core, System.Text.Json)
**Method:** TDD per task; run the affected test project after every task; full suite at checkpoints.

Test projects in the origin (mirror of src layout): `tests/Application.Tests`, `tests/Domain.Tests`, `tests/Editor.Tests`, `tests/Infrastructure.Tests`. If a feature's project has no test project, create one before writing production code (the origin's own convention: one test project per src project).

---

## Task 1 — Baseline (no code)

1. Clone/checkout the origin `main`. Build: `dotnet build` from repo root; run full suite `dotnet test`.
2. Confirm the current state of each touch point (record their exact current shape so later diffs are clean):
   - `src/TemplateBuilder.Application/Services/TemplateEngine.cs` (render path, `CaseInsensitiveScriptObject`, `MemberRenamer`)
   - `src/TemplateBuilder.Application/DTOs/SqlColumnInfo.cs` (positional record `(Name, DataType)`)
   - `src/TemplateBuilder.Application/Services/SqlViewDiscoveryService.cs` (`GetViewColumnsAsync` query)
   - `src/TemplateBuilder.Application/Options/TemplateBuilderOptions.cs`
   - `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs` (`Create` POST action)
   - `src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (already has `_csrf` + header-sending fetch pattern — verify)
   - `src/TemplateBuilder.Core/Entities/Template.cs` (no `SampleData` today)
   - `src/TemplateBuilder.Application/DependencyInjection.cs` (`AddTemplateBuilderEditor`)
3. `git status` clean. **Checkpoint:** full suite green; baseline recorded in a short commit message.

## Task 2 — F6: dual model access (Application layer)

Test-first in `tests/Application.Tests` (`TemplateEngineTests`):

1. **Red:** tests for — default `AllowTopLevelModelAccess=false` → `{{ X }}` renders empty, `{{ model.X }}` renders; option on → both render; option on + model key literally named `model` → user's `model` value not clobbered (the `model.*` wrapper stays overridden by the user key); option on + case-insensitive access `{{ x }}` for key `X`.
2. **Green:** add `AllowTopLevelModelAccess` (bool, default `false`) to `TemplateBuilderOptions`. In the render path, when the option is on, import the model members at global scope into the same case-insensitive `ScriptObject` the `model` wrapper uses; keep the existing `model` wrapper import; guard so a top-level model key named `model` wins over the wrapper.
3. Document the builtin-shadowing caveat (`date`, `string`, `math`, `html`, `array`, `object`, `for`, `if`, …) in the option's XML doc.
4. **Checkpoint:** `dotnet test tests/Application.Tests` green.

## Task 3 — F2b: SqlColumnInfo length/nullability (Application layer)

Test-first:

1. **Red:** `SqlViewDiscoveryServiceTests` — `GetViewColumnsAsync` returns columns with `MaxLength` and `IsNullable` populated for a view with varchar/int/bit/decimal/date columns (test DB or in-memory; match existing discovery tests' DB strategy).
2. **Green:**
   - Search the repo for **every** construction site of `SqlColumnInfo` (the record is positional today). If only the discovery service constructs it, convert to `record SqlColumnInfo(string Name, string DataType, int? MaxLength = null, bool IsNullable = false)` — verify no test constructs it positionally; fix any construction sites found. (If a site outside the service constructs it, convert to init-props instead and update sites.)
   - Extend the discovery query with `CHARACTER_MAXIMUM_LENGTH` and `IS_NULLABLE` (reference: fork `SqlViewDiscoveryService`).
3. **Checkpoint:** discovery tests green; `git grep 'new SqlColumnInfo'` clean (no stale positional constructions).

## Task 4 — F2a: SampleDataGenerator service (Application layer)

Test-first in `tests/Application.Tests` — port the fork's tests:

1. **Red:** port `SampleDataGeneratorTests` from the fork (`tests/TemplateBuilder.Application.Tests`) covering: view-only columns (typed values, max 50, length clipping), tokens-only (`model.X` scalars, kind inference by name: email/phone/date/decimal/int/bool/string), loops (`for x in model.Items` → 3 items with loop fields), view+body merge (view wins on collision), parse-error tolerance (no throw), max scalar keys (50).
2. **Green:** port `SampleDataGenerator.cs` verbatim, adapting only namespaces (`TemplateBuilder.Application.Services`, same) and adding `ISampleDataGenerator` to the Application DI registration.
3. **Checkpoint:** Application.Tests green.

## Task 5 — F2c: SampleData persistence + endpoints (Domain + Infrastructure + Editor)

Test-first:

1. **Red (Domain.Tests / Infrastructure.Tests):** `Template` has nullable `SampleData`; persists through the repository round-trip.
2. **Green:**
   - Add `public string? SampleData { get; set; }` to `Template`.
   - EF Core migration for the new column (existing `MigrationHostedService` will apply it on startup).
   - Endpoints in `TemplatesController`:
     - `POST /Templates/Api/SampleData/Generate` → body `{ viewName, templateBody }` → `{ sampleData }` (via `ISampleDataGenerator`).
     - `PUT /Templates/{id}/SampleData` → body `{ sampleData }` → sets `template.SampleData` (null when blank), persists, → `{ saved: true }`. 404 when template missing.
   - Register `ISampleDataGenerator` in `AddTemplateBuilderEditor`.
3. **Green (Editor.Tests):** controller tests for Generate (happy + empty result) and SaveSampleData (save / blank-clears / 404). Match the origin's existing controller test style.
4. **Checkpoint:** full `dotnet test` green.

## Task 6 — F2d: sample-data editor UI (JS/CSS)

1. Port fork JS behaviors (`template-editor.js`): `generateSampleData(mode)` (`view` / `tokens` / `both`) via `POST /Templates/Api/SampleData/Generate` with `_csrf` header; write pretty-printed JSON into the preview-model textarea; `saveSampleData()` via `PUT /Templates/{id}/SampleData`; button wiring — `btn-gen-save` (save with template), `btn-gen-menu` dropdown with `.tb-gen-option[data-gen=view|tokens|both]` options, `btn-gen-sample` (generate); on Edit load prefill model textarea from saved data, else auto-generate `both` once.
2. Port related CSS (`.gen-*` rules) from fork `template-editor.css`.
3. Editor partial: add the buttons to the sample-data section; set the preview model input value from `template.SampleData` server-side on Edit load.
4. **Checkpoint:** manual browser pass — generate in all three modes; save; reload → prefilled; preview renders with generated model.

## Task 7 — F1: JSON Create endpoint (Editor + JS)

Test-first:

1. **Red (Editor.Tests):** `POST /Templates/Create` with JSON body `{ name, description, templateType, body }` creates template + initial version, returns `{ templateId }`; duplicate name → 400 `VALIDATION_ERROR` with message; missing name → 400.
2. **Green:**
   - Rename POST action to `CreateTemplateJson`, `[ActionName("Create")]`, bind `[FromBody] TemplateEditorViewModel`, keep `[ValidateAntiForgeryToken]` (Core reads the `RequestVerificationToken` header).
   - Server logic copied from fork (`CreateTemplateJson`): publish version 1 "Initial version" when body non-empty; duplicate → 400 with name in message; missing name → 400.
3. JS: rewrite `createTemplate()` to `fetch` POST JSON with `_csrf` header (same as `saveVersion`); on `{ templateId }` navigate to Edit; on 400 show `err.message`; Create button no longer submits the form.
4. Remove now-dead form-POST path if unreferenced.
5. **Checkpoint:** Editor.Tests green; manual browser create → redirect → render; duplicate shows real message.

## Task 8 — F3: Scriban reference catalog + panel (Application static + JS/CSS)

1. Port `ScribanReferenceCatalog.cs` + `ScribanReferenceEntry` verbatim (Application, static, no deps).
2. Editor UI: collapsible "Scriban reference" panel listing entries grouped by `Group` (Label + Code + Expected when present); click-to-insert at editor caret (same insertion path the field palette uses).
3. Render the catalog via a script tag / inline JSON in the editor partial (no endpoint).
4. **Checkpoint:** browser pass — panel renders all 7 groups; click inserts at caret.

## Task 9 — F4: palette search, used-field marks, model badges (JS/CSS)

1. Port from fork JS/CSS:
   - Palette search input (case-insensitive substring filter on palette rows).
   - `_tbUsedFields(html)` regexes (scalars `model.\w+` + loops `for ... in model.\w+`); apply `palette-field--used` + checkmark span on load and debounced on body change.
   - Model badges: chips for preview-model JSON keys above the textarea; remove-chip deletes key; typing refreshes chips.
2. **Checkpoint:** browser pass — search filters live; used fields marked and update on edit; chips reflect JSON and edit it.

## Task 10 — F5: CSS scoping under `#tb-editor-host`

1. Ensure the editor partial's root element carries `id="tb-editor-host"`.
2. Prefix all editor CSS rules under `#tb-editor-host` (fork `template-editor.css` is the scoping reference — apply the same discipline to the origin's stylesheet, including the styles added in Tasks 6/8/9).
3. Keep overlay/modal z-index working under the wrapper.
4. **Checkpoint:** sample host page loads Bootstrap 3.3.7 + jQuery 3.7.1 (the exact collision scenario from the fork's risk register); editor renders identical to isolated case; no editor styles leak outside the wrapper (inspect rendered stylesheet).

## Task 11 — Final verification & docs

1. Full `dotnet build` + full `dotnet test` from repo root; record output.
2. Manual end-to-end pass against the sample host: create (JSON) → edit → auto-fill sample data (all modes) → save → reload → preview with generated model → save version → history → restore → reference panel → palette search/used marks → Bootstrap-loaded page.
3. Update the origin README/docs: F6 option documentation, new endpoints, sample-data feature note.
4. Squash/PR: 6 features, one PR or stacked PRs ordered F6 → F2 → F1 → F3/F4 → F5. Conventional commit style (`feat:`, `fix:`) per origin convention.
5. **Final checkpoint:** full suite green; PR description maps each change to the spec's feature number.

---

## Verification commands (run, don't assert)

- `dotnet build` — repo root, after each task
- `dotnet test` — full suite at checkpoints; `dotnet test tests/<Project>.Tests` after each task's unit tests
- Browser pass — use the sample host (`TemplateBuilder.SampleHost` or equivalent) for F2d/F1/F3/F4/F10 manual checks; never claim a browser feature works without running it.

## Risks carried into implementation (from spec)

- F2b: `SqlColumnInfo` is a positional record — grep for all constructions before changing shape.
- F6: option must default off; do not "fix" Scriban builtin shadowing beyond documenting it.
- F5: Bootstrap-3.3.7 collision test is the acceptance gate — do it in the sample host, don't skip it.
- JS lands in one file (`template-editor.js`) — implement Tasks 6/7/8/9 sequentially to keep the diff reviewable.