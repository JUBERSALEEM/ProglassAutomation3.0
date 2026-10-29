#!/usr/bin/env python3
"""Gate for the permanent rules around module boundaries, theme registration and
mode parity.

Three invariants, each of which a G1 finding depended on being *true* rather than
being written down somewhere:

1. **Cross-module storage access is a closed set.** G1-03, G1-04 and G1-05 found
   four places where one module reaches into another module's tables — three in
   `step2` reading `Users`, one in `step4` reading `Users` with raw SQL, and the
   `step5` link write into `Inquiries`. Those four are the register. Anything else
   fails here, so the boundary cannot widen between reviews without a deliberate
   edit to this file.

2. **`Inquiries` is written only by the Inquiry module and by the one approved
   link file.** That was the narrowest part of the review and it is worth a rule of
   its own: a second writer would silently undo the guarantee that linking a
   project touches exactly one column.

3. **Nine themes, nine token sets, both modes.** G1-09 was raised because a
   *document* said four themes were unregistered while the catalog registered all
   nine. The lesson is that a status table is not evidence. This rule measures the
   packages: every theme appears in `ThemeCatalog.Installed`, every package ships a
   Light and a Dark dictionary, and every one of the 18 dictionaries defines the
   same set of keys.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROBLEMS = []


def check(condition, message):
    if not condition:
        PROBLEMS.append(message)


def read(path):
    return Path(path).read_text(encoding="utf-8")


def sources(*folders):
    for folder in folders:
        for path in sorted((ROOT / folder).rglob("*.cs")):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            yield path


# ── 1. The cross-module storage register ──────────────────────────────────
# Each entry names a file and what it is allowed to touch. A change to any of the
# three numbers below is an architecture decision, not a refactor.
PERMITTED_USER_ACCESS = {
    "step2.ClientManagement/step2.b.4.ClientRepository.cs": 3,
    "step4.CompanyProfile/step4.b.4.CompanyProfileRepository.cs": 1,
}

user_access = {}
for path in sources("step2.ClientManagement", "step3.UserManagement",
                    "step4.CompanyProfile", "step5.ProjectRecords"):
    relative = path.relative_to(ROOT).as_posix()
    text = read(path)
    # A DbContext of another module, or raw SQL into its table.
    # Namespace-qualified or not, it is the same breach.
    hits = len(re.findall(r"new\s+(?:[\w]+\.)*UserDbContext\s*\(", text))
    # C# verbatim SQL doubles its quotes: FROM ""Users"" WHERE ""Id"" = @id
    hits += len(re.findall(r'FROM\s+("{2}|")Users(?:\1)', text))
    hits += len(re.findall(r'(?:UPDATE|DELETE\s+FROM|INSERT\s+INTO)\s+("{2}|")Users(?:\1)', text))
    if hits:
        user_access[relative] = hits

for relative, count in sorted(user_access.items()):
    if relative.startswith("step3.UserManagement/"):
        continue                      # the owner of Users may use its own store
    expected = PERMITTED_USER_ACCESS.get(relative)
    check(expected is not None,
          f"{relative} reaches into the Users table ({count} site(s)) and is not in the "
          "G1 register. A module does not read another module's storage: either use an "
          "approved contract, or add the exception here with a reason and an owner decision")
    if expected is not None:
        check(count == expected,
              f"{relative} now touches the Users table {count} time(s); the G1 review "
              f"recorded {expected}. docs/17 §3 enumerates these — update both together")

for relative, expected in PERMITTED_USER_ACCESS.items():
    check(user_access.get(relative) == expected,
          f"{relative} no longer shows the {expected} Users access(es) the G1 review "
          "recorded. If the cross-module read was removed, delete its entry here and "
          "update docs/17 §3 — the review must describe the code that exists")

# ── 2. Who may write Inquiries ────────────────────────────────────────────
LINK_WRITER = "step5.ProjectRecords/step5.b.6.ProjectRepository.Link.cs"

INQUIRY_WRITE = re.compile(
    r'(?:UPDATE|DELETE\s+FROM|INSERT\s+INTO)'
    r'(?:[\\"\s]{1,4})'
    r'(?:Inquiries|\{ProjectSchema\.InquiriesTable\})', re.I)


def inquiry_writes(text):
    """Every statement in the file that writes the inquiry table."""
    return INQUIRY_WRITE.findall(text)
for path in sources("step2.ClientManagement", "step3.UserManagement",
                    "step4.CompanyProfile", "step5.ProjectRecords", "step1.ThemeStudio",
                    "Inquiry"):
    relative = path.relative_to(ROOT).as_posix()
    if relative.startswith("Inquiry/"):
        continue                      # the owner of the inquiry table
    if relative == LINK_WRITER:
        continue                      # the one approved integration point
    found = inquiry_writes(read(path))
    check(not found,
          f"{relative} writes the inquiry table ({len(found)} statement(s)). Linking a project "
          "is one UPDATE of ProjectRecordId in step5.b.6 and nothing else writes an inquiry "
          "from outside the Inquiry module — G1-05 counts on that")

link = ROOT / LINK_WRITER
check(link.is_file(), f"the approved inquiry-link writer is missing: {LINK_WRITER}")
if link.is_file():
    text = read(link)
    writes = inquiry_writes(text)
    check(len(writes) == 2,
          f"the link writer contains {len(writes)} statement(s) that write the inquiry table; the "
          "G1 review recorded exactly two (attach and detach, both single-column guarded UPDATEs). "
          "Re-read docs/17 §3.3 before changing this")
    for column in ("CustomerId", "ProjectId", "SalesmanId", "PreparedByUserId"):
        check(f'"{column}"' not in text.split('UPDATE ""{ProjectSchema.InquiriesTable}""')[1][:600]
              if 'UPDATE ""{ProjectSchema.InquiriesTable}""' in text else True,
              f"the link writer now touches the legacy placeholder {column}. The G1 review "
              "records that it reads and writes none of them")

# ── 3. Nine registered themes, one token set, both modes ──────────────────
catalog = ROOT / "Theme/step1.a.ThemeEngine/step1.a.6.ThemeCatalog.cs"
check(catalog.is_file(), "the theme catalog is missing")
if catalog.is_file():
    text = read(catalog)
    installed = re.search(r"Installed\s*\{[^=]*=\s*Array\.AsReadOnly\(new\[\]\s*\{(.*?)\}\)",
                          text, re.S)
    check(installed is not None, "the theme catalog no longer declares an Installed list")
    if installed is not None:
        entries = re.findall(r"new ThemeDefinition\(", installed.group(1))
        check(len(entries) == 9,
              f"the catalog registers {len(entries)} theme(s); the product ships nine and G1's "
              "theme finding is about exactly this number")

packages = sorted(p for p in (ROOT / "Theme").glob("Theme*") if p.is_dir())
check(len(packages) == 9, f"expected nine theme packages, found {len(packages)}")

TOKENS = None
for package in packages:
    name = package.name
    for mode in ("Light", "Dark"):
        dictionary = package / f"{name}.{mode}.xaml"
        check(dictionary.is_file(), f"{name}: the {mode} dictionary is missing")
        if not dictionary.is_file():
            continue
        keys = set(re.findall(r'x:Key="([^":]+)"', read(dictionary)))
        if TOKENS is None:
            TOKENS = keys          # the first dictionary read is the baseline
            continue
        missing = sorted(TOKENS - keys)
        extra = sorted(keys - TOKENS)
        check(not missing and not extra,
              f"{name}.{mode}.xaml does not define the same tokens as every other dictionary: "
              f"missing {missing[:6]}{'…' if len(missing) > 6 else ''}, "
              f"unexpected {extra[:6]}{'…' if len(extra) > 6 else ''}. A theme that is "
              "registered but incomplete renders defaults, and no status table proves otherwise")

check(TOKENS is not None and len(TOKENS) >= 100,
      f"the token baseline looks wrong ({0 if TOKENS is None else len(TOKENS)} keys): the "
      "contract defines more than a hundred, so this rule is not measuring anything")


if PROBLEMS:
    print(f"module boundary checks: {len(PROBLEMS)} failure(s).")
    for problem in PROBLEMS:
        print("  FAIL: " + problem)
    sys.exit(1)

print(f"PASS: the cross-module register is exactly the {sum(PERMITTED_USER_ACCESS.values())} "
      f"accesses the G1 review recorded, Inquiries is written only by its own module and the "
      f"one approved link file, and all {len(packages)} themes are registered with identical "
      f"token sets in Light and Dark ({len(TOKENS)} keys each).")
