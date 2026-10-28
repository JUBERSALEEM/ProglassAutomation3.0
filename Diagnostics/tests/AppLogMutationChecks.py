"""Diagnostics mutation checks — does the source gate actually catch anything?

A check that cannot fail is decoration. This harness copies the repository into a
throw-away folder, breaks one rule at a time in the copy, runs
`AppLogSourceChecks.py` against that copy (through PROGLASS_ROOT) and requires it
to FAIL with the message that belongs to the broken rule.

Run from anywhere:  python3 Diagnostics/tests/AppLogMutationChecks.py
Exit code 0 = every mutation was caught (and the unmutated copy still passes).
"""

from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
CHECKER = ROOT / "Diagnostics" / "tests" / "AppLogSourceChecks.py"

# (name, relative file, text to replace, replacement, message the checker must print)
MUTATIONS = [
    ("the process traps are never attached", "App.xaml.cs",
     "        AppLog.AttachProcessHandlers();", "",
     "startup must attach the process traps"),

    ("a Debug.WriteLine comes back", "MainWindow.xaml.cs",
     'AppLog.Error("shell.sign-in-gate.prepare", error,',
     'System.Diagnostics.Debug.WriteLine("prepared"); AppLog.Error("shell.sign-in-gate.prepare", error,',
     "Debug.WriteLine is invisible in Release"),

    ("the file count is unbounded", "Diagnostics/AppLogFile.cs",
     "public const int MaxFiles = 5;", "public const int MaxFiles = 50;",
     "outside the bounded range"),

    ("the log moves to a temp folder", "Diagnostics/AppLogFile.cs",
     'public static string Folder => Path.Combine(AppDataRoot.CurrentFolder, "Logs");',
     'public static string Folder => Path.Combine(Path.GetTempPath(), "Logs");',
     "must never resolve a repo/working/executable/temp path"),

    ("the caller's context stops being scrubbed", "Diagnostics/AppLog.cs",
     "Line(context)", "context",
     "the caller's context must be scrubbed"),

    ("a dispatcher fault closes the shell again", "Diagnostics/AppLog.cs",
     "args.Handled = true;", "args.Handled = false;",
     "marked handled so one bad click"),

    ("e-mail addresses stop being redacted", "Diagnostics/AppLogRedaction.cs",
     "result = Email.Replace(result, Mask);", "",
     "e-mail addresses must be redacted"),

    ("a duplicate username becomes a logged failure", "step3.UserManagement/step3.b.10.UserRepositoryFailureText.cs",
     """            if (!SqliteErrorClassifier.IsUnique(exception))
                AppLog.Error("step3.users.write-failure", exception, action);""",
     """            AppLog.Error("step3.users.write-failure", exception, action);
            if (!SqliteErrorClassifier.IsUnique(exception)) { }""",
     "must sit after the unique-constraint escape"),

    ("the severity vocabulary grows a third word", "Diagnostics/AppLog.cs",
     'severity == AppLogSeverity.Fatal ? "FATAL" : "ERROR"',
     'severity == AppLogSeverity.Fatal ? "WARNING" : "ERROR"',
     "the log must not emit"),

    ("a write fault throws into the caller", "Diagnostics/AppLogFile.cs",
     """                LastFailure = error.GetType().Name + ": " + error.Message;
                return false;""",
     """                LastFailure = error.GetType().Name + ": " + error.Message;
                throw;""",
     "must not throw"),

    ("an operation id names a routine event", "MainWindow.xaml.cs",
     'AppLog.Error("shell.first-run-check", error,',
     'AppLog.Error("shell.first-run-navigated", error,',
     "names a routine event"),

    ("a failed startup is no longer recorded", "App.xaml.cs",
     """            AppLog.Fatal("app.startup", error,
                "the shell could not be created; the application is shutting down");""",
     "",
     "a failed startup must be recorded as FATAL"),
]


def copy_repository(target):
    def ignore(directory, names):
        return [name for name in names if name in (".git", "bin", "obj", "node_modules")]

    shutil.copytree(ROOT, target, ignore=ignore, dirs_exist_ok=True)


def run_checker(root):
    environment = dict(os.environ)
    environment["PROGLASS_ROOT"] = str(root)
    return subprocess.run([sys.executable, str(CHECKER)], capture_output=True,
                          text=True, env=environment)


def main():
    failures = []
    with tempfile.TemporaryDirectory(prefix="proglass-mut-") as workspace:
        baseline = Path(workspace) / "baseline"
        copy_repository(baseline)

        result = run_checker(baseline)
        if result.returncode != 0:
            print("The unmutated copy does not pass — fix that first.")
            print(result.stdout)
            return 1
        print("baseline: PASS")

        for index, (name, relative, old, new, expected) in enumerate(MUTATIONS, start=1):
            case = Path(workspace) / f"case{index}"
            copy_repository(case)

            target = case / relative
            if not target.exists():
                failures.append(f"{name}: {relative} is missing")
                continue

            text = target.read_text(encoding="utf-8")
            if text.count(old) != 1:
                failures.append(f"{name}: anchor found {text.count(old)} times in {relative}")
                continue
            target.write_text(text.replace(old, new, 1), encoding="utf-8")

            result = run_checker(case)
            caught = result.returncode != 0 and expected in result.stdout
            print(f"{index:2d}. {'CAUGHT ' if caught else 'SURVIVED'}  {name}")
            if not caught:
                detail = " ".join((result.stdout + " " + result.stderr).split())[:400]
                failures.append(f"{name}: expected '{expected}' — got: "
                                + (detail if detail else "no output"))

            shutil.rmtree(case, ignore_errors=True)

    print()
    if failures:
        print(f"{len(failures)} mutation(s) not caught:")
        for failure in failures:
            print("  FAIL:", failure)
        return 1

    print(f"all {len(MUTATIONS)} mutations caught.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
