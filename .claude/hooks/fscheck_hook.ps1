# PostToolUse hook (Edit|Write): run fscheck.py on the edited .fs file's folder (a document's tabs
# must be checked together, or cross-tab names show as UNDEFINED) and report only findings in the
# edited file. Exit 2 + stderr = feedback to Claude; exit 0 = silent. Never blocks the edit.
$ErrorActionPreference = 'SilentlyContinue'
$j = [Console]::In.ReadToEnd() | ConvertFrom-Json
$f = $j.tool_response.filePath
if (-not $f) { $f = $j.tool_input.file_path }
if (-not $f -or $f -notlike '*.fs') { exit 0 }
$f = [IO.Path]::GetFullPath($f)
if ($f -match '\\(std|archive|example_1)\\' -or $f -match '_(tests|spike|probe)\.fs$') { exit 0 }
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dir = Split-Path -Parent $f
$files = @(Get-ChildItem -Path $dir -Filter *.fs -File | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) { exit 0 }
Push-Location $root
$out = & python fscheck.py @files 2>&1 | Out-String
Pop-Location
$name = [IO.Path]::GetFileName($f)
$mine = ($out -split "`r?`n") | Where-Object { $_ -match [regex]::Escape($name) + ':\d+' -or $_ -match [regex]::Escape($name) + '\s' }
if ($mine) {
    [Console]::Error.WriteLine("fscheck findings in $name (checked with the other .fs files in its folder; UNDEFINED may be false if the name lives in an imported tab outside this folder):")
    [Console]::Error.WriteLine(($mine -join "`n"))
    exit 2
}
exit 0
