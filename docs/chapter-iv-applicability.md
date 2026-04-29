# Chapter IV paragraph applicability

This document explains how to decide whether a Chapter IV paragraph *could* apply to a given patient/doctor for a given prescription. Applicability is **not** stored on the paragraph — it is derived from the tree of verses and their individual restrictions.

The companion C# API lives in `samparse/Applicability.cs` (`PatientContext`, `DoctorContext`, `ChapterIvIndex`, `ApplicabilityChecker`).

## 1. Where restrictions live

Every restriction is attached to a `VerseDataType` (the versioned payload of a `VerseFullDataType`), never to the paragraph itself. The relevant fields:

| Field | Meaning |
|---|---|
| `SexRestricted` (+ `Specified`) | `F` or `M`; verse only applies to that sex |
| `MinimumAgeAuthorized` / `MaximumAgeAuthorized` (+ `Unit` + `Specified`) | Inclusive age bounds, expressed in `D` / `W` / `M` / `Y` |
| `PurchasingAdvisorQualList` | FK (string key) to a `QualificationListFullDataType` listing authorised prescriber codes |
| `RequestType` (+ `Specified`) | `N` = new prescription only, `P` = renewal only, absent = either |
| `VerseType` (+ `Specified`) | Currently only `E` = exclusion verse (legislation phrased as "X is not authorised") |
| `AndClauseNum` (+ `Specified`) | Couples this verse to other verses sharing the same number across non-contiguous branches |
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

Frequency in the reference export (April 2026):

| Field | Occurrences |
|---|---|
| `<CheckBoxInd>true</CheckBoxInd>` | 19 648 |
| `<MinCheckNum>` | 9 841 |
| `<RequestType>N</RequestType>` | 40 816 |
| `<RequestType>P</RequestType>` | 10 661 |
| `<VerseType>E</VerseType>` | 426 |
| `<AndClauseNum>` | 8 |

## 4. Qualification lists (doctor side)

- `VerseDataType.PurchasingAdvisorQualList` is a short string key such as `"962"`.
- It resolves against `ExportChapterIvType.QualificationList` → `QualificationListFullDataType` whose `QualificationList` attribute matches.
- That list contains a collection of `ProfessionalAuthorisation` entries. Each entry's `Data` is versioned (`From`/`To`) and carries a `ProfessionalCv` (the professional code, e.g. `"668"`).
- A doctor with any `ProfessionalCv` in the list's active entries for the reference date **qualifies**.
- `QualificationListDataType.ExclusiveInd` defines AND/OR semantics in the schema (`"1"` = OR, `"2"` = AND) but no AND lists exist in the reference export. Treated as OR; a real AND list would need a logic change.

## 5. RequestType — new vs. renewal

`RequestType` gates verses by whether the prescription is a first request (`N`) or a renewal/prolongation (`P`). Roughly half of all typed verses in the reference export carry one or the other, so this is a primary filter:

- A verse with `RequestType = N` is only applicable when the prescription is **new**.
- A verse with `RequestType = P` is only applicable when the prescription is a **renewal**.
- A verse without `RequestType` applies to both.

The prescription's request type flows through `CouldApplyTo` / `VerseCompatible` as `bool? isRenewal` (null ⇒ unknown, propagates as `Unknown`).

## 6. `AndClauseNum` — cross-branch coupling

Verses sharing the same `AndClauseNum` value must be co-selectable: the legislation expects the prescriber to engage with all of them together, even when they live in non-contiguous branches of the tree.

`ApplicabilityChecker` models this by precomputing, for each `AndClauseNum` group with two or more members, the combined per-verse compatibility (worst-of) and folding that result back into every member's effective compat. Effect: if any single member of an `AndClauseNum` group is `NotApplicable`, all members become `NotApplicable`, and the constraint propagates naturally through the tree walk.

The reference export has only 8 occurrences (a single group, value `220`, on paragraph `1880100`), so this is rarely load-bearing in practice — but the implementation is in place for future exports.

## 7. `VerseType = E` — exclusion verses

Per the XSD, `VerseType = E` marks a verse whose legal text is phrased as a *prohibition* ("simultaneous reimbursement of X is not authorised") rather than a condition. The reference export has 426 such verses.

The applicability check treats them identically to other verses: an exclusion verse with `RequestType=N` and `SexRestricted=F` still contributes a `NotApplicable` for a male renewal patient via the normal predicates. In practice the sampled exclusion verses describe medicine-level interactions and don't carry sex/age/qual restrictions, so they're functionally informational — the viewer surfaces them with a distinct marker so a reader doesn't mistake "thou shalt not" for "thou shalt".

If a future export contains exclusion verses where the natural reading is *inverted* (i.e. the verse's restrictions describe a prohibited combination rather than a required one), the predicates would need conditional logic. No such verses are observed today.

## 8. The algorithm

Top-level pseudocode (see `samparse/Applicability.cs` for the real thing):

```
CouldApplyTo(paragraph, patient, doctor, isRenewal, date, index):
  active     = pick the VerseDataType per verse active on `date`
  childrenBy = group by VerseSeqParent
  rootSeqs   = verses whose parent isn't in `active`

  # Precompute AndClauseNum group adjustments
  for each group of verses sharing AndClauseNum (size > 1):
    combined = Worst over VerseCompatible(member) for each member
    record andAdj[memberSeq] = combined

  return Worst over EvaluateChoice(r) for r in rootSeqs

EvaluateChoice(verseSeq):
  self = VerseCompatible(verseData, patient, doctor, isRenewal, date, index)
  if andAdj has verseSeq: self = Worst(self, andAdj[verseSeq])
  if self == NotApplicable: return NotApplicable
  return Worst(self, EvaluateSubtree(verseSeq))

EvaluateSubtree(verseSeq):
  narrativeKids, checkboxKids = split children by CheckBoxInd
  narrative = Worst over EvaluateChoice(k) for k in narrativeKids
  if narrative == NotApplicable: return NotApplicable

  mandatory = Applicable
  if MinCheckNum > 0 and checkboxKids not empty:
    results    = EvaluateChoice(k) for k in checkboxKids
    definitely = count Applicable
    possibly   = count != NotApplicable
    if possibly   < MinCheckNum: return NotApplicable
    if definitely < MinCheckNum: mandatory = Unknown

  return Worst(narrative, mandatory)
```

`VerseCompatible` runs four independent predicates (sex / age / qualification / request type) and combines with `Worst`. Each predicate returns `Applicable` when the restriction is absent or satisfied, `NotApplicable` when it is violated, and `Unknown` when the restriction exists but the relevant patient/doctor/prescription datum is missing.

`Worst(a, b)` = `NotApplicable` if either is `NotApplicable`, else `Unknown` if either is `Unknown`, else `Applicable`. This makes the three-valued logic monotonic.

## 9. Interpretation & caveats

- **`Applicable` means "not provably unreachable"**, not "this patient definitely qualifies". Natural-language criteria in the verse text (diagnosis, prior treatments, etc.) aren't evaluable here.
- **Non-checkbox verses are treated as mandatory narrative.** If a narrative verse carries e.g. `SexRestricted = F` and the patient is male, the whole paragraph is reported `NotApplicable`. This matches the working assumption that narrative headers set the scope for their subtree. If you find real paragraphs where this is too strict, relax it.
- **Unknown inputs propagate to `Unknown`.** Missing patient sex + a sex-restricted verse ⇒ no committed verdict. Same for age, doctor qualifications, and request type.
- **`AndClauseNum` interpretation is conservative** (§6): worst-of within a group propagates to every member. This is a reasonable reading of "must be co-selectable" but is not exhaustively validated against the legislation, since only one group exists in the reference data.
- **`VerseType = E` exclusion verses** are flagged in the UI but treated identically by the predicates (§7). Logic only needs to change if a future export carries exclusion verses with inverted-restriction semantics.
- **Time-versioned constraints.** `VerseDataType` and `QualificationListDataType` both carry `From`/`To`; the reference date filters both.
- **Qualification list semantics.** OR, per §4. Switch to AND if you encounter a real list with `ExclusiveInd = "2"`.
- **Paragraph-level `Exclusion` (medicine-level) is out of scope.** The XSD allows paragraphs to carry exclusions pointing at ATC codes, VMP clusters, AMPP IDs, or prior paragraphs — this models whether a *medicine* falls under the paragraph, not whether a patient/doctor does. Zero entries populated in the reference export, and answering "does this medicine apply" would require an additional input (the medicine being prescribed).

## 10. Smoke test

Hand-checked against the April 2026 SAM export (`CHAPTERIV-1775613877335.xml`):

| Paragraph | Input | Result | Why |
|---|---|---|---|
| `12850000` (osteoporosis) | anything | `Applicable` | No mandatory restrictive choice group at the top level — patient-agnostic at the paragraph level |
| `13780000` (CLL) | female, age 10 | `NotApplicable` | Age < 18 fails a mandatory age check |
| `13780000` | female, age 70, CV `"001"` | `NotApplicable` | Doctor CV not in qualification list `962` |
| `13780000` | female, age 70, CV `"668"` | `Applicable` | CV `"668"` is in qualification list `962` |
| `13780000` | female, age 70, no doctor | `Unknown` | Qualification requirement exists, doctor info missing |
