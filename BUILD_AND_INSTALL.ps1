param([string]$GameDir = "", [string]$LunarisLibDir = "")

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
Import-Module (Join-Path $ProjectRoot 'build\ErenshorLocalBuildSupport.psm1') -Force

$GameDir = Get-ErenshorGameDir $GameDir
$LunarisLibDir = Get-LunarisReferenceDir -LunarisLibDir $LunarisLibDir -GameDir $GameDir -ProjectRoot $ProjectRoot -ModRoot $ScriptRoot
$csc = Get-ErenshorCsc
$managed = Join-Path $GameDir 'Erenshor_Data\Managed'
$refs = @(
    (Join-Path $LunarisLibDir 'Lunaris.dll'), (Join-Path $LunarisLibDir '0Harmony.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'), (Join-Path $managed 'netstandard.dll'), (Join-Path $managed 'UnityEngine.dll'), (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.AIModule.dll'), (Join-Path $managed 'UnityEngine.InputLegacyModule.dll'), (Join-Path $managed 'UnityEngine.PhysicsModule.dll'),
    (Join-Path $managed 'UnityEngine.AnimationModule.dll'), (Join-Path $managed 'UnityEngine.TextRenderingModule.dll'), (Join-Path $managed 'UnityEngine.UIModule.dll'),
    (Join-Path $managed 'UnityEngine.UI.dll'), (Join-Path $managed 'Unity.TextMeshPro.dll')
)
Assert-ErenshorReferences $refs
$outDir = Join-Path $ScriptRoot 'bin'; New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$builtDll = Join-Path $outDir 'ErenshorFollow.dll'
$rsp = Join-Path $env:TEMP ('ErenshorFollow-' + [Guid]::NewGuid().ToString('N') + '.rsp')
try {
    $lines = @('/nologo', '/target:library', '/optimize+', ('/out:"{0}"' -f $builtDll))
    $refs | ForEach-Object { $lines += ('/reference:"{0}"' -f $_) }
    Get-ChildItem -LiteralPath (Join-Path $ScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $lines += ('"' + $_.FullName + '"') }
    $fallbackUi = Join-Path $ProjectRoot 'Erenshor-Mod-Suite\shared\ErenshorSuite.UI\StandaloneFallbackUi.cs'
    if (-not (Test-Path -LiteralPath $fallbackUi)) { throw "Missing shared standalone UI source: $fallbackUi" }
    $lines += ('"' + $fallbackUi + '"')
    $lines | Set-Content -LiteralPath $rsp -Encoding ASCII
    Write-Host "Building current local Follow source against $managed" -ForegroundColor Cyan
    Write-Host "Lunaris references: $LunarisLibDir" -ForegroundColor Cyan
    & $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
} finally { Remove-Item -LiteralPath $rsp -Force -ErrorAction SilentlyContinue }
$builtHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $builtDll).Hash.ToLowerInvariant()
$installed = Install-ErenshorPluginDll -BuiltDll $builtDll -GameDir $GameDir -PluginFileName 'ErenshorFollow.dll'
$installedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installed.Destination).Hash.ToLowerInvariant()
if ($builtHash -ne $installedHash) {
    throw "Installed Follow DLL hash mismatch. Built=$builtHash Installed=$installedHash"
}
Write-Host '============================================================' -ForegroundColor Green
Write-Host 'FOLLOW BUILD AND INSTALL SUCCESSFUL' -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
Write-Host "Built DLL: $builtDll`nInstalled DLL: $($installed.Destination)`nBuilt SHA-256: $builtHash`nInstalled SHA-256: $installedHash`nBackup: $($installed.Backup)"
