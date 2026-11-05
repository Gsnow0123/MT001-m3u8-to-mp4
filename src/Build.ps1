param([switch]$Test, [switch]$StageOnly)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $appRoot '.build'
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$references = @('/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll')
$sources = @('CourseCore.cs','Language.cs','ResourcesPanel.cs','SingleVideoForm.cs','CourseUi.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ })
$staged = Join-Path $buildRoot '课程学习助手.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$staged" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath $staged -Destination (Join-Path $buildRoot 'MT001-m3u8-to-mp4.exe') -Force

if ($Test) {
    $tests = Join-Path $buildRoot 'CourseTests.exe'
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /main:CourseTests "/out:$tests" @references @sources (Join-Path $PSScriptRoot 'CourseTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $tests $appRoot
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed. The installed application was not changed.' }
}
if ($StageOnly) { Write-Output "Built: $staged"; return }
try { Copy-Item -LiteralPath $staged -Destination (Join-Path $appRoot '课程学习助手.exe') -Force }
catch { throw "Close the running course assistant, then rerun this build. $($_.Exception.Message)" }
Write-Output 'Build installed successfully.'
