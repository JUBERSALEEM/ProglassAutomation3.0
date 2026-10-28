"""Diagnostics source checks — the failure-only diagnostic log (round 33).

Portable checks that need no .NET SDK, no Windows and no running application.
They verify the properties of the log that are cheap to get wrong and expensive
to miss, because every one of them fails silently:

   1. ONE logger. No second log file, no second sink, no `Debug.WriteLine`
      left behind anywhere in the application (Release throws those away).
   2. FAILURE ONLY. Every call site is inside a `catch` block (or inside the one
      helper that turns a caught exception into a message), and no operation id
      names a routine success. The count is capped, so this cannot quietly grow
      into an activity recorder.
   3. WHERE. The file lives under the product's app-data root, never the repo,
      the working directory, the executable folder or a temp folder.
   4. CREATE ON DEMAND. The directory is made when the first failure is written,
      not at startup — a clean run leaves nothing on disk.
   5. BOUNDED. One file size, one file count, oldest deleted first.
   6. DEFENSIVE. No `throw` in the logger, every write returns a bool, a
      recursion guard, and the failure is kept in LastFailure instead of thrown.
   7. REDACTED. Passwords, hashes, salts, tokens, long account numbers and
      e-mail addresses never reach the file, and every emitted value goes
      through the one scrubbing line.
   8. ALL THREE PROCESS TRAPS, with the dispatcher's behaviour deliberate and
      documented (the app keeps running; a background-thread fault does not).
   9. SEVERITY VOCABULARY. ERROR and FATAL only.
  10. WIRED. Startup logs before it shows the message box, and the paths that
      already turned a failure into a sentence (client/user/company writes) now
      also record it.
  11. THE BENIGN CASE IS NOT LOGGED. A duplicate-constraint refusal is feedback,
      not a fault.
  12. The project includes the folder and excludes its tests, and the audit
      document exists and says what was and was not verified.

Exit code 0 = pass. Run from anywhere:
    python3 Diagnostics/tests/AppLogSourceChecks.py
Set PROGLASS_ROOT to check a copy (used by the mutation harness).
"""

from pathlib import Path
import os
import re
import sys

ROOT = Path(os.environ.get("PROGLASS_ROOT") or Path(__file__).resolve().parents[2])
DIAG = ROOT / "Diagnostics"
APP = DIAG / "AppLog.cs"
TRAPS = DIAG / "AppLogProcessTraps.cs"
STORE = DIAG / "AppLogFile.cs"
REDACT = DIAG / "AppLogRedaction.cs"
APP_XAML_CS = ROOT / "App.xaml.cs"
PROJECT = ROOT / "ProGlassAutomation.csproj"

FAILURES = []

# Every operation id the application is allowed to write, and the file it is
# written from. A new id must be added here, which is the point: an id is a
# decision that this is a failure worth keeping.
REQUIRED_SITES = {
    "app.startup": "App.xaml.cs",
    "app.startup.customization-restore": "App.xaml.cs",
    "app.dispatcher.unhandled": "Diagnostics/AppLogProcessTraps.cs",
    "app.domain.unhandled": "Diagnostics/AppLogProcessTraps.cs",
    "app.task.unobserved": "Diagnostics/AppLogProcessTraps.cs",
    "shell.first-run-check": "MainWindow.xaml.cs",
    "shell.remembered-signin": "MainWindow.xaml.cs",
    "shell.sign-in-gate.create": "MainWindow.xaml.cs",
    "shell.sign-in-gate.prepare": "MainWindow.xaml.cs",
    "step2.clients.write-failure": "step2.ClientManagement/step2.b.5.ClientFailureText.cs",
    "step3.users.write-failure": "step3.UserManagement/step3.b.10.UserRepositoryFailureText.cs",
    "step3.users.refresh": "step3.UserManagement/step3.d.1.UserWorkspaceView.xaml.cs",
    "step4.company.save": "step4.CompanyProfile/step4.b.4.CompanyProfileRepository.cs",
    "step4.company.load": "step4.CompanyProfile/step4.d.2.CompanyProfileView.xaml.cs",
    "step4.company.refresh": "step4.CompanyProfile/step4.d.2.CompanyProfileView.xaml.cs",
    "step4.company.logo-delete": "step4.CompanyProfile/step4.a.3.CompanyLogoStore.cs",
    "theme.customization.clear-corrupt": "Theme/step1.g.Customization/step1.g.2.CustomizationStore.cs",
    "theme.customization.restore": "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs",
}

# A call site may sit outside a catch block only in this method: it is the one
# place that receives a caught exception and turns it into a sentence.
CATCH_EXEMPT_METHODS = ("DescribeWriteFailure",)

# Words that would mean a routine event is being written.
SUCCESS_WORDS = (
    "success", "succeeded", "started-up", "opening", "opened", "navigat",
    "click", "heartbeat", "ping", "hello", "ready", "saved-ok", "info",
    "debug", "trace", "progress", "activity", "audit",
)

MAX_CALL_SITES = 25


def check(condition, message):
    """Record a failure and return the condition, so a caller can stop early."""
    if not condition:
        FAILURES.append(message)
    return bool(condition)


def read(path):
    return path.read_text(encoding="utf-8")


def strip_comments(source):
    """Remove // and /// comments, keeping line structure."""
    return "\n".join(line.split("//")[0] for line in source.splitlines())


def strip_strings(source):
    """Blank out string literals so a word inside a message is not read as code."""
    out = []
    inside = False
    escape = False
    for char in source:
        if inside:
            if escape:
                escape = False
            elif char == "\\":
                escape = True
            elif char == '"':
                inside = False
                out.append('"')
            continue
        if char == '"':
            inside = True
            out.append('"')
        else:
            out.append(char)
    return "".join(out)


def application_sources():
    """Every application .cs file, excluding the log's own tests."""
    paths = sorted(ROOT.rglob("*.cs"))
    keep = []
    for path in paths:
        parts = path.parts
        if any(part in ("bin", "obj", ".git") for part in parts):
            continue
        if any("tests" in part.lower() for part in parts):
            continue
        keep.append(path)
    return keep


def catch_spans(source):
    """(start, end) of the body of every catch block, by brace matching."""
    spans = []
    for match in re.finditer(r"\bcatch\b", source):
        brace = source.find("{", match.end())
        if brace < 0:
            continue
        depth = 0
        for index in range(brace, len(source)):
            char = source[index]
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    spans.append((brace, index))
                    break
    return spans


def method_spans(source):
    """(name, start, end) for simple `private/public static ... Name(...) { }` methods."""
    found = []
    for match in re.finditer(r"\b(?:private|public|internal|protected)\b[^\n;{]*?\b(\w+)\s*\([^)]*\)\s*\n?\s*\{", source):
        name = match.group(1)
        start = source.index("{", match.end() - 1)
        depth = 0
        for index in range(start, len(source)):
            if source[index] == "{":
                depth += 1
            elif source[index] == "}":
                depth -= 1
                if depth == 0:
                    found.append((name, start, index))
                    break
    return found


def call_sites():
    """(path, operation id, offset, source) for every AppLog.Error/Fatal call."""
    sites = []
    for path in application_sources():
        source = read(path)
        # Inside the logger the methods are called unqualified; the definition
        # itself has no string after the parenthesis, so it cannot match.
        for match in re.finditer(r"(?:AppLog\.)?(?:Error|Fatal)\(\s*\"([^\"]*)\"", source):
            sites.append((path, match.group(1), match.start(), source))
    return sites


def require_one_logger():
    for path in application_sources():
        source = read(path)
        name = path.relative_to(ROOT).as_posix()
        if not name.startswith("Diagnostics/"):
            check("System.Diagnostics.Debug.WriteLine" not in source,
                  f"{name}: Debug.WriteLine is invisible in Release — use AppLog")
            check("Console.WriteLine" not in source,
                  f"{name}: Console.WriteLine is not a log the owner can open")
            check(".log\"" not in source and ".log'" not in source,
                  f"{name}: names a log file; only Diagnostics/AppLogFile may")
            check("Serilog" not in source and "NLog" not in source and "log4net" not in source,
                  f"{name}: introduces a third-party logger without evidence it is needed")

    # The only writer of the log file is AppLogFile.
    for path in application_sources():
        source = read(path)
        if path.name == "AppLogFile.cs":
            continue
        check("AppendAllText" not in source and "StreamWriter" not in source,
              f"{path.name}: writes a file of its own instead of using AppLog")

    for required in (APP, TRAPS, STORE, REDACT):
        check(required.exists(), f"missing {required.name}")


def require_failure_only():
    sites = call_sites()
    check(len(sites) <= MAX_CALL_SITES,
          f"{len(sites)} log call sites exceeds the cap of {MAX_CALL_SITES}; "
          "this must not become an activity recorder")

    for path, operation, offset, source in sites:
        name = path.relative_to(ROOT).as_posix()
        check(re.match(r"^[a-z0-9]+(\.[a-z0-9-]+)+$", operation) is not None,
              f"{name}: operation id '{operation}' must look like module.thing.what-failed")
        lowered = operation.lower()
        for word in SUCCESS_WORDS:
            check(word not in lowered,
                  f"{name}: operation id '{operation}' names a routine event, not a failure")

        if name.startswith("Diagnostics/"):
            continue

        spans = catch_spans(source)
        inside = any(start <= offset <= end for start, end in spans)
        if not inside:
            methods = method_spans(source)
            inside = any(
                method in CATCH_EXEMPT_METHODS and start <= offset <= end
                for method, start, end in methods
            )
            check(inside,
                  f"{name}: '{operation}' is logged outside a catch block "
                  "(failure-only means a caught exception, not a progress message)")


def require_required_sites():
    found = {(path.relative_to(ROOT).as_posix(), operation) for path, operation, _, _ in call_sites()}
    for operation, expected_file in REQUIRED_SITES.items():
        check(any(op == operation for _, op in found),
              f"the log call '{operation}' is missing: that failure is no longer recorded")
        check(any(op == operation and path == expected_file for path, op in found),
              f"'{operation}' must be written from {expected_file}")


def require_location():
    source = read(STORE)
    check("AppDataRoot.CurrentFolder" in source,
          "the log folder must derive from AppDataRoot, the product's one data root")
    check('"Logs"' in source, "the log folder must be the Logs folder under the app-data root")

    body = read(STORE) + read(APP)
    for forbidden in ("GetTempPath", "GetCurrentDirectory", "Environment.CurrentDirectory",
                      "AppContext.BaseDirectory", "Directory.GetCurrentDirectory"):
        check(forbidden not in body,
              f"the log must never resolve a repo/working/executable/temp path ({forbidden})")
    check("LocalApplicationData" not in body,
          "the log uses the app-data root, not a second folder choice")


def require_created_on_demand():
    store = read(STORE)
    creates = [m.start() for m in re.finditer(r"Directory\.CreateDirectory", store)]
    check(len(creates) == 1,
          "the Logs directory must be created in exactly one place (on the first failure)")
    if creates:
        append_at = store.find("public static bool Append(")
        check(append_at >= 0 and creates[0] > append_at,
              "the Logs directory must be created inside Append, never at startup")
    check("Directory.CreateDirectory" not in read(APP),
          "AppLog must not create the log folder itself")
    check("CreateDirectory" not in read(APP_XAML_CS),
          "startup must not create the log folder")


def require_bounded():
    store = read(STORE)
    size = re.search(r"MaxFileBytes\s*=\s*([0-9*\s]+);", store)
    files = re.search(r"MaxFiles\s*=\s*(\d+);", store)
    check(size is not None, "MaxFileBytes must be a stated constant")
    check(files is not None, "MaxFiles must be a stated constant")
    if size:
        value = eval(size.group(1).strip())  # constant arithmetic, e.g. 1024 * 1024
        check(1024 * 1024 <= value <= 8 * 1024 * 1024,
              f"MaxFileBytes={value} is outside the bounded range (1–8 MB)")
    if files:
        value = int(files.group(1))
        check(2 <= value <= 10, f"MaxFiles={value} is outside the bounded range (2–10)")
    check("RotateIfFull" in store, "there must be a rotation step")
    check("MaxEntryBytes" in store,
          "a single record must be clamped, or one huge stack trace breaks 'bounded'")
    check("record truncated" in store,
          "a clamped record must say so instead of looking complete")
    check("MaxEntryBytes / 4" in store,
          "the character budget must be a quarter of the byte budget, so 4-byte characters cannot overflow")
    check("File.Move" in store, "rotation must move the full file aside")
    check("File.Delete(oldest)" in store, "rotation must delete the oldest file, not grow forever")


def require_defensive():
    store = read(STORE)
    app = read(APP)
    for name, source in (("AppLogFile.cs", store), ("AppLog.cs", app)):
        check(re.search(r"\bthrow\b", strip_strings(strip_comments(source))) is None,
              f"{name}: must not throw — a logging fault may not become the incident")
    check("return false;" in store, "Append must report failure by returning false")
    check("LastFailure" in store and "LastFailure" in app,
          "a write fault must be observable instead of thrown")
    check("_writing" in store and "ThreadStatic" in store,
          "Append needs a recursion guard so a fault cannot re-enter itself")
    check("try" in app and "catch (Exception)" in app,
          "AppLog.Write must guard itself")
    check("catch (Exception error)" in store, "Append must catch its own faults")


def require_redaction():
    redact = read(REDACT)
    app = read(APP)
    lower = redact.lower()
    for secret in ("password", "hash", "salt", "token", "secret"):
        check(secret in lower, f"redaction must name '{secret}'")
    check("Email.Replace(result, Mask)" in redact, "e-mail addresses must be redacted")
    check("LongNumber.Replace(result, Mask)" in redact,
          "long account/bank numbers must be redacted")
    check("GroupedNumber.Replace(result, Mask)" in redact,
          "spaced IBAN-shaped numbers must be redacted")
    check('SecretAssignment.Replace(result, "${name}" + Mask)' in redact,
          "a secret field's value must be replaced by name")
    check('QuotedSecret.Replace(text, "${name}" + Mask)' in redact,
          "a quoted secret must be replaced too")
    check("public static string Scrub" in redact, "redaction needs one entry point")
    check("Scrub(" in app, "AppLog must scrub what it writes")
    check(app.count("Line(") >= 6,
          "every emitted value must go through the one scrubbing Line() helper")

    # The message and the stack are the two places a password could arrive by
    # accident, so both must be scrubbed at the point they are emitted.
    check("Line(error.Message)" in app, "the exception message must be scrubbed")
    check("Line(frame.Trim())" in app, "the stack trace must be scrubbed")
    check("Line(context)" in app, "the caller's context must be scrubbed")


def require_process_traps():
    app = read(APP) + read(TRAPS)
    for handler in ("DispatcherUnhandledException", "AppDomain.CurrentDomain.UnhandledException",
                    "TaskScheduler.UnobservedTaskException"):
        check(handler in app, f"missing process trap: {handler}")

    dispatcher = app[app.find("void OnDispatcherUnhandled"):app.find("void OnAppDomainUnhandled")]
    check("Fatal(" in dispatcher, "a dispatcher fault must be recorded as FATAL")
    check("System.Windows.MessageBox.Show" in dispatcher,
          "the operator must be told what happened, not left on a half-updated screen")
    check("Shutdown(" in dispatcher,
          "the application must close through its own shutdown path after a dispatcher fault")
    check("args.Handled = true;" in dispatcher,
          "Handled must stop the runtime aborting mid-unwind; the shutdown follows it")
    if "Fatal(" in dispatcher and "Shutdown(" in dispatcher:
        check(dispatcher.index("Fatal(") < dispatcher.index("Shutdown("),
              "the failure must be recorded before the application closes")

    domain = app[app.find("void OnAppDomainUnhandled"):app.find("void OnUnobservedTask")]
    check("Fatal(" in domain, "an AppDomain fault must be recorded as FATAL")
    check("Handled" not in domain.replace("UnhandledExceptionEventArgs", ""),
          "the AppDomain trap must not pretend the process survives")

    task = app[app.find("void OnUnobservedTask"):]
    check("Error(" in task, "an unobserved task fault must be recorded")

    # Late attachment: the App type's static constructor runs before there is an
    # Application object, so the dispatcher flag must be separate or the trap is
    # lost for the life of the process.
    check("DispatcherHandlerAttached" in app,
          "the dispatcher trap needs its own attached flag")
    check("if (ProcessHandlersAttached) return;" not in app,
          "an early 'already attached' return would silently skip the dispatcher trap")
    check("ProcessHandlersAttached = _domainAttached && _taskAttached && DispatcherHandlerAttached;" in app,
          "the 'all attached' flag must be computed, not assumed")


def require_severity_vocabulary():
    app = read(APP)
    for word in ('"INFO"', '"DEBUG"', '"TRACE"', '"WARNING"', '"VERBOSE"'):
        check(word not in app, f"the log must not emit {word}; ERROR and FATAL only")
    check('"FATAL"' in app and '"ERROR"' in app, "ERROR and FATAL are the two severities")
    check("timezone" in app.lower() or "UTC" in app,
          "the timestamp must state its zone")
    check("yyyy-MM-dd'T'HH:mm:ss.fffzzz" in app,
          "the timestamp needs a round-trip format with an explicit offset")
    check("AppVersion()" in app, "every record must name the application version")
    check("operation :" in app and "module    :" in app,
          "every record must name the operation and the module")


def require_wired():
    """Startup must attach the traps, and record before it shows anything."""
    app_cs = read(APP_XAML_CS)

    # Two attachment points, both needed and both asserted by LOCATION: a string
    # that appears somewhere in the file proves nothing about where it runs.
    on_startup = app_cs[app_cs.find("protected override void OnStartup"):]
    if not check("AppLog.AttachProcessHandlers();" in on_startup,
                 "startup must attach the process traps"):
        return
    check(on_startup.index("AppLog.AttachProcessHandlers();")
          < on_startup.index("Themes.Initialize(Resources);"),
          "the traps must be attached before theme setup can fail")

    if not check("static App()" in app_cs,
                 "a trap must exist before the shell is constructed: the App field "
                 "initialisers run before OnStartup, and a fault there would leave no record"):
        return
    check("AppLog.AttachProcessHandlers()" in app_cs[app_cs.find("static App()"):].split("public ThemeService")[0],
          "the static constructor must attach the traps")
    check(app_cs.index("static App()") < app_cs.index("public ThemeService Themes"),
          "the static constructor must run before the fields initialise")

    start = app_cs.find("MainWindow = new MainWindow")
    check(start >= 0, "startup must create the shell")
    outer = app_cs[app_cs.find("catch (Exception error)", start):] if start >= 0 else ""
    if not check("AppLog.Fatal(" in outer, "a failed startup must be recorded as FATAL"):
        return
    check(outer.index("AppLog.Fatal(") < outer.index("MessageBox.Show"),
          "the failure must be recorded before the message box is shown")
    check("Shutdown(1)" in outer, "startup must still stop instead of continuing half-built")
    check('AppLog.Error("app.startup.customization-restore"' in app_cs,
          "the swallowed customisation-restore failure must be recorded")


def require_benign_case_not_logged():
    client = read(ROOT / "step2.ClientManagement/step2.b.5.ClientFailureText.cs")
    user = read(ROOT / "step3.UserManagement/step3.b.10.UserRepositoryFailureText.cs")
    for name, source in (("ClientRepository", client), ("UserRepository", user)):
        check("IsUnique(exception)" in source and "AppLog.Error" in source,
              f"{name}: the duplicate refusal must stay unlogged")
        guard = source.find("IsUnique(exception)")
        logged = source.find("AppLog.Error")
        check(guard >= 0 and logged > guard,
              f"{name}: the log call must sit after the unique-constraint escape")


def require_project_and_docs():
    project = read(PROJECT)
    check('Include="Diagnostics/**/*.cs"' in project,
          "the project must compile Diagnostics (EnableDefaultCompileItems=false)")
    check('Remove="Diagnostics/tests/**/*.cs"' in project,
          "the Diagnostics checks must stay out of the application build")

    audit = ROOT / "docs/14.Foundation-Audit-and-Diagnostic-Logging.md"
    check(audit.exists(), "the audit document docs/14 must exist")
    if audit.exists():
        text = read(audit)
        for phrase in ("failure-only", "not executed", "Error Report"):
            check(phrase.lower() in text.lower(),
                  f"docs/14 must state '{phrase}'")


def main():
    require_one_logger()
    require_failure_only()
    require_required_sites()
    require_location()
    require_created_on_demand()
    require_bounded()
    require_defensive()
    require_redaction()
    require_process_traps()
    require_severity_vocabulary()
    require_wired()
    require_benign_case_not_logged()
    require_project_and_docs()

    if FAILURES:
        print(f"diagnostics source checks: {len(FAILURES)} failure(s).")
        for failure in FAILURES:
            print("  FAIL:", failure)
        return 1
    print("diagnostics source checks: PASS.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
