# Farm Basics - rebuild the design-system HTML pages from their Markdown sources.
# Uses Plan\tools\build_page.py (pandoc + shared CSS at Plan\assets\farm-basics.css).

$ErrorActionPreference = "Stop"

# Resolve Python at run time. Anaconda was removed from this machine on 2026-10-01,
# so the previously hardcoded anaconda3 path no longer exists. build_page.py needs
# only the standard library, so any CPython 3.8+ works.
$py = $null
$pyCandidates = @(
  (Join-Path $env:LOCALAPPDATA "Python\pythoncore-3.14-64\python.exe"),
  (Join-Path $env:LOCALAPPDATA "Programs\Python\Python313\python.exe")
)
$onPath = Get-Command python.exe -ErrorAction SilentlyContinue
if ($onPath) { $pyCandidates += $onPath.Source }
foreach ($c in $pyCandidates) {
  if ($c -and (Test-Path -LiteralPath $c)) { $py = $c; break }
}
if (-not $py) {
  throw "python.exe not found. Install CPython 3.8+ or set the PANDOC/python path in Plan\regenerate.ps1."
}

# build_page.py resolves pandoc itself ($PANDOC, then PATH, then the winget install
# root); pandoc is not on PATH until a new shell is opened after installation.
if (-not $env:PANDOC) {
  $pandocRoot = Join-Path $env:LOCALAPPDATA "Pandoc\pandoc.exe"
  if (Test-Path -LiteralPath $pandocRoot) { $env:PANDOC = $pandocRoot }
}

$builder = Join-Path $PSScriptRoot "tools\build_page.py"
$here = $PSScriptRoot

& $py $builder `
  --md (Join-Path $here "Farm-Basics-Implementation-Sprints.md") `
  --out (Join-Path $here "Farm-Basics-Implementation-Sprints.html") `
  --title "Progressive Implementation Sprints" `
  --subtitle "Farm Basics full project - tasks & sub-tasks into <=10-minute micro-tasks" `
  --meta "24 sprints|forest,283 micro-tasks|gold,<=10 min each|sage,interactive checkboxes|ink" `
  --phases "now,pending,pending,pending,HL" `
  --note "Phase 0 in progress - S1-S4; then Phase 1 (S5-S13), Phase 2 (S14-S19), Phase 3 (S20-S22), Hardening (S23-S24)." `
  --progress

& $py $builder `
  --md (Join-Path $here "Farm-Basics-Sprint-Plan.md") `
  --out (Join-Path $here "Farm-Basics-Sprint-Plan.html") `
  --title "Implementation Sprint Plan" `
  --subtitle "Farm Basics 20-section plan - sprints S1-S24 across BRD phases 0-3 + hardening" `
  --meta "Phases 0-3|forest,24 sprints|gold,S1-S24|sage,effort TBC|ink" `
  --phases "pending,pending,pending,pending,HL" `
  --note "Indicative durations & effort per sprint - Requires Clarification (capacity not yet confirmed)."