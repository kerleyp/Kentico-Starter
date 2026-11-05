# Pilot: atomic content model (Content Hub-backed pages)

Tests whether Clinical Guidance content can live as **reusable** Content
Hub items instead of directly on the page, which is the prerequisite for
reusing the same content in a future headless channel (per Kentico's own
guidance -- see the chat response this accompanies for the doc citations).

**Fully non-destructive.** This does not touch any of the 45 existing live
Clinical Guidance pages or their data. It creates one new reusable content
item, one new test page, and one new (separate) page template -- all
additive. The existing pages keep using the existing
`AUA.ClinicalGuidanceDocument.Default` template, untouched.

## 1. New reusable content type: AUA.ClinicalGuidanceContent

| Field | Data type | Form component |
|---|---|---|
| Title | Text | Text input |
| Headline | Text | Text input |
| MetaDescription | Long text | Text area |
| MetaKeywords | Text | Text input |
| Body | Rich text (HTML) | Rich text editor |
| SiteSection | Taxonomy | Tag selector |
| NeedsManualContentReview | Boolean | Checkbox |

Same fields as `AUA.ClinicalGuidanceDocument` today -- this is the "real"
content, moved off the page and into Content Hub.

## 2. New field on the existing AUA.ClinicalGuidanceDocument (additive)

Add one field, don't remove anything existing:

| Field | Data type | Form component |
|---|---|---|
| GuidanceContent | Pages and reusable content (→ **AUA.ClinicalGuidanceContent**) | Combined content selector, single item |

The 44 other existing pages will just have this field blank -- harmless.

## 3. Codegen both

```powershell
dotnet run --no-build -- --kxp-codegen --type "ReusableContentTypes" --include "AUA.ClinicalGuidanceContent"
dotnet run --no-build -- --kxp-codegen --type "PageContentTypes" --include "AUA.ClinicalGuidanceDocument"
```

(Re-run the page type's codegen since its field list changed.)

## 4. New page template, registered separately from the existing one

`ClinicalGuidanceDocumentAtomicPilot.cshtml` (new file) resolves
Title/Headline/Body from the **linked reusable item**, not from the page's
own fields -- this is the actual thing being tested. Registered under a
different identifier (`AUA.ClinicalGuidanceDocument.AtomicPilot`) so it's
selectable per-page without affecting the 44 existing pages, which keep
using `.Default`.

## 5. Run the pilot trigger

```powershell
$env:PILOT_ATOMIC_FILE="C:\Users\pkerley\Documents\Kentico\pilot_atomic_content.json"
dotnet run
```
```powershell
Remove-Item Env:\PILOT_ATOMIC_FILE
```

Creates ONE new `AUA.ClinicalGuidanceContent` reusable item (real content,
copied from an existing guideline), ONE new page at
`/clinical-guidance-atomic-pilot` referencing it, and sets that one page's
template config to the new pilot template -- via a targeted SQL `UPDATE`
scoped to that single `WebPageItemID`, same technique as
`fix_template_config.sql` but for one row instead of all `NULL` rows.

## 6. Verify

- **Content Hub** → the new `AUA.ClinicalGuidanceContent` item should now
  show up there -- this is the actual thing you asked about.
- `/clinical-guidance-atomic-pilot` should render identically to a normal
  Clinical Guidance page, but with its content resolved through the linked
  item instead of its own fields.

## If it works: what a real migration would involve

Only decide this after the pilot proves out. For the other 44 (or all
704+90, if going further): create a matching reusable item for each
existing page, point each page's new reference field at it, then decide
whether to also strip the now-redundant direct fields off the page type
(optional -- Kentico's docs explicitly allow a hybrid where pages store
some data directly AND reference reusable content, so this doesn't have
to be all-or-nothing).

## Unproven code, same caveat as the Menu/MenuItem work

This reuses the same `IContentItemManager`-based reusable-content creation
approach from `MENU_SEED_FILE` (still unverified at time of writing), plus
a new untested piece: resolving a page's own linked-content reference
field at render time. If either breaks, paste the exact error.
