param(
    [string]$CscPath = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

function Find-Csc {
    if ($CscPath -and (Test-Path $CscPath)) { return (Resolve-Path $CscPath).Path }
    foreach ($candidate in @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "csc.exe was not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

function Invoke-StandaloneSuite([string]$Name, [string[]]$Sources, [string]$Csc) {
    foreach ($source in $Sources) { if (-not (Test-Path $source)) { throw "$Name source missing: $source" } }
    $outputDir = Join-Path $env:TEMP ("ErenshorFollow-" + $Name + "-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $outputDir | Out-Null
    try {
        $output = Join-Path $outputDir ($Name + ".exe")
        $arguments = @("/nologo", "/target:exe", "/optimize+", ('/out:"{0}"' -f $output)) + $Sources
        & $Csc $arguments
        if ($LASTEXITCODE -ne 0) { throw "$Name compilation failed." }
        & $output
        if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
    }
    finally {
        if (Test-Path -LiteralPath $outputDir) { Remove-Item -LiteralPath $outputDir -Recurse -Force }
    }
}

$csc = Find-Csc

Invoke-StandaloneSuite "TravelCommandGrammarTests" @(
    (Join-Path $repoRoot "src\TravelCommandGrammar.cs"),
    (Join-Path $scriptRoot "TravelCommandGrammarTests.cs")
) $csc

Invoke-StandaloneSuite "FollowRebindPolicyTests" @(
    (Join-Path $repoRoot "src\FollowRebindPolicy.cs"),
    (Join-Path $scriptRoot "FollowRebindPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "RouteCandidatePolicyTests" @(
    (Join-Path $repoRoot "src\RouteCandidatePolicy.cs"),
    (Join-Path $scriptRoot "RouteCandidatePolicyTests.cs")
) $csc

Write-Host "Erenshor Follow deterministic suites: ALL PASS" -ForegroundColor Green
