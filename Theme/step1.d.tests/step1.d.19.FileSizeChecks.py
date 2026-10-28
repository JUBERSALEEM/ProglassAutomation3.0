"""step1.d.19 — no god files.

Owner instruction (round 30b): *"never keep any file long like god files, keep
them small separate parts for the same module so they are easy to maintain."*

The rule
--------
  * a C# file is at most 400 lines;
  * an XAML file is at most 750 lines — a page is one declarative unit and is
    sectioned by comments, but past that it stops being readable and has to be
    split (styles to a ResourceDictionary, bands to their own part);
  * a screen's styles live in their own dictionary and are merged by the view
    (that is what step4.d.3 is for: the Company page dropped from 946 to 724
    lines without moving a single element out of the page).

What is scanned
---------------
Application source only: `.cs` and `.xaml` under the repository, skipping
`bin/`, `obj/` and every `*.tests` folder. A verification executable or a
portable checker is deliberately one self-contained file — it has to be
runnable on its own, from anywhere — so the caps do not apply there.

The baseline
------------
Seventeen classes and two shell views were already over the cap when the rule
was adopted. They are listed with their size on 2026-10-28 and **may not grow**:
any new line in one of them has to go into a new part instead. When one of them
is split below the cap, its entry must be deleted — a stale exemption is a lie,
so the checker fails on it.

Exit code 0 = pass. Portable: python3, no .NET SDK, no Windows.
"""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]

CAP = {".cs": 400, ".xaml": 750}

# Frozen 2026-10-28, round 30b. Delete the line when the file is split.
BASELINE = {
    "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs": 1337,
    "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs": 1091,
    "step3.UserManagement/step3.b.4.UserRepository.cs": 1088,
    "step3.UserManagement/step3.c.2.UserWorkspaceViewModel.cs": 1037,
    "step4.CompanyProfile/step4.c.1.CompanyProfileViewModel.cs": 999,
    "Inquiry/ViewModels/InquiryEditorViewModel.cs": 922,
    "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs": 913,
    "step3.UserManagement/step3.d.1.UserWorkspaceView.xaml": 900,
    "Inquiry/ViewModels/InquiryListViewModel.cs": 899,
    "MainWindow.xaml": 895,
    "Inquiry/ViewModels/InquiryInboxViewModel.cs": 832,
    "Inquiry/Services/Inquiry/InquiryRepository.cs": 784,
    "step3.UserManagement/step3.c.1.UserEditorViewModel.cs": 633,
    "Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxClient.cs": 561,
    "step2.ClientManagement/step2.c.2.ClientWorkspaceViewModel.cs": 540,
    "step2.ClientManagement/step2.c.1.ClientEditorViewModel.cs": 513,
    "step2.ClientManagement/step2.b.4.ClientRepository.cs": 482,
    "step3.UserManagement/step3.c.3.FirstRunViewModel.cs": 473,
    "Theme/ThemeClaymorphism/step1.e.8.ClayCustomization.cs": 431,
}

FAILURES = []


def check(condition, message):
    if not condition:
        FAILURES.append(message)


def scan_paths():
    for path in sorted(ROOT.rglob("*")):
        if not path.is_file() or path.suffix not in CAP:
            continue
        parts = path.relative_to(ROOT).parts
        if any(part in ("bin", "obj", ".git") for part in parts):
            continue
        if any(part.endswith(".tests") for part in parts):
            continue
        yield path


def line_count(path):
    return len(path.read_text(encoding="utf-8", errors="ignore").splitlines())


def main():
    sizes = {}
    for path in scan_paths():
        name = str(path.relative_to(ROOT)).replace("\\", "/")
        sizes[name] = (line_count(path), CAP[path.suffix])

    check(len(sizes) > 200, f"the scan found only {len(sizes)} source files — paths are wrong")

    for name, (count, cap) in sorted(sizes.items(), key=lambda item: -item[1][0]):
        allowed = BASELINE.get(name)
        if allowed is None:
            check(count <= cap,
                  f"{name}: {count} lines, over the {cap}-line cap — split it into parts "
                  "(styles to a dictionary, one class or band per file)")
        else:
            check(count <= allowed,
                  f"{name}: {count} lines, up from the frozen {allowed} — a grandfathered "
                  "file may be split, never grown")
            check(count > cap,
                  f"{name}: now {count} lines, inside the {cap}-line cap — delete its "
                  "exemption from step1.d.19; a stale baseline entry hides the truth")

    for name in BASELINE:
        check(name in sizes, f"baseline entry {name} no longer exists — delete it")

    if FAILURES:
        print(f"file size checks: {len(FAILURES)} failure(s).")
        for failure in FAILURES:
            print("  FAIL: " + failure)
        return 1

    print(f"file size checks: PASS ({len(sizes)} source files; caps {CAP['.cs']} C# / "
          f"{CAP['.xaml']} XAML; {len(BASELINE)} grandfathered files still to split).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
