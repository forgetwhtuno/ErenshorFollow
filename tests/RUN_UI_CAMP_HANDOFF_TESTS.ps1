param([string]$CscPath = "")

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modRoot = Split-Path -Parent $scriptRoot

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

$csc = Find-Csc
$sourceFiles = @(
    (Join-Path $modRoot "src\TravelOverlayLogic.cs"),
    (Join-Path $modRoot "src\CampmasterIntegrationBridge.cs"),
    (Join-Path $scriptRoot "FollowUiCampHandoffDeterministicTests.cs")
)
foreach ($source in $sourceFiles) { if (-not (Test-Path $source)) { throw "Test source missing: $source" } }

$outputDir = Join-Path $env:TEMP ("ErenshorFollowUiCampTests-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $outputDir | Out-Null
try {
    $output = Join-Path $outputDir "ErenshorFollowUiCampTests.exe"
    $arguments = @("/nologo", "/target:exe", "/optimize+", ('/out:{0}' -f $output)) + $sourceFiles
    & $csc $arguments
    if ($LASTEXITCODE -ne 0) { throw "Follow UI/Camp handoff test compilation failed." }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $outputDir) { Remove-Item -LiteralPath $outputDir -Recurse -Force }
}
