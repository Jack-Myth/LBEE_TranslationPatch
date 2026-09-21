param(
    [string]$GoExe = "go"
)

$ErrorActionPreference = "Stop"

$GoVersion = & $GoExe version
if ($LASTEXITCODE -ne 0) {
    throw "Unable to run the Go compiler: $GoExe"
}
if ($GoVersion -notmatch '^go version go1\.20\.14 ') {
    throw "Windows 7 builds require Go 1.20.14. Found: $GoVersion"
}

$PreviousGoOs = $env:GOOS
$PreviousGoArch = $env:GOARCH
$PreviousCgoEnabled = $env:CGO_ENABLED

try {
    $env:GOOS = "windows"
    $env:GOARCH = "386"
    $env:CGO_ENABLED = "0"

    Push-Location (Join-Path $PSScriptRoot "LuckSystem")
    try {
        & $GoExe build -trimpath -ldflags='-s -w' `
            -o (Join-Path $PSScriptRoot "Files\lucksystem.exe") .
        if ($LASTEXITCODE -ne 0) {
            throw "LuckSystem build failed."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:GOOS = $PreviousGoOs
    $env:GOARCH = $PreviousGoArch
    $env:CGO_ENABLED = $PreviousCgoEnabled
}

Write-Host "Built Files\lucksystem.exe with Go 1.20.14 for Windows 7 x86."
