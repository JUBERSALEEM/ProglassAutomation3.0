# step6.e.2 — Owner verification: Proforma Invoice

This is the list a person runs **on Windows**, with the real application and a
**copy** of the real database. Nothing below has been executed by the author:
the authoring machine has no .NET SDK and no WPF runtime, so the module is
verified to the level of source rules only.

Record the date, the machine and the outcome beside each check. A check that was
not run is not a pass.

**Database location.** `%APPDATA%\ProGlassAutomation\inquiries.db`
(the schema version is `PRAGMA user_version`; it must be **1018** after the first
launch of this build).

---

## Before anything else

| # | Step | Expected | If not |
| --- | --- | --- | --- |
| 0.1 | Copy `inquiries.db` somewhere safe; work on a copy | a copy exists, production untouched | stop; do not run the new build against production first |
| 0.2 | `cd step5.ProjectRecords\step5.f.tests`, `dotnet run -- "<copy path>" --rewind-1017` | the harness rewinds only 1017's objects, then applies 1017 **and 1018**; every inquiry value and id unchanged; `quick_check` clean | stop and report the first failing line: the migration chain is not treating existing data safely |
| 0.3 | `cd step6.ProformaInvoice\step6.f.tests`, `dotnet run` | every check prints PASS; exit code 0 | report the first FAIL verbatim |
| 0.4 | Build the solution | 0 errors, 0 warnings added by step6 | report the file and line |

0.2 exists because 1018 is registered on the same chain: the step5 harness now
asserts that **1017 comes first after a rewind and later migrations follow it**,
so this is also the test that 1018 did not disturb 1017's preservation proof.

## The seven stages

| # | Step | Expected |
| --- | --- | --- |
| 1.1 | Start a new proforma from a quotation whose inquiry carries a client and a project | stage 1 is filled: company name, TRN, address; client name and TRN; project name; prepared-by; the inquiry number; nothing typed by hand |
| 1.2 | Start a new proforma with no inquiry | stage 1 is empty except the signed-in user as prepared-by, and the stage marker reads incomplete — not an error |
| 1.3 | Change the client's name in Client Management, reopen the saved proforma | the proforma still prints the name it stored; only "refresh header" changes it |
| 1.4 | Set a rate for a DGU group leaving one pane's rate empty | the group is marked incomplete; the total does not silently count that pane as zero |
| 1.5 | Enter yield `0.5` on a DGU group | the rate is costed at the stated yield `/ 0.5`; **if a yield outside 0.10–1.00 is entered, a warning appears** and the calculation uses 1.00 |
| 1.6 | Enter a DGU+LAM group | the printed specification names three panes and an air spacer plus an interlayer; the rate includes both |
| 1.7 | Change the grid's glass colours | the wastage rows follow the colours: one per colour in use, and a colour used on two panes shows twice the area |
| 1.8 | Add a manual wastage row, then edit the grid | the manual row survives the rebuild |
| 1.9 | Switch off a charge that automation switched on, then edit the grid | it stays off (a manual switch is never overridden) |
| 1.10 | Enter a discount larger than the net | the discount is capped, the grand total is not negative, and the document says so |
| 1.11 | Set a target grand total | the discount is reverse-solved so the grand total matches; VAT is 5% of the discounted subtotal, printed after the discount |
| 1.12 | Open stage 7 | cost rows are whole sheets; the sheet quantity entered on a wastage row wins; the margin is measured against the grand total less VAT; a margin below 5% warns and still saves |

## Lifecycle and numbering

| # | Step | Expected |
| --- | --- | --- |
| 2.1 | Save a new proforma | the number is `PI-<year>-001`; saving again does not change it |
| 2.2 | Cancel it, start another | the new number is the next sequence, not the cancelled one |
| 2.3 | Send a draft, then edit and save | refused with a message naming the state; the stored document is unchanged |
| 2.4 | Duplicate the sent document | a new draft with a new number, carrying the same lines |
| 2.5 | Try to reopen a cancelled document for editing | refused; the document is still readable and still in the register when "show cancelled" is on |
| 2.6 | Two operators save the same draft | the second save is refused as out of date, not silently merged |
| 2.7 | Sign out and try to save | refused: an unidentified session is not an author |

## Attachments

| # | Step | Expected |
| --- | --- | --- |
| 3.1 | Attach a PDF | the file appears in `%APPDATA%\ProGlassAutomation\ProformaInvoice\<id>\<number>\` with a generated name; the original name is shown in the list |
| 3.2 | Attach a file over 20 MB | refused with the limit stated |
| 3.3 | Remove an attachment | the row and the file both disappear; no orphan left behind |
| 3.4 | Delete a file from disk by hand, then open it | an error message, not a crash |

## Data safety (the checks that matter most)

| # | Step | Expected |
| --- | --- | --- |
| 4.1 | Compare the inquiry row and its BOQ before and after converting | the inquiry's own columns are unchanged; only `ProformaInvoiceId` is set, and it is set by the Inquiry module's own method |
| 4.2 | Look for a delete anywhere in the register | there is none: a document is cancelled |
| 4.3 | Open a proforma saved by an older build after a few edits elsewhere | it opens with the totals it stored; an unreadable body reports an error and is **not** replaced by an empty draft |
| 4.4 | Check the audit columns of a saved document | created/updated by the signed-in user's id and name; never the Windows login |

## What to report back

For each failure: the check number, the first message shown, and the file/line if
the message names one. Do not fix numbering or totals by editing stored data —
the body and the row would then disagree; report it instead.


## PDF export — added after the 2026-11-14 owner approval

| # | Step | Expected |
| --- | --- | --- |
| 5.1 | Save a complete invoice and export Classic Navy | a valid PDF opens; company logo/identity, permanent PI number, dates, parties and project are correct |
| 5.2 | Export the same invoice in Modern Teal, Editorial Ink, Industrial Steel and Minimal Ivory | all five styles contain the same invoice sections and totals; only print treatment changes |
| 5.3 | Use an invoice with several specification groups, long descriptions and enough rows for multiple pages | rows stay aligned, group subtotals and the grand total remain correct, footer page numbers are present, no clipped columns |
| 5.4 | Remove the stored company logo or use a company profile without one | the PDF still exports with readable company identity; no broken image or crash |
| 5.5 | Export an unsaved draft | the UI asks the operator to save first; no unnumbered customer PDF is produced |
| 5.6 | Inspect the customer PDF for internal P&L values | no internal profit, cost or margin worksheet is disclosed |
| 5.9 | Company Profile logo layout is LeftPortrait | the logo appears at the left using the saved size and the rest of the letterhead remains readable |
| 5.10 | Company Profile logo layout is TopPanel, then None | TopPanel centres the logo above company details; None omits the logo; missing files do not break export |

5.7 | Export the full dossier without enabling P&L | no cost, profit or margin figures appear |
| 5.8 | Enable internal P&L and export the dossier | the red internal-only warning appears on every page; cost rows, profit and target analysis are present |

**Not yet verified:** QuestPDF package restore, Windows compilation, actual PDF
byte generation, page breaks, logo rendering, dossier P&L opt-in behaviour, and
visual comparison of all five styles. The new automated render checks are in `step6.f.4`; the Windows owner
checks above remain mandatory before calling PDF export complete.


## Cut-list import and workbook/CSV export — owner verification

| # | Step | Expected |
| --- | --- | --- |
| 6.1 | Import an XLSX file with REF/W1/H1/QTY headers | rows append to the selected group with the expected dimensions and quantities |
| 6.2 | Import comma CSV, tab TXT, semicolon CSV with decimal commas, and dimension-pair shorthand | all valid rows parse; malformed/zero-quantity rows are skipped with a visible count |
| 6.3 | Import scanner JSON | aliases for ref/spec/type/dimensions/qty are accepted; invalid JSON is reported |
| 6.4 | Import a file with multiple specification names or glass types that match existing groups | rows are mapped to the matching groups; unknown or ambiguous groups are rejected without partial row mutation |
| 6.5 | Export invoice workbook without internal P&L | Summary, Specifications, Other Charges and Terms exist; no internal cost sheet exists |
| 6.6 | Enable internal P&L and export workbook | red-marked Internal P&L and P&L Targets sheets are included |
| 6.7 | Export cut-list CSV and register workbook | 13 columns, three-decimal areas, totals, and the current register filter are preserved |
| 6.8 | Import an XLSX file while it is open in Excel | the last-saved bytes are read using shared file access |

**Known V3 boundary:** PDF/photo OCR is not ported because this repository has no
Gemini file-AI service/settings integration. Excel, CSV, text and scanner JSON
are supported without inventing a new external AI dependency.


## Other-charge automation and wastage guidance

| # | Step | Expected |
| --- | --- | --- |
| 7.1 | Use a DGU spec naming U-Insert, overlap, shutter bonding, fanhole, cutouts/holes, beveling and frosting | each named charge is suggested; DGU argon and silicon-bed suggestions remain available |
| 7.2 | Use a DGU+LAM spec with three pane colours | the build is recognised and all three pane colours contribute to the V3 per-colour wastage aggregation |
| 7.3 | Inspect a Clear wastage row | tooltip explains that clear-glass wastage is optional because offcuts may be reusable |
| 7.4 | Change the wastage SHEETS FOR COST quantity, then turn the wastage charge off | P&L still uses the sheet quantity as production consumption; the billing switch does not erase it |
| 7.5 | Manually switch a suggested charge off, then recalculate | the manual active override remains off |
| 7.6 | Compare matched DGU polish with W2/H2 present and absent | both measured panes are counted; when W2/H2 is absent, W1/H1 is counted twice |
| 7.7 | Select each legacy polish edge type on a specification group | the selected formula is used for W1/H1 and W2/H2, matched panes are not double charged, and Custom uses the per-group manual LM input |
| 7.8 | Set a Step 4 U-Insert rate on a spec naming U-Insert, then clear it | the per-spec rate overrides the invoice-level fallback; PDF/XLSX show the blended effective rate |


| 7.7 | Enable U-Insert on two DGU pricing sections with different Step 4 rates; leave the third disabled | only enabled specs are charged, first-pane/cavity area is used, each spec rate wins, and the Step 6 rate is the fallback only when the Step 4 rate is blank |
