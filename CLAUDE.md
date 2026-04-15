# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This repo parses the Belgian SAM (Système d'Authentification Médicaments / Système de fichiers pharmaceutiques) database exports. SAM is the Belgian national medicinal product database maintained by eHealth.

The project uses XSD schemas (v6.0.2) from the SAM web service (`urn:be:fgov:ehealth:samws:v2:*`) to generate C# classes for deserializing the SAM XML export files.

## Solution structure

- `samparse/` — Class library containing the XSD schemas and generated C# serialization classes
- `trial/` — Console app that references `samparse` and experiments with deserializing SAM XML files
- `trial/SAM/` — Sample SAM XML export files (CPN, REF, VMP, AMP, RML, CMP, IMPP, RMB, NONMEDICINAL, CHAPTERIV)

## Build and run

```bash
# Build the solution
dotnet build samparse.slnx

# Run the trial console app
dotnet run --project trial/trial.csproj
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

## Deserializing SAM XML

Use `XmlSerializer` with the appropriate export type. Example from `trial/Program.cs`:

```csharp
var serializer = new XmlSerializer(typeof(ExportCompaniesType));
using var stream = File.OpenRead("./SAM/CPN-1775613602318.xml");
var result = (ExportCompaniesType)serializer.Deserialize(stream);
```

The SAM XML filenames encode the export type and a timestamp: `{TYPE}-{timestamp}.xml`.
