# Design: Porting fork-only editor features into TemplateBuilder.Editor (.NET 8/10)

**Date:** 2026-08-19
**Audience:** implementer working in the `TemplateBuilder` origin repo (net8.0/net10.0, ASP.NET Core, EF Core, System.Text.Json). This document describes features that exist today only in the MVC5 fork (`TemplateBuilder.Editor.Mvc5`) and specifies how to implement them in the origin.
**Status:** approved design (handoff)

---

## Goal

Bring six features that were built for the .NET Framework fork back into the origin `TemplateBuilder.Editor`, so both products converge on the same editor capabilities. The fork's implementation is the proven reference — every feature here is already working in production-like conditions on the fork, so the origin port should copy behavior, not redesign it.

The fork is a verbatim port of the origin with additions layered on top; these six features are the additions. **Do not modify the fork code** — it is the source of truth for behavior. The origin repo remains the only codebase edited.

## Scope (all 6 features)

| # | Feature | Layer touched |
|---|---------|---------------|
| F1 | JSON Create endpoint (replace form-POST) | Controller + JS |
| F2 | Server-side sample-data generation (SQL view / template tokens / both; save-with-template) | Application service + Domain + Controller + JS/CSS |
| F3 | Scriban syntax reference catalog + editor panel | Application (static catalog) + JS/CSS |
| F4 | Palette search, used-field marks, model badges | JS/CSS |
| F5 | Editor CSS scoping under `#tb-editor-host` | CSS |
| F6 | Dual model access (top-level `X` **and** `model.X`) | Application service (engine) + options |

## Source of truth (fork reference implementation)

All fork paths are relative to the `TemplateBuilder.Mvc5` repo root.

| Feature | Fork reference files |
|---------|----------------------|
| F1 | `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs` (`CreateTemplateJson`, lines ~58–90); `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (`createTemplate()`, ~line 628); `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml` (`btn-create-submit` is `type="button"`) |
| F2 | `src/TemplateBuilder.Application/Services/SampleDataGenerator.cs` (199 lines, full); `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs` (`GenerateSampleData` ~235, `SaveSampleData` ~244); `src/TemplateBuilder.Editor.Mvc5/Models/SampleDataRequests.cs`; `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (`generateSampleData` ~526, `saveSampleData` ~565, `updateSampleSaveBtn`); `src/TemplateBuilder.Application/DTOs/SqlColumnInfo.cs` (has `MaxLength`, `IsNullable` — origin's DTO lacks these) |
| F3 | `src/TemplateBuilder.Application/Services/ScribanReferenceCatalog.cs` (full, 31 lines) |
| F4 | `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (palette search filter, used-field marking, model badges); `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css` |
| F5 | `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css` (all rules scoped under `#tb-editor-host`) |
| F6 | `src/TemplateBuilder.Application/Services/TemplateEngine.cs` (`RenderBodyAsync`, lines 39–82) |

---

## F1 — JSON Create endpoint

### Problem in the origin

The origin's `Create` POST is a form-bound action (`TemplateEditorViewModel` + `[ValidateAntiForgeryToken]`) and the editor JS submits the form. This works on ASP.NET Core today, but it is the one endpoint in the API that doesn't match the JSON pattern every other write endpoint uses (`SaveVersion`, `RestoreVersion`, `ToggleActive`, `Validate`, `Duplicate`), and form POSTs are the fragile case (form collection parsing, model binding, antiforgery form-field conventions).

### Design

Mirror the fork exactly:

1. Keep `Create` GET as-is (renders the editor).
2. Rename the POST action to `CreateTemplateJson` with `[ActionName("Create")]` so the route stays `POST /Templates/Create`.
3. Bind the JSON body (`[FromBody] TemplateEditorViewModel`) instead of the form. Request shape: `{ name, description, templateType, body }`.
4. Server behavior (copy from fork):
   - `name` required → 400 `ErrorResult("VALIDATION_ERROR", "Template name is required.")`
   - Create template; if `body` non-empty, publish initial version (version number 1, comment "Initial version").
   - Return `{ templateId }`.
   - Duplicate name (unique constraint violation) → 400 `ErrorResult("VALIDATION_ERROR", "A template named '<name>' already exists.")`.
5. `[ValidateAntiForgeryToken]` stays on the action — ASP.NET Core's antiforgery checks the `RequestVerificationToken` **header** when no form field is present (default `AntiforgeryOptions.HeaderName`), so header-based antiforgery works with the stock attribute; no custom attribute needed in the origin.
6. JS: replace the form submission in `createTemplate()` with a `fetch` POST of `JSON.stringify({ name, description, templateType, body })` and `RequestVerificationToken` header — identical to how the origin's `saveVersion` already posts. On `{ templateId }`, navigate to `/Templates/{id}/Edit`. On 400, show `err.message` in the existing error area.
7. Remove the form submit path; the Create button no longer submits a form (`type="button"`).

### Acceptance criteria

- `POST /Templates/Create` with JSON body creates a template with an initial version; response `{ templateId }`.
- Duplicate name returns 400 with `message` showing the duplicate error.
- Missing name returns 400.
- Editor JS create flow redirects to Edit and the created template renders.
- All other endpoints unchanged.

---

## F2 — Server-side sample-data generation

### Problem in the origin

The origin only generates sample data client-side from template tokens (`_tbGenerateSampleFromHtml` / `_tbGenerateSampleFromTemplate`). For SQL-view-backed templates there is no way to auto-populate the preview model from the actual view schema, and there is no way to persist generated sample data with the template.

### Design

Three moving parts, copied from the fork:

**2a. `SampleDataGenerator` service (Application layer)**

Port `SampleDataGenerator.cs` verbatim (behavior must match):

- `GenerateAsync(string? viewName, string? templateBody, CancellationToken)` → `Dictionary<string, object?>` (case-insensitive keys).
- View columns: up to 50 columns, typed values (`ValueForColumn`) honoring column type and length.
- Template tokens: parse with Scriban, walk the AST:
  - `model.X` member expressions → scalar keys (max 50), value inferred from the key name (`InferKind`: email/phone/date/decimal/int/bool/string with realistic values like `jane.doe@agency.gov`, `(860) 555-0142`, `2026`, `1250.00`).
  - `for x in model.Items` loops → array of 3 item dictionaries with the loop's member fields.
  - Parsing errors → return what was collected so far (no throw).
- If both view and body are provided, view columns win on key collisions (body tokens only fill keys not already present).

**2b. Column metadata (origin Infrastructure/Application dependency)**

The origin's `SqlColumnInfo` is a positional record `(Name, DataType)` — no length/nullability. The generator needs `MaxLength` (clip long strings) and `IsNullable`. Extend the discovery query with `CHARACTER_MAXIMUM_LENGTH` and `IS_NULLABLE` (reference: fork `SqlViewDiscoveryService`), and change the DTO to a record with init-style properties (or add a 3rd/4th positional parameter **only** if the repo-wide search proves nothing constructs `SqlColumnInfo` positionally outside the discovery service; records with more than two positional params are the classic source-breaking change — verify every construction site first).

**2c. Endpoints + persistence (Editor layer)**

- `POST /Templates/Api/SampleData/Generate` — body `{ viewName, templateBody }` → `{ sampleData }` (dictionary).
- `PUT /Templates/{id}/SampleData` — body `{ sampleData }` → saves to the template (see below) → `{ saved: true }`.
- **Domain change:** add a `SampleData` property to `Template` (the fork added it as a nullable string holding the JSON text). It must be persisted (EF Core: new string column; include in migrations). Not part of any version — it's template-level metadata for the editor's convenience.
- `TemplateRepository`: nothing new — `UpdateTemplateAsync` already persists the entity; the property is saved with it.
- DI: register `ISampleDataGenerator` in `AddTemplateBuilderEditor`.

**2d. Editor UI (JS/CSS)**

Port the fork's behaviors:

- "Auto-fill from template" button with a data attribute selecting mode: `view` (SQL view columns), `tokens` (template tokens), `both` (view columns + token fills).
- Generated JSON is written into the preview-model textarea (pretty-printed).
- "Save sample data with template" button (`btn-gen-save`) persists the preview model via `PUT /Templates/{id}/SampleData`.
- On load, if saved sample data exists, prefill the preview-model textarea; if none, auto-generate from `both` once (fork: `if (!cta) await generateSampleData('both')`).
- The reference model for preview (`modelJson`) is initialized from saved sample data on Edit load, so Preview works immediately without manual model entry.

### Acceptance criteria

- `GenerateAsync` unit tests: view-only, tokens-only, both, loops with 3 items, kind inference, max-column/key caps, parse-error tolerance. (Fork has these tests in `tests/TemplateBuilder.Application.Tests` — port them.)
- SQL-view discovery returns `MaxLength`/`IsNullable` (new tests).
- Edit page: auto-fill buttons work in all three modes; save persists; reload prefills; preview works with generated data.
- Migration adds `SampleData` column; existing templates unaffected (nullable).

---

## F3 — Scriban syntax reference catalog + editor panel

### Problem in the origin

The editor gives template authors no in-product syntax reference; they must know Scriban from memory or external docs.

### Design

1. **Application layer (static, no dependencies):** port `ScribanReferenceCatalog.cs` verbatim — `ScribanReferenceEntry { Group, Label, Code, Expected? }` and a static `Entries` list covering: Loops (simple, separator), Conditionals (if/else, value-exists), Dates (format, datetime, today), Strings (upcase, capitalize, escape, truncate), Numbers (round, fixed decimals), Missing values (fallback), Whitespace (trim).
2. **Editor UI:** a collapsible reference panel in the editor sidebar listing entries grouped by `Group`; each entry shows `Label`, the `Code` snippet, and `Expected` output when present; clicking a code snippet copies it into the editor caret position (same mechanism the field palette uses to insert at the cursor).
3. No endpoint needed — the catalog is static and bundled into the editor JS/CSS via a rendered script tag or inline JSON.

### Acceptance criteria

- Panel renders all 7 groups with code + expected output.
- Click-to-insert places the snippet at the caret and focuses the editor.
- Panel toggle persists per-session like other editor UI state.

---

## F4 — Palette search, used-field marks, model badges

### Problem in the origin

With many view columns, the field palette becomes unusable; nothing shows which fields the template actually references.

### Design (JS/CSS only, no server change)

1. **Palette search:** a text input above the field list filters columns case-insensitively (substring) as the user types.
2. **Used-field marks:** on load and after each body change (debounced), the editor scans the editor contents for references and marks palette entries. Fork implementation is regex-based — port it exactly:
   - scalars: `/\{\{-?\s*model\.(\w+)\s*-?\}\}/g`
   - loops: `/\{\{-?\s*for\s+\w+\s+in\s+model\.(\w+)\s*-?\}\}/g`
   - matching fields get CSS class `palette-field--used` plus a checkmark span (`.palette-field-used-mark`) inside the palette row.
3. **Model badges:** the preview-model JSON keys are shown as removable chips above the preview-model textarea; removing a chip deletes the key from the JSON; typing in the textarea refreshes the chips.

### Acceptance criteria

- Search filters the palette in real time.
- Fields referenced in the body are visibly marked; marking updates as the body changes.
- Chips reflect the current model JSON keys; removal edits the JSON.

---

## F5 — Editor CSS scoping under `#tb-editor-host`

### Problem in the origin

The editor's CSS targets generic element selectors (`.modal`, `input[type=...]`, `.btn`, etc.) with no scoping. Consumers embedding the editor into an existing app (the origin's own sample host includes Bootstrap and other CSS) can suffer style collisions. The fork proved the fix: all editor styles are scoped under a single `#tb-editor-host` wrapper that the editor DOM root carries.

### Design

1. Wrap all editor CSS rules under `#tb-editor-host` (selector prefixing). Fork's `template-editor.css` is the reference — every rule there is scoped; port the same scoping discipline to the origin's stylesheet.
2. The editor root element gets `id="tb-editor-host"` (rendered by the editor partial — verify the partial actually renders the wrapper element).
3. Keep layout/theme overrides (z-index for modal overlays above the consumer's chrome) working under the wrapper.

### Acceptance criteria

- With Bootstrap (or any other CSS framework) loaded on the consumer page, the editor renders identically to the isolated case.
- No editor style leaks outside `#tb-editor-host` (spot-check the rendered stylesheet).
- The sample host gains a test page that loads Bootstrap 3.3.7 and asserts the editor is visually intact (fork risk note: Bootstrap 3 vs the editor is the exact collision scenario).

---

## F6 — Dual model access (top-level `X` and `model.X`)

### Problem in the origin

The origin's engine always wraps the model under `model` (via `CaseInsensitiveScriptObject` + `MemberRenamer`), so `{{ X }}` renders empty while `{{ model.X }}` works. The fork's engine imports the model **both** at global scope and under `model`, so both access styles work. Template authors (especially non-developers) write `{{ FirstName }}` naturally; on the origin this silently produces blank output.

### Design

1. **Engine change (Application):** in the render path, after building the model `ScriptObject`:
   - always set `model` (current behavior),
   - additionally import the model members at global scope,
   - **collision protection:** if the model itself contains a top-level key named `model`, do not overwrite it (the fork's `ContainsKey("model")` guard — the user model wins; a model key literally named `model` shadows the wrapper).
2. **Option gate:** add `AllowTopLevelModelAccess` (bool, default `false`) to `TemplateBuilderOptions` so existing consumers keep the documented `model.*` contract unless they opt in. When disabled, current behavior is unchanged (the fork has no option because it shipped this behavior from the start; the origin has an existing documented contract, hence the option).
3. **Documentation caveat (documented in the option XML doc / README):** top-level members can shadow Scriban's built-in function groups if a model key collides with a builtin name (`date`, `string`, `math`, `html`, `array`, `object`, `for`, `if`, etc.). The `model.*` form always wins for safety. This is exactly why the option exists; consumers enabling it should be aware.

### Acceptance criteria

- Default: `{{ model.X }}` works, `{{ X }}` renders empty (unchanged).
- With `AllowTopLevelModelAccess = true`: `{{ X }}` and `{{ model.X }}` both render; model key `model` at top level is not clobbered.
- Case-insensitivity composes: `{{ x }}` works for model key `X` (both styles) — `CaseInsensitiveScriptObject` already provides this; the top-level import must use the same case-insensitive object.
- Unit tests for: top-level access on/off, `model` key collision, builtin-name collision caveat (behavior documented, not tested as error), preview endpoint rendering.

---

## Cross-feature dependencies

- F2 **requires** the `SqlColumnInfo` extension (`MaxLength`, `IsNullable`) — no other feature depends on it.
- F1 and F2 both touch `template-editor.js`; their JS changes are independent but land in the same file — coordinate to avoid merge churn (implement sequentially, same PR or stacked PRs).
- F3/F4 are JS/CSS-only and independent of F1/F2.
- F5 is CSS-only; F3/F4 add CSS too — F5's scoping pass should happen after F3/F4 land so their styles are scoped from birth (or the scoping pass covers them).
- F6 is Application-layer only; no UI changes.

## Non-goals (explicitly out of scope)

- No change to the `model.*`-only contract for existing consumers (F6 is opt-in).
- No client-side-only token generation changes — the origin's existing client-side generation stays; F2 adds the server-side path alongside it.
- No multi-tenancy, no data-type-aware editor controls (beyond what the fork already ships).
- No changes to the fork repo (`TemplateBuilder.Mvc5`). This document is a one-way handoff.
- No UI framework changes (vanilla JS/CSS, same as origin/fork today).

## Known risks

1. **`SqlColumnInfo` DTO change is additive but shared** — verify no existing consumer of `GetViewColumnsAsync` does its own result-shape comparison (unit tests may assert exact column count/props).
2. **EF Core migration for `Template.SampleData`** — the origin's `MigrationHostedService` runs `MigrateAsync` on startup; the new column must be in a new migration and covered by the EF migration tests the origin runs.
3. **F6 builtin shadowing** — if a real-world consumer has a model key named `date`/`string`/etc. and enables the option, templates break subtly. The option default-off plus the documented caveat mitigates; do not "fix" Scriban internals to avoid it.
4. **F5 collision risk remains a fork-proven, origin-unproven claim** — the origin's sample host must load Bootstrap 3.3.7 (the exact scenario from the fork's risk register) before claiming scoping is sufficient.
5. **JS size** — the fork's editor JS is ~2,335 lines vs origin ~2,098; ports add ~250 lines net. Keep the bundle structure (single `template-editor.js`) and avoid introducing a build step.

## Fork point reference

Fork diverged from origin before all six features existed; the fork's commit history for these features: `f130d2a` (F6: preview `model.*` fix), `4261fa3` (F1: JSON Create). F2–F5 landed as part of the fork's parity work (`docs/superpowers/plans/2026-08-17-parity-editor-ui-implementation.md`).