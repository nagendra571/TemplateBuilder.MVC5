# Design: Lifecycle & Ops (export/import, health check, bulk ops) — TemplateBuilder.Editor (origin)

**Date:** 2026-08-21
**Audience:** implementer in the origin repo `github.com/nagendra571/TemplateBuilder` (private). Ports the fork's lifecycle-ops feature (already implemented and verified in `TemplateBuilder.Mvc5`) with deliberate stack adaptations. Requires the two-state save model first (spec `2026-08-21-origin-two-state-save-design.md`) — per-version `IsActive` is part of the export format.

## Goal

Add the third phase of the product roadmap on top of the origin's editor:

1. **Export/import (JSON incl. version history)** — promote templates dev→prod via a versioned JSON file matched by a stable external key.
2. **Template health check** — detect field drift between template bodies and the live SQL view schema.
3. **Bulk operations** — checkbox multi-select with Activate / Deactivate / Export (ZIP) / Delete.

## Decisions (approved by the product owner)

| # | Decision | Rationale |
|---|---|---|
| L1 | Export format `schemaVersion: 2`, mirroring the fork's v2 shape **minus `sampleData`** | File interchangeability with the fork where both products coexist; the origin's `main` has no SampleData column (1.6.0-only) — the field is reserved for when it lands |
| L2 | `Template.ExternalKey` (Guid, unique) as the cross-environment identity | Survives renames; unambiguous dev→prod link; NEWID() backfill for legacy rows |
| L3 | Import upsert: match by key → update-in-place (append versions); no match → create; **no skip/collapse** | No Review/Approved statuses exist in the origin (two-state model); import preserves per-version `isActive` and template `isActive` exactly |
| L4 | Import transport = multipart file upload (`[FromForm] IFormFile`) | Same UX as the fork; ASP.NET Core binds it natively |
| L5 | Health check parses bodies with the **Scriban AST** (not regex) | No false positives from literals/comments; nested paths and loop collections covered (same algorithm as the fork) |
| L6 | `Template.SourceView` (nvarchar 200) + `SourceViewSnapshot` (nvarchar max, `{ takenAt, columns }`) | Drift comparison needs a declared source of truth per template; snapshots are environment-local and NOT exported |
| L7 | Health findings: `column_missing`/`view_missing` Critical; `column_type_changed`/`column_length_changed`/`column_nullability_changed`/`unbound_tokens` Warning | Same severity model as the fork |
| L8 | Bulk ops: Activate / Deactivate / Export ZIP / Delete only | Housekeeping story |
| L9 | `ITemplateRepository.DeleteAsync(int id)` → `Task<bool>`; bulk delete removes versions then the template | FK `NoAction` on Versions + `SetNull` on CurrentVersionId require explicit ordering |
| L10 | New `GetAllIncludingInactiveAsync` for health page + bulk ops | The origin's `GetAllAsync` filters `IsActive`; ops tooling must see all templates |
| L11 | System.Text.Json everywhere (camelCase); **no Newtonsoft** | Origin stack |
| L12 | DI registration in `AddTemplateBuilderEditor` only | Lifecycle is UI/ops; the render-only Core package is unaffected |
| L13 | **No audit wiring** | The origin has no audit log (fork-native); bulk delete records nothing — documented divergence from the fork |
| L14 | Version: **2.1.0** (additive, after the 2.0.0 two-state release) — or fold into 2.0.0 if released together | SemVer: additive feature after a breaking release is a minor |

## Current state (origin — verified against `main`, commit 194cf15)

- `Template` has no `ExternalKey`/`SourceView`/`SourceViewSnapshot`; no `DeleteAsync` anywhere; `GetAllAsync` filters `IsActive` (L10).
- `SqlViewDiscoveryService` exists with `ViewPrefix`/`ViewAllowlist`/excluded-schema filtering and an `IMemoryCache` — reuse for live schema columns (`GetViewColumnsAsync(viewName)` returns `IReadOnlyList<SqlColumnInfo>` where `SqlColumnInfo { Name, DataType, MaxLength, IsNullable }` in `Application/DTOs/`).
- `TemplateEngine` serves the last Active version (post-two-state) — health checks the **latest** version (drafts included), which is what the editor shows.
- Controllers: attribute-routed; JSON via `Ok(...)`/`[FromBody]`; antiforgery via `[ValidateAntiForgeryToken]` + `RequestVerificationToken` header.
- The editor index page has a plain table (no checkboxes/health badges); the edit page has a Properties panel with no Source View select (the palette's view selector exists separately in the canvas).
- ZIP: `System.IO.Compression` is in the ASP.NET Core shared framework — no package needed.
- Migrations: `dotnet ef migrations add <Name> --project src/TemplateBuilder.Infrastructure`; design-time factory exists; `MigrationHostedService` applies at startup.
- Reference implementation: fork commits `fbc1e54`..`46fc1f9` (lifecycle phase) — stack mapping table in the two-state spec applies (EF Core, System.Text.Json, RCL views, MS DI).

## Module 1 — Export / Import

### File format (schemaVersion 2 — camelCase, indented)

```json
{
  "schemaVersion": 2,
  "exporter": { "name": "TemplateBuilder.Editor", "version": "2.1.0" },
  "exportedAt": "2026-08-21T12:00:00Z",
  "template": {
    "externalKey": "7f2c4b1e-...",
    "name": "Invoice v3",
    "templateType": "Email",
    "description": "...",
    "isActive": true,
    "versions": [
      {
        "versionNumber": 1,
        "body": "<p>Hi {{ model.FirstName }}</p>",
        "changeComment": "Initial version",
        "createdAt": "2026-08-19T09:00:00Z",
        "createdBy": "nchinnam",
        "isActive": true
      }
    ]
  }
}
```

(`sampleData` deliberately absent — L1. Import must tolerate its absence and, for forward-compatibility, ignore it if present.)

### Endpoints

| Route | Method | Returns |
|---|---|---|
| `Templates/Export/{id:int}` | GET | JSON file attachment (`Content-Disposition: attachment; filename={sanitized}.template.json`) |
| `Templates/Import` | POST (multipart `IFormFile file`) | `TemplateImportResult` JSON (`{ created, updated, skipped, errors }`; skipped stays empty in the origin) |
| `Templates/BulkExport` | POST JSON `{ ids }` | ZIP attachment (`template-builder-export.zip`: per-template `.template.json` + `_summary.json` schemaVersion 2) |
| `Templates/BulkActivate` / `BulkDeactivate` / `BulkDelete` | POST JSON `{ ids }` | `{ succeeded, failed }` JSON |

### Import rules

1. Parse JSON (camelCase). Failure → single error entry.
2. `schemaVersion != 2` → rejected.
3. Missing name/type or zero versions → rejected.
4. Every version body must parse via `Scriban.Template.Parse` (HasErrors → rejected with the version number).
5. Key match → update metadata + `IsActive`, append versions preserving each `isActive`, continuing from `max + 1`.
6. No key match → create template (ExternalKey from file or new Guid) with original version numbers and flags preserved.
7. No skip/collapse (L3). `Skipped` always empty.

## Module 2 — Health check

- `TemplateHealthService` (Application): Scriban AST walk for `model.*` paths (leaf filtering — same algorithm as the fork), snapshot deserialization (`{ takenAt, columns }`), live columns via `ISqlViewDiscoveryService.GetViewColumnsAsync`, findings per L7.
- Missing view = empty column list from discovery (the origin's discovery returns an empty list for a nonexistent view — treat `Count == 0` as `view_missing`).
- Endpoints: `GET /Templates/{id:int}/Health` (report JSON), `GET /Health` (RCL view: Healthy/Warnings/Critical/Unbound chips + per-template finding table), `GET /Health/Summaries?ids=1,2` (badge data).
- Editor: Source SQL View select in the Properties panel (`prop-source-view`, fed from the existing `AvailableViews`), saved via SaveVersion; when it changes, refresh `SourceViewSnapshot` via `BuildSnapshotJsonAsync(viewName)`. Health button in the footer renders findings inline.
- Index: `tb-health-badge` per row fed by `/Health/Summaries` (severity-styled via existing badge token classes).

## Module 3 — Bulk operations

- Index table gains checkbox column (`tb-row-check`), select-all, and a bulk toolbar (hidden until selection): Activate / Deactivate / Export ZIP / Delete / Clear.
- `List<int>`/`int[]` JSON binding works natively in ASP.NET Core (no fork-style model-binder workaround).
- Bulk toggle: fetch per id, skip if already in target state, `UpdateTemplateAsync`, `succeeded`/`failed` split.
- Bulk delete: audit N/A (L13); `DeleteAsync` must delete versions first then the template (FK `NoAction` on `TemplateId`; `CurrentVersionId` FK is `SetNull` — the fork's approach of nulling CurrentVersionId first is NOT needed on EF Core with `SetNull` configured, but verify the InMemory + SqlServer behaviors in tests).

## Module 4 — Data model & migration

`Template` gains (Domain + `TemplateConfiguration.cs`):

```csharp
public Guid ExternalKey { get; set; } = Guid.NewGuid();
public string? SourceView { get; set; }
public string? SourceViewSnapshot { get; set; }
```

Configuration:

```csharp
builder.Property(t => t.ExternalKey).IsRequired();
builder.HasIndex(t => t.ExternalKey).IsUnique();
builder.Property(t => t.SourceView).HasMaxLength(200);
builder.Property(t => t.SourceViewSnapshot).HasColumnType("nvarchar(max)");
```

Migration `AddLifecycleOps` (scaffolded, then hand-added):

```csharp
migrationBuilder.AddColumn<Guid>(name: "ExternalKey", table: "Templates", type: "uniqueidentifier", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
migrationBuilder.AddColumn<string>(name: "SourceView", table: "Templates", type: "nvarchar(200)", maxLength: 200, nullable: true);
migrationBuilder.AddColumn<string>(name: "SourceViewSnapshot", table: "Templates", type: "nvarchar(max)", nullable: true);
migrationBuilder.Sql("UPDATE dbo.Templates SET ExternalKey = NEWID() WHERE ExternalKey = '00000000-0000-0000-0000-000000000000'");
migrationBuilder.CreateIndex(name: "IX_Templates_ExternalKey", table: "Templates", column: "ExternalKey", unique: true);
```

`ITemplateRepository` additions:

```csharp
Task<bool> DeleteAsync(int id, CancellationToken ct = default);
Task<IReadOnlyList<Template>> GetAllIncludingInactiveAsync(CancellationToken ct = default);
```

## Module 5 — Testing & verification

- **Application.Tests**: promotion service (export shape v2 incl. per-version `isActive`; import create/update/flag-preservation/schema-reject/scriban-reject; bulk ZIP contents + `_summary.json`); health service (token extraction incl. nested loops/conditionals/literals; missing view; type/length/nullability drift; unbound warning).
- **Editor.Tests** (Moq): Export/Import/Bulk endpoints (payload shapes, `IFormFile` path), health endpoints, DeleteAsync behavior.
- **Infrastructure.Tests** (InMemory): `ExternalKey` round-trip + unique violation; `DeleteAsync` removes versions then template; `GetAllIncludingInactiveAsync` includes inactive.
- **e2e** (Web at `https://localhost:7275/`): create → export (schemaVersion 2) → import (created) → import again (updated, versions appended, flags preserved) → health page + badges (bind a view, drop a column via sqlcmd, re-check → critical/warning) → bulk select → deactivate/activate → export ZIP (`unzip -l` shows entries + `_summary.json`) → delete (rows gone). `GET /Templates/_setup` green.
- **Pack**: nupkgs inspected; README What's New sync (repo lesson).

## Out of scope (future work)

- Audit log (fork-native).
- Single-template delete UI (bulk delete only).
- Import of `sampleData` (arrives with 1.6.0 on `main`).
- Export/import of snippets.

## Port/fork deviation log

- `sampleData` omitted from the export format (origin `main` lacks the column).
- No skip/collapse on import (no statuses).
- No audit records for bulk ops (no audit log).
- `GetAllIncludingInactiveAsync` added (origin's `GetAllAsync` filters `IsActive`).
- Bulk IDs bind natively (`List<int>`), no fork-style `BulkIdsRequest` workaround needed.
