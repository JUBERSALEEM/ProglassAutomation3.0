# step4 — Company Profile

The installation's **own** identity: the company name, TRN, address, contact and
bank details that every future document prints, plus the logo. One page, one
record, one logo.

This is not client data and not user data. `step2` owns the companies we work
for; `step3` owns the people who use the application; `step4` owns the business
that runs it.

---

## Where it lives in the shell

| | |
| --- | --- |
| Workspace index | `10` (appended, so every earlier index keeps its meaning) |
| Rail entry | **Settings → Company** (gear mark) |
| Banner subtitle | `Settings · Company profile, contact details and logo` |
| View | `step4.d.1.CompanyProfileView.xaml` |

---

## Files

| File | Responsibility |
| --- | --- |
| `step4.a.1.CompanyProfile.cs` | `CompanyLogoLayout`, the `CompanyProfile` entity, `CompanyIdentitySnapshot`, `CompanyProfileDraft`, `CompanyProfileSaveResult` |
| `step4.a.2.CompanyProfileRules.cs` | Normalisation and validation — the one place the rules exist |
| `step4.a.3.CompanyLogoStore.cs` | The logo asset store in the app data folder: import, resolve, delete |
| `step4.b.1.CompanyProfileSchema.cs` | Migration **1016**: the table, its two CHECK constraints, and the post-migration assertions |
| `step4.b.2.ICompanyProfileRepository.cs` | Read and write contracts, including `GetIdentityAsync` for future documents |
| `step4.b.3.CompanyProfileDbContext.cs` | EF map on the existing `inquiries.db`; no `EnsureCreated` |
| `step4.b.4.CompanyProfileRepository.cs` | Authorised write path, tracked row, transaction, delete-after-commit |
| `step4.c.1.CompanyProfileViewModel.cs` | The editor: fields, per-field messages, dirty state, honest save reporting |
| `step4.c.2.CompanyLogoPreview.cs` | Bounded, non-throwing image decode for the preview |
| `step4.d.1.CompanyProfileView.xaml` | The page |
| `step4.d.2.CompanyProfileView.xaml.cs` | Composition, the file picker, contained failures |
| `step4.f.tests/` | `step4.f.1` behaviour checks (Windows) and `step4.f.2` portable source/contract gate |

---

## Rules the module keeps

**One profile, enforced by the database.** `Id` is fixed at `1` with a table
`CHECK ("Id" = 1)`; the layout value has its own `CHECK`; EF maps the key as
`ValueGeneratedNever`. A second profile cannot be inserted by this code, by a
future module, or by hand in SQLite. There is no `IsDefault`, no `UsageCount` and
no company directory.

**One migration chain.** 1016 is appended to `AppSchemaMigrations.Inquiries` after
1015 and runs through the existing initializer, so the backup-before-migrate,
single-transaction and verify safeguards apply. It creates one table and seeds no
row: "never saved" is a real state, not an empty record.

**The logo is a name, not a path.** A picked image is copied into
`%APPDATA%\ProGlassAutomation\CompanyProfile\logo-<guid>.<ext>` and the row stores
that generated name. A backup or a moved data folder carries the artwork with it.
Only PNG/JPG/JPEG under 12 MB; only a bare file name is ever resolved, so a
hand-edited row cannot reach an arbitrary file.

**The aspect ratio is never stretched.** One stored dimension (the long edge,
8–80 mm); the other follows the image. Every `<Image>` is `Stretch="Uniform"`.

**A failed save changes nothing.** The replacement asset is deleted only after the
transaction commits. A refused save leaves the stored profile and its logo
exactly as they were, and the page says so in words instead of showing a
half-truth.

**Only an administrator writes.** The repository reads
`AuthenticationContext.Current` — set only by a verified password — and requires
an active Administrator. The button is not the gate. Everyone else can read the
profile, and the page prints why it is read-only.

**One validation implementation.** The editor runs `step4.a.2` before the write
and the repository runs it again before persisting, so a caller cannot skip it.

---

## Fields

| Section | Fields | Required |
| --- | --- | --- |
| Business identity | company name, TRN, address | name only |
| Contact | telephone, website, company e-mail | none |
| Representative | representative name, representative mobile | none |
| Banking | bank name, account number / IBAN | none |
| Logo | image, placement (`None` / `LeftPortrait` / `TopPanel`), printed long edge | none |

TRN is optional but must be exactly 15 digits when given (separators tolerated).
A placement without a logo is refused; a stored logo with the `None` placement is
allowed — "keep the artwork on file, print text only" is a letterhead choice.

---

## Verification state

- **Portable checks:** `step4.f.2` runs anywhere with Python 3 and needs no .NET.
  The nine pre-existing checkers still pass.
- **Not run, not claimed:** no .NET SDK and no WPF runtime existed in the
  authoring environment, so the module was **not compiled**, `step4.f.1` was
  **not executed**, and the page was **not opened** in any of the nine themes.
  Those checks are the owner's to run on Windows:

  ```
  dotnet build ProGlassAutomation.csproj
  dotnet run --project step4.CompanyProfile/step4.f.tests/step4.f.CompanyTests.csproj
  python3 step4.CompanyProfile/step4.f.tests/step4.f.2.CompanyProfileSourceChecks.py
  ```

---

## Out of scope (deliberately)

Multi-company directories, tenants, a second database or backend, client/user
redesign, new authentication, new themes or private theme dictionaries,
PI/JO/DN/TI or any document rendering, document numbering, licensing, and
unapproved VAT/currency defaults. The bank details are stored; nothing prints
them yet.
