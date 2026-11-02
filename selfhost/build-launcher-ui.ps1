param([switch]$BuildTests, [string]$IconPath)
$ErrorActionPreference = 'Stop'
$launcherSources = @('NetworkLauncher.UI.cs', 'SaveManager.cs', 'SaveManager.UI.cs', 'ModHttpGuard.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$launcherCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$launcherIcon = if ($IconPath) { (Resolve-Path -LiteralPath $IconPath).Path } else { Join-Path $PSScriptRoot 'launcher-assets\anon.ico' }
$launcherOptions = @('/nologo', '/platform:x64', '/optimize+', '/codepage:65001', '/reference:System.ServiceProcess.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/win32manifest:' + (Join-Path $PSScriptRoot 'launcher-ui.manifest')))
if (Test-Path -LiteralPath $launcherIcon) {
    $launcherOptions += @(('/win32icon:' + $launcherIcon), ('/resource:' + $launcherIcon + ',SeamlessCoop.LauncherIcon'))
}
$launcherOutput = Join-Path $PSScriptRoot 'ersc_launcher.ui.exe'
& $launcherCompiler @launcherOptions /target:winexe ('/out:' + $launcherOutput) @launcherSources
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
if ($BuildTests) {
    foreach ($launcherTest in @('LauncherUI', 'LauncherLayout', 'SaveManager', 'HttpGuard')) {
        $launcherTestOutput = Join-Path $PSScriptRoot ($launcherTest + '.tests.exe')
        & $launcherCompiler @launcherOptions /target:exe ('/main:' + $launcherTest + 'Tests') ('/out:' + $launcherTestOutput) @launcherSources (Join-Path $PSScriptRoot ($launcherTest + '.Tests.cs'))
        if ($LASTEXITCODE -ne 0) { throw "Test build failed: $launcherTest" }
    }
}
Write-Output $launcherOutput
