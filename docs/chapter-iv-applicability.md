# Chapter IV paragraph applicability

This document explains how to decide whether a Chapter IV paragraph *could* apply to a given patient/doctor. Applicability is **not** stored on the paragraph — it is derived from the tree of verses and their individual restrictions.

The companion C# API lives in `samparse/Applicability.cs` (`PatientContext`, `DoctorContext`, `ChapterIvIndex`, `ApplicabilityChecker`).

## 1. Where restrictions live

Every restriction is attached to a `VerseDataType` (the versioned payload of a `VerseFullDataType`), never to the paragraph itself. The relevant fields on `VerseDataType`:

| Field | Meaning |
|---|---|
| `SexRestricted` + `SexRestrictedSpecified` | `F` or `M`; verse only applies to that sex |
| `MinimumAgeAuthorized` / `MaximumAgeAuthorized` (+ `Unit` + `Specified`) | Inclusive age bounds, expressed in `D`/`W`/`M`/`Y` |
| `PurchasingAdvisorQualList` | FK (string key) to a `QualificationListFullDataType` listing authorised prescriber codes |
| `From` / `To` | Validity window (from `DataPeriodType`) — pick the one data row active for the reference date |

A paragraph like "only for females" is therefore an emergent property of the combination of restrictions on verses that must be satisfied.

## 2. Verse tree

Within a paragraph, verses form a tree built from:

- `VerseSeq` — id within the paragraph (on `VerseFullDataType`, inherited from `VerseKeyType`)
- `VerseSeqParent` — parent's `VerseSeq` (on `VerseDataType`)
- `VerseLevel` — depth hint (redundant with the parent chain)

`VerseSeqParent = 0` typically points at a synthetic root; any verse whose parent is not itself present (after date filtering) is treated as a root in the walk.

## 3. Verse roles — three kinds

| Role | Signal | Effect on applicability |
|---|---|---|
| **Narrative / header** | `CheckBoxInd == false` | Descriptive text. Own restrictions still apply to it and anyone "entering" its subtree. Not a choice point. |
| **Mandatory choice group** | A parent verse has `MinCheckNum > 0`, and its children with `CheckBoxInd == true` are the alternatives | At least `MinCheckNum` checkbox children must be satisfiable for the patient/doctor. Fewer ⇒ paragraph **not applicable**. |
| **Optional choice** | Parent has `MinCheckNum == 0` | Children don't constrain the paragraph. |

From the XSD:
> Every verse with a checkbox has to have a parent verse with a granted `MIN_CHECK_NUM`. And vice versa, every verse with `MIN_CHECK_NUM > 0` has to have minimum two children verses with `CHECK_BOX_IND = true`.

Frequency in the reference export:
- `<CheckBoxInd>true</CheckBoxInd>` — ~19 648 occurrences (very common)
- `<MinCheckNum>` — ~9 841 occurrences (common)
- `<AndClauseNum>` — 8 occurrences (niche; see §5)

## 4. Qualification lists (doctor side)

- `VerseDataType.PurchasingAdvisorQualList` is a short string key such as `"962"`.
- It resolves against `ExportChapterIvType.QualificationList` → `QualificationListFullDataType` whose `QualificationList` attribute matches.
- That list contains a collection of `ProfessionalAuthorisation` entries. Each entry's `Data` is versioned (`From`/`To`) and carries a `ProfessionalCv` (the professional code, e.g. `"668"`).
- A doctor with any `ProfessionalCv` in the list's active entries for the reference date **qualifies**.
- `QualificationListDataType.ExclusiveInd` defines AND/OR semantics in the schema (`"1"` = OR, `"2"` = AND) but is **absent in the real data** we inspected. Treat as OR.

## 5. `AndClauseNum` — rare cross-branch coupling

`AndClauseNum` groups checkbox verses across *different, non-contiguous* branches that must all be selected together. If any member of an AndClause is not satisfiable, the whole group fails and every other branch that relies on it fails too.

Only 8 occurrences in the reference export, so skipping this in v1 is a reasonable trade-off. It is **not currently modelled** by `ApplicabilityChecker`.

## 6. The algorithm

Recursive tree walk:

```
CouldApplyTo(paragraph, patient, doctor, date, index):
  pick active VerseDataType per verse for `date`
  build children-by-parent map
  walk every root (verse whose parent isn't in the active set)
  combine with Worst()

EvaluateChoice(verseSeq):
  self = VerseCompatible(verseData, patient, doctor, date, index)
  if self == NotApplicable: return NotApplicable
  return Worst(self, EvaluateSubtree(verseSeq))

EvaluateSubtree(verseSeq):
  narrativeKids, checkboxKids = split children by CheckBoxInd
  narrative = Worst over EvaluateChoice(k) for k in narrativeKids
  if narrative == NotApplicable: return NotApplicable

  mandatory = Applicable
  if MinCheckNum > 0 and checkboxKids not empty:
    results = EvaluateChoice(k) for k in checkboxKids
    definitely = count Applicable
    possibly   = count != NotApplicable
    if possibly < MinCheckNum: return NotApplicable
    if definitely < MinCheckNum: mandatory = Unknown

  return Worst(narrative, mandatory)
```

`VerseCompatible` runs three independent predicates (sex / age / qualification) and combines with `Worst`. Each predicate returns `Applicable` when the restriction is absent or satisfied, `NotApplicable` when it is violated, and `Unknown` when the restriction exists but the relevant patient/doctor datum is missing.

`Worst(a, b)` = `NotApplicable` if either is `NotApplicable`, else `Unknown` if either is `Unknown`, else `Applicable`. This makes the three-valued logic monotonic.

## 7. Interpretation & caveats

- **`Applicable` means "not provably unreachable"**, not "this patient definitely qualifies". Natural-language criteria in the verse text (diagnosis, prior treatments, etc.) aren't evaluable here.
- **Non-checkbox verses are treated as mandatory narrative.** If a narrative verse carries e.g. `SexRestricted = F` and the patient is male, the whole paragraph is reported `NotApplicable`. This matches the working assumption that narrative headers set the scope for their subtree. If you find real paragraphs where this is too strict, relax it.
- **Unknown inputs propagate to `Unknown`.** Missing patient sex + a sex-restricted verse ⇒ we don't commit to a verdict. Same for age and doctor qualifications.
- **`AndClauseNum` is ignored** (§5). Adding it would require a second pass that groups checkboxes by clause and reports the group `NotApplicable` if any member fails.
- **Time-versioned constraints.** `VerseDataType` and `QualificationListDataType` both carry `From`/`To`; the reference date filters both.
- **Qualification list semantics.** OR, per §4. Switch to AND if you encounter a real list with `ExclusiveInd = "2"`.

## 8. Smoke test

Hand-checked against the April 2026 SAM export (`CHAPTERIV-1775613877335.xml`):

| Paragraph | Input | Result | Why |
|---|---|---|---|
| `12850000` (osteoporosis) | anything | `Applicable` | No mandatory restrictive choice group at the top level — patient-agnostic at the paragraph level |
| `13780000` (CLL) | female, age 10 | `NotApplicable` | Age < 18 fails a mandatory age check |
| `13780000` | female, age 70, CV `"001"` | `NotApplicable` | Doctor CV not in qualification list `962` |
| `13780000` | female, age 70, CV `"668"` | `Applicable` | CV `"668"` is in qualification list `962` |
| `13780000` | female, age 70, no doctor | `Unknown` | Qualification requirement exists, doctor info missing |
