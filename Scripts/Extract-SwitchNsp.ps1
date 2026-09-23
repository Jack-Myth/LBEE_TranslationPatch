param(
    [string]$NspPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = Join-Path $root 'Files'
$hactool = Join-Path $files 'hactool.exe'
$prodKeys = Join-Path $files 'prod.keys'
if (-not $NspPath) {
    $packages = @(Get-ChildItem $files -Filter '*.nsp' -File)
    if ($packages.Count -ne 1) { throw 'Specify -NspPath when Files contains zero or multiple NSP files.' }
    $NspPath = $packages[0].FullName
}
if (-not $OutputPath) { $OutputPath = Join-Path $files 'SwitchExtract' }
$NspPath = (Resolve-Path $NspPath).Path
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path $hactool) -or -not (Test-Path $prodKeys)) { throw 'Files/hactool.exe and Files/prod.keys are required.' }

$ncaDir = Join-Path $OutputPath 'nsp'
$contentDir = Join-Path $OutputPath 'contents'
New-Item -ItemType Directory -Force $ncaDir, $contentDir | Out-Null

# This older hactool rejects newer or unrelated entries in current prod.keys.
# Keep only the NCA keys it needs. This derived file stays in the ignored output directory.
$keyPath = Join-Path $OutputPath 'compat.keys'
$allowed = '^(header_key|master_key_[0-9a-f]{2}|key_area_key_(application|ocean|system)_[0-9a-f]{2}|titlekek_[0-9a-f]{2})$'
$keys = @(Get-Content $prodKeys | Where-Object {
    $parts = $_ -split '=', 2
    $parts.Count -eq 2 -and $parts[0].Trim() -match $allowed -and $parts[1].Trim() -match '^[0-9a-fA-F]{32,64}$'
})
if (-not ($keys | Where-Object { $_ -match '^\s*header_key\s*=' })) { throw 'prod.keys has no valid header_key.' }
[IO.File]::WriteAllLines($keyPath, [string[]]$keys)

function Invoke-Hactool([string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    $result = & $hactool @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        # hactool can include key material in errors, so never print its raw output.
        throw "hactool failed with exit code $LASTEXITCODE. Arguments and diagnostic output withheld because they may contain key material."
    }
    return $result
}

$ncas = @(Get-ChildItem $ncaDir -Filter '*.nca' -File)
if ($ncas.Count -eq 0) {
    Write-Host 'Extracting NSP container...'
    Invoke-Hactool -Arguments @('-t', 'pfs0', "--outdir=$ncaDir", $NspPath) | Out-Null
    $ncas = @(Get-ChildItem $ncaDir -Filter '*.nca' -File)
}
if ($ncas.Count -eq 0) { throw 'No NCA files were extracted.' }

foreach ($nca in $ncas) {
    $info = Invoke-Hactool -Arguments @('-k', $keyPath, '--disablekeywarns', '--suppresskeys', '-t', 'nca', '-i', $nca.FullName)
    $match = [regex]::Match(($info -join "`n"), '(?m)^Content Type:\s*(\w+)')
    if (-not $match.Success) { throw "Could not identify NCA type: $($nca.Name)" }
    $type = $match.Groups[1].Value.ToLowerInvariant()
    $target = Join-Path $contentDir ($type + '-' + $nca.BaseName)
    New-Item -ItemType Directory -Force $target | Out-Null
    Write-Host "Extracting $type : $($nca.Name)"
    $args = @('-k', $keyPath, '--disablekeywarns', '--suppresskeys', '-t', 'nca', '-x')
    if ($type -eq 'program') {
        $args += "--exefsdir=$(Join-Path $target 'exefs')"
        $args += "--romfsdir=$(Join-Path $target 'romfs')"
    } else {
        $args += "--section0dir=$target"
    }
    $args += $nca.FullName
    Invoke-Hactool -Arguments $args | Out-Null
}
Write-Host "Done: $OutputPath"
