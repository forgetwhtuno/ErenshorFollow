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

Invoke-StandaloneSuite "FollowNameMatchPolicyTests" @(
    (Join-Path $repoRoot "src\FollowNameMatchPolicy.cs"),
    (Join-Path $scriptRoot "FollowNameMatchPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "FollowZoneHandoffPolicyTests" @(
    (Join-Path $repoRoot "src\FollowZoneHandoffPolicy.cs"),
    (Join-Path $scriptRoot "FollowZoneHandoffPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "FollowStuckRecoveryPolicyTests" @(
    (Join-Path $repoRoot "src\FollowStuckRecoveryPolicy.cs"),
    (Join-Path $scriptRoot "FollowStuckRecoveryPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "FollowCombatPolicyTests" @(
    (Join-Path $repoRoot "src\FollowCombatPolicy.cs"),
    (Join-Path $scriptRoot "FollowCombatPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "RouteCandidatePolicyTests" @(
    (Join-Path $repoRoot "src\RouteCandidatePolicy.cs"),
    (Join-Path $scriptRoot "RouteCandidatePolicyTests.cs")
) $csc

Invoke-StandaloneSuite "FollowControlDescriptorTests" @(
    (Join-Path $repoRoot "src\FollowSuiteDescriptorPolicy.cs"),
    (Join-Path $repoRoot "src\SuiteUiPolicies.cs"),
    (Join-Path $scriptRoot "FollowControlDescriptorTests.cs")
) $csc


Invoke-StandaloneSuite "SimActionMenuLayoutPolicyTests" @(
    (Join-Path $repoRoot "src\SimActionMenuLayoutPolicy.cs"),
    (Join-Path $scriptRoot "SimActionMenuLayoutPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "ExpeditionMovementPolicyTests" @(
    (Join-Path $repoRoot "src\ExpeditionMovementPolicy.cs"),
    (Join-Path $scriptRoot "ExpeditionMovementPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "ExpeditionMovementOwnershipPolicyTests" @(
    (Join-Path $repoRoot "src\ExpeditionMovementOwnershipPolicy.cs"),
    (Join-Path $scriptRoot "ExpeditionMovementOwnershipPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "ExpeditionCrossingPolicyTests" @(
    (Join-Path $repoRoot "src\ExpeditionCrossingPolicy.cs"),
    (Join-Path $scriptRoot "ExpeditionCrossingPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "ExpeditionRouteGraphTests" @(
    (Join-Path $repoRoot "src\ZoneRouteGraphPolicy.cs"),
    (Join-Path $scriptRoot "ExpeditionRouteGraphTests.cs")
) $csc

Invoke-StandaloneSuite "ExpeditionWorkflowTests" @(
    (Join-Path $repoRoot "src\ExpeditionModels.cs"),
    (Join-Path $repoRoot "src\ExpeditionWorkflowPolicy.cs"),
    (Join-Path $scriptRoot "ExpeditionWorkflowTests.cs")
) $csc

Invoke-StandaloneSuite "ExpeditionTelemetryPolicyTests" @(
    (Join-Path $repoRoot "src\ExpeditionTelemetryPolicy.cs"),
    (Join-Path $scriptRoot "ExpeditionTelemetryPolicyTests.cs")
) $csc

Invoke-StandaloneSuite "PostZoneRouteReadinessPolicyTests" @(
    (Join-Path $repoRoot "src\PostZoneRouteReadinessPolicy.cs"),
    (Join-Path $scriptRoot "PostZoneRouteReadinessPolicyTests.cs")
) $csc

Write-Host "Erenshor Follow deterministic suites: ALL PASS" -ForegroundColor Green
