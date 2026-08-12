param(
    [string]$CscPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Csc {
    if ($CscPath -and (Test-Path $CscPath)) { return (Resolve-Path $CscPath).Path }
    $command = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($candidate in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Could not find csc.exe. Install/enable the .NET Framework compiler or run from a Developer PowerShell."
}

$csc = Find-Csc
$tempDir = Join-Path ([IO.Path]::GetTempPath()) ("ErenshorFollow-route-tests-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir | Out-Null
try {
    $exe = Join-Path $tempDir "RouteCandidatePolicyTests.exe"
    & $csc /nologo /target:exe /out:$exe `
        (Join-Path $root "src\RouteCandidatePolicy.cs") `
        (Join-Path $root "tests\RouteCandidatePolicyTests.cs")
    if ($LASTEXITCODE -ne 0) { throw "Route policy test compilation failed with exit code $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Route policy tests failed with exit code $LASTEXITCODE" }
}
finally {
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}

& (Join-Path $root "tests\RUN_DETERMINISTIC_TESTS.ps1") -CscPath $csc
if ($LASTEXITCODE -ne 0) { throw "Follow command/rebind deterministic tests failed." }

& (Join-Path $root "tests\RUN_UI_CAMP_HANDOFF_TESTS.ps1") -CscPath $csc
if ($LASTEXITCODE -ne 0) { throw "Follow UI/Camp handoff deterministic tests failed." }

Write-Host "Erenshor Follow consolidated deterministic tests: ALL PASS" -ForegroundColor Green
