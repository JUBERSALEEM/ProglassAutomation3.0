<#
.SYNOPSIS
  Safe, one-command verification that the ProGlass migration chain preserves
  existing Inquiry data. Built for the G1 Foundation Review.

.DESCRIPTION
  1. Finds the real database (%APPDATA%\ProGlassAutomation\inquiries.db).
  2. Refuses to continue if the application is running (a copy taken while it is
     writing could be inconsistent).
  3. Makes TWO copies in a folder you choose:
       frozen\inquiries.db        — the archive. Never opened again.
       working\inquiries.db       — the copy the migration is applied to.
  4. Verifies both copies exist and are byte-identical to the original.
  5. Runs the application's own migration runner against the WORKING copy only,
     via step5.f.2, which refuses the production path itself.
  6. Leaves everything in place and prints a report path.

  The production file is only ever READ by Copy-Item. It is never opened by the
  migration, never modified, and never deleted.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Verify-Migration1017.ps1 -Destination D:\G1

  # from an existing frozen copy instead of %APPDATA% (the frozen copy is only read)
  powershell -ExecutionPolicy Bypass -File tools\Verify-Migration1017.ps1 -Destination D:\G1 `
      -SourceFile D:\G1\G1-migration-20261029_172103\frozen\inquiries.db -Rewind

.NOTES
  Requires the .NET 8 SDK on the machine that runs it, and a checkout of the
  branch that contains it (arena/81baffde-proglassautomation3-0). A clone of
  this repository's default branch does NOT contain tools\ or step5.f — see
  docs/18, "Which checkout to run it from".
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Destination,

    # Use when the database is already at the chain tip, so the copy is rewound
    # one step (on the copy only) and the real migration 1017 runs for real.
    [switch] $Rewind,

    [string] $ApplicationProcess = 'ProGlassAutomation',

    # Optional: take the test copy from an existing database file instead of from
    # %APPDATA% — for example a frozen copy made by an earlier run, or one of the
    # runner's own MigrationBackups\inquiries_pre_v1017_*.db files (a genuine
    # pre-1017 database). The file you name is only ever READ; everything the test
    # touches is created under -Destination.
    [string] $SourceFile
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Text) Write-Host "`n== $Text" -ForegroundColor Cyan }
function Write-Ok   { param([string] $Text) Write-Host "   OK   $Text" -ForegroundColor Green }
function Write-Warn { param([string] $Text) Write-Host "   WARN $Text" -ForegroundColor Yellow }
function Fail       { param([string] $Text) Write-Host "`nSTOPPED: $Text" -ForegroundColor Red; exit 2 }

Write-Step '0. Which checkout is this?'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Write-Ok  "repository folder: $repoRoot"
try {
    $gitBranch = & git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null
    $gitCommit = & git -C $repoRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $gitBranch) { Write-Ok "branch $gitBranch at commit $gitCommit" }
} catch { Write-Warn 'git is not available here; the branch name is not recorded in this log' }

Write-Step '1. Locate the database to copy (read only)'
$productionFolder = Join-Path $env:APPDATA 'ProGlassAutomation'
$productionFile = Join-Path $productionFolder 'inquiries.db'
$usingProduction = [string]::IsNullOrWhiteSpace($SourceFile)
if ($usingProduction) {
    if (-not (Test-Path -LiteralPath $productionFile)) {
        Fail "no database at $productionFile — start the application once, or check the account you are signed in to."
    }
    $sourcePath = $productionFile
} else {
    $sourcePath = [System.IO.Path]::GetFullPath($SourceFile)
    if (-not (Test-Path -LiteralPath $sourcePath)) { Fail "no database at $sourcePath" }
}
$source = Get-Item -LiteralPath $sourcePath
if ($usingProduction) {
    Write-Ok "source: the production file $($source.FullName)"
} else {
    Write-Ok "source: $($source.FullName)  (supplied with -SourceFile)"
}
Write-Ok  "size $($source.Length) bytes, last written $($source.LastWriteTime)"
Write-Host '   (this file will only be read; the migration never sees this path)'

Write-Step '2. Make sure nothing is writing to it'
if ($usingProduction) {
    $running = Get-Process -Name $ApplicationProcess -ErrorAction SilentlyContinue
    if ($running) {
        Fail "the application is running (PID $($running.Id -join ', ')). Close it and run this again — a copy taken mid-write could be inconsistent."
    }
    Write-Ok 'the application is not running'
} else {
    Write-Ok 'the source is a file supplied with -SourceFile, so a running application cannot be writing to it'
}
foreach ($sidecar in @("$($source.FullName)-wal", "$($source.FullName)-shm")) {
    if (Test-Path -LiteralPath $sidecar) {
        Write-Warn "found $sidecar. A -wal file means the database was open recently; committed data may still be in it. If the integrity result is not clean, close the application cleanly, wait ten seconds and run again."
    }
}

Write-Step '3. Create the archive copy and the working copy'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$runFolder = Join-Path $Destination "G1-migration-$stamp"
$frozenFolder = Join-Path $runFolder 'frozen'
$workingFolder = Join-Path $runFolder 'working'
$logFolder = Join-Path $runFolder 'log'
foreach ($folder in @($frozenFolder, $workingFolder, $logFolder)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

$frozenFile = Join-Path $frozenFolder 'inquiries.db'
$workingFile = Join-Path $workingFolder 'inquiries.db'
Copy-Item -LiteralPath $source.FullName -Destination $frozenFile
Copy-Item -LiteralPath $source.FullName -Destination $workingFile
try { Start-Transcript -Path (Join-Path $logFolder 'verify.log') | Out-Null } catch { Write-Warn 'could not start the transcript; the console output below is still the evidence' }

Write-Step '4. Prove the copies exist before anything touches them'
$sourceHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
foreach ($copy in @($frozenFile, $workingFile)) {
    if (-not (Test-Path -LiteralPath $copy)) { Fail "copy missing: $copy" }
    $hash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
    if ($hash -ne $sourceHash) { Fail "copy differs from the original: $copy" }
    Write-Ok  "$copy"
    Write-Host "        SHA256 $hash"
}
if (-not (Test-Path -LiteralPath $frozenFile) -or -not (Test-Path -LiteralPath $workingFile)) {
    Fail 'a copy was not created; nothing else was done.'
}
Write-Ok 'archive and working copy are byte-identical to the production file'

Write-Step '5. Run the application''s migration runner against the WORKING copy only'
$project = Join-Path $PSScriptRoot '..\step5.ProjectRecords\step5.f.tests\step5.f.ProjectTests.csproj'
$project = [System.IO.Path]::GetFullPath($project)
if (-not (Test-Path -LiteralPath $project)) {
    Fail ("the verification program is not in this checkout: $project`n" +
          "         It lives on branch arena/81baffde-proglassautomation3-0. A clone of the`n" +
          "         default branch does not contain it. docs/18 section 3 shows how to get a`n" +
          "         second checkout without touching the one you are working in.")
}

$arguments = @('run', '--project', $project, '--configuration', 'Release', '--', $workingFile)
if ($Rewind) { $arguments += '--rewind-1017' }
Write-Host "   dotnet $($arguments -join ' ')"
& dotnet @arguments
$code = $LASTEXITCODE

Write-Step '6. Result'
switch ($code) {
    0 { Write-Ok  'every check passed — see the report below' }
    1 { Write-Warn 'a check FAILED — see the report below; the frozen copy is your untouched original' }
    3 { Write-Warn 'NOT RUN — the copy was already at the chain tip. Run again with -Rewind to let migration 1017 execute against real data.' }
    default { Write-Warn "the check could not run (exit $code)" }
}

Write-Host "`nArtifacts kept for evidence:"
Write-Host "   original (untouched) : $($source.FullName)"
Write-Host "   original SHA256      : $sourceHash"
Write-Host "   archive copy         : $frozenFile"
Write-Host "   migrated copy        : $workingFile"
Write-Host "   runner's own backup  : $workingFolder\MigrationBackups\"
Write-Host "   report               : $workingFile.g1-migration-report.md"
Write-Host "   console log          : $logFolder\verify.log"

try { Stop-Transcript | Out-Null } catch { }
exit $code
