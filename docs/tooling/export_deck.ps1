# Export a .pptx to PDF (next to it) and to per-slide PNGs (docs/tooling/_render/<name>/) with PowerPoint.
# usage: powershell -ExecutionPolicy Bypass -File docs/tooling/export_deck.ps1 docs/decks/<slug>/<slug>.pptx [-NoPng]
param([Parameter(Mandatory = $true)][string]$Deck, [switch]$NoPng)

$deckPath = (Resolve-Path $Deck).Path
$name = [System.IO.Path]::GetFileNameWithoutExtension($deckPath)
$pdf = [System.IO.Path]::ChangeExtension($deckPath, ".pdf")
$renderDir = Join-Path (Split-Path $PSScriptRoot -Parent) "tooling\_render\$name"

$app = New-Object -ComObject PowerPoint.Application
# PowerPoint is single-instance: if the user has presentations open, this is THEIR PowerPoint -- never quit it.
$userOpen = $app.Presentations.Count
try {
    # ReadOnly, Untitled, WithWindow = false
    $p = $app.Presentations.Open($deckPath, -1, 0, 0)
    $p.SaveAs($pdf, 32)   # ppSaveAsPDF
    if (-not $NoPng) {
        if (Test-Path $renderDir) { Remove-Item -Recurse -Force $renderDir }
        New-Item -ItemType Directory -Force $renderDir | Out-Null
        $i = 1
        foreach ($s in $p.Slides) {
            $s.Export((Join-Path $renderDir ("slide-{0:D2}.png" -f $i)), "PNG", 1600, 900)
            $i++
        }
    }
    $p.Close()
}
finally {
    if ($userOpen -eq 0) { $app.Quit() }
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($app) | Out-Null
}
Write-Output "pdf: $pdf"
if (-not $NoPng) { Write-Output "png: $renderDir" }
