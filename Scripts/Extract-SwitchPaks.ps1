param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = Join-Path $root 'Files'
$luckSystem = Join-Path $files 'lucksystem.exe'
$contents = Join-Path $files 'SwitchExtract\contents'
if (-not $OutputPath) { $OutputPath = Join-Path $files 'SwitchExtract\pak-unpacked' }
if (-not (Test-Path $luckSystem)) { throw 'Files/lucksystem.exe is missing.' }
$programs = @(Get-ChildItem $contents -Directory -Filter 'program-*')
if ($programs.Count -ne 1) { throw 'Expected exactly one extracted Program NCA in Files/SwitchExtract/contents.' }
$romfs = Join-Path $programs[0].FullName 'romfs'
$names = @('FONT', 'SCRIPT', 'OTHCG', 'PARTS', 'SYSCG', 'SYSCG2', 'GM')

foreach ($name in $names) {
    $source = Join-Path $romfs "$name.PAK"
    if (-not (Test-Path $source)) { throw "Missing $name.PAK in Program RomFS." }
    $target = Join-Path $OutputPath $name
    $unpacked = Join-Path $target 'files'
    $list = Join-Path $target 'file-list.txt'
    New-Item -ItemType Directory -Force $target, $unpacked | Out-Null
    Write-Host "Extracting $name.PAK..."
    & $luckSystem pak extract -i $source -o $list --all $unpacked
    if ($LASTEXITCODE -ne 0) { throw "LuckSystem failed to extract $name.PAK (exit $LASTEXITCODE)." }
    $count = @(Get-ChildItem $unpacked -File -Recurse).Count
    if ($count -eq 0) { throw "No files extracted from $name.PAK." }
    Write-Host "  $count files -> $target"
}
Write-Host "Done: $OutputPath"
