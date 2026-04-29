# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This repo parses the Belgian SAM (Système d'Authentification Médicaments / Système de fichiers pharmaceutiques) database exports. SAM is the Belgian national medicinal product database maintained by eHealth.

The project uses XSD schemas (v6.0.2) from the SAM web service (`urn:be:fgov:ehealth:samws:v2:*`) to generate C# classes for deserializing the SAM XML export files.

## Solution structure

- `samparse/` — Class library containing the XSD schemas, generated C# serialization classes, `SamLoader`, `ApplicabilityChecker`, `QualificationListResolver`, and `ChapterIvAnalysis`
- `trial/` — Console app that references `samparse` — kept as a scratchpad for small experiments
- `trial/SAM/` — Sample SAM XML export files (CPN, REF, VMP, AMP, RML, CMP, IMPP, RMB, NONMEDICINAL, CHAPTERIV)
- `analysis/` — Console app for ad-hoc investigations over the dataset (qualification variance, name resolution, etc.)
- `DisplayParagraph/` — Blazor Web App (Interactive Server + WASM) for visualising Chapter IV paragraph verses
- `docs/` — Additional domain/design notes (see `docs/chapter-iv-applicability.md` for how paragraph applicability is derived)

## Build and run

```bash
# Build the solution
dotnet build samparse.slnx

# Run the trial console app
dotnet run --project trial/trial.csproj

# Run the Blazor app
dotnet run --project DisplayParagraph/DisplayParagraph/DisplayParagraph.csproj
```

## Regenerating the generated classes

The `samparse/Generated/` folder contains C# classes auto-generated from the XSD schemas using [XmlSchemaClassGenerator](https://github.com/mganss/XmlSchemaClassGenerator) (`xscgen`).

To regenerate, run from `samparse/`:
```powershell
.\xscgen-parse.ps1
```

**After regenerating**, two manual fixes are required (see README.md — this is a known issue, TODO to automate):
1. Remove the empty `partial class StandardResponseType` that causes a circular reference.
2. Remove the `[XmlIncludeAttribute(typeof(Be.Fgov.Ehealth.Samws.V2.Core.StandardResponseType))]` line above the real `StandardResponseType` definition.

Also resolve any namespace conflict where the same XML element name maps to two different C# types in the same namespace (e.g. `Text255Type` vs `TextType`). The resolution applied here is to use the more permissive type (`TextType`). Affected fields: `Title`, `Type`, `AdditionalInformation`, `Impact` in the reimbursement law and actual medicine common namespaces.

## Key namespaces

| C# namespace | SAM domain |
|---|---|
| `Be.Fgov.Ehealth.Samws.V2.Export` | Export root types (`ExportCompaniesType`, etc.) |
| `Be.Fgov.Ehealth.Samws.V2.Core` | Shared base types |
| `Be.Fgov.Ehealth.Samws.V2.Actual.*` | AMP (Actual Medicinal Products) |
| `Be.Fgov.Ehealth.Samws.V2.Virtual.*` | VMP (Virtual Medicinal Products) |
| `Be.Fgov.Ehealth.Samws.V2.Refdata` | Reference data |
| `Be.Fgov.Ehealth.Samws.V2.Company.Submit` | Company data |

## DisplayParagraph Blazor app

The `DisplayParagraph/` folder contains a Blazor Web App (Interactive Server render mode) with a single feature page: **Paragraph Viewer** (`/paragraph-viewer`).

### Architecture

- `DisplayParagraph/DisplayParagraph/` — server project; references `samparse`
- `DisplayParagraph/DisplayParagraph.Client/` — WASM client (currently only the Counter demo page)
- `DisplayParagraph/DisplayParagraph/Services/SamDataService.cs` — singleton that eagerly loads `SamDatabase` (CHAPTERIV + REF only) at app startup via `Program.cs` calling `GetRequiredService<SamDataService>()` before `app.Run()`. Other exports are skipped via the `SamExport` flags enum on `SamLoader.Load` to avoid the ~2.4 GB full-dataset cost.

### SAM data path

The server reads the SAM XML files from a path configured in `appsettings.json`:

```json
"SamDataPath": "../../trial/SAM"
```

The path is resolved relative to the server project's content root (i.e. `DisplayParagraph/DisplayParagraph/`), so the default points at the shared `trial/SAM/` folder. Override in `appsettings.Development.json` if your files live elsewhere.

### Paragraph Viewer page

Inputs: paragraph name, reference date (defaults to today), patient sex, patient date of birth (the resolved age on the reference date is shown next to the field).

On load it finds all `VerseFullDataType` entries for the paragraph, selects the one `VerseDataType` per verse that is active on the reference date (`From ≤ date` and either `ToSpecified` is false or `To ≥ date`), then displays them in a table:

| Column | Source |
|---|---|
| Seq | `VerseFullDataType.VerseSeq` |
| Verse # | `VerseDataType.VerseNum` |
| Text (NL) | `VerseDataType.TextNl`, indented by `VerseLevel × 24 px` |
| Sex | `VerseDataType.SexRestricted` (♀ / ♂) when `SexRestrictedSpecified` |
| Age limits | Formatted from `MinimumAgeAuthorized` / `MaximumAgeAuthorized` + unit |

Rows where the patient's sex or age falls outside the verse restriction are highlighted in yellow.

## Deserializing SAM XML

Use `XmlSerializer` with the appropriate export type. Example from `trial/Program.cs`:

```csharp
var serializer = new XmlSerializer(typeof(ExportCompaniesType));
using var stream = File.OpenRead("./SAM/CPN-1775613602318.xml");
var result = (ExportCompaniesType)serializer.Deserialize(stream);
```

The SAM XML filenames encode the export type and a timestamp: `{TYPE}-{timestamp}.xml`.
