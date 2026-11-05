param([Parameter(Mandatory=$true)][string]$FfmpegPackage)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root '.build'
$dist = Join-Path $build 'dist'
$name = 'MT001-m3u8-to-mp4-v1.0.0-win-x64'
$portable = Join-Path $dist $name
if (Test-Path -LiteralPath $portable) { throw 'Package directory already exists. Inspect it before packaging again.' }
foreach ($file in @('ffmpeg.exe','ffmpeg-source.tar.gz','build-info.txt','config.log','licenses\COPYING.LGPLv2.1','licenses\LICENSE.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $FfmpegPackage $file))) { throw "Missing FFmpeg publication material: $file" }
}
$expected = [regex]::Match([IO.File]::ReadAllText((Join-Path $FfmpegPackage 'build-info.txt')),'(?m)^([0-9a-f]{64})\s+.*ffmpeg\.exe\s*$').Groups[1].Value
$actual = (Get-FileHash -LiteralPath (Join-Path $FfmpegPackage 'ffmpeg.exe') -Algorithm SHA256).Hash
if (!$expected -or $expected -ne $actual) { throw 'FFmpeg executable does not match the recorded build hash.' }
$sourceExpected = [regex]::Match([IO.File]::ReadAllText((Join-Path $FfmpegPackage 'build-info.txt')),'(?m)^([0-9a-f]{64})\s+.*ffmpeg-source\.tar\.gz\s*$').Groups[1].Value
if (!$sourceExpected -or $sourceExpected -ne (Get-FileHash -LiteralPath (Join-Path $FfmpegPackage 'ffmpeg-source.tar.gz') -Algorithm SHA256).Hash) { throw 'FFmpeg source archive does not match the build record.' }
$license = (& (Join-Path $FfmpegPackage 'ffmpeg.exe') -L 2>&1 | Out-String)
if ($license -notmatch 'GNU\s+Lesser\s+General\s+Public\s+License' -or $license -match '(--enable-gpl|--enable-nonfree)') { throw 'This package requires the reviewed LGPL-only FFmpeg build.' }
New-Item -ItemType Directory -Path (Join-Path $portable 'third-party') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'MT001-m3u8-to-mp4.exe') -Destination $portable
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'ffmpeg.exe') -Destination $portable
foreach ($file in @('demo-catalog.txt','README.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $portable }
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $portable -Recurse
$third = Join-Path $portable 'third-party'
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'ffmpeg-source.tar.gz') -Destination $third
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'build-info.txt') -Destination (Join-Path $third 'ffmpeg-build-info.txt')
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'config.log') -Destination (Join-Path $third 'ffmpeg-config-log.txt')
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'licenses\COPYING.LGPLv2.1') -Destination $third
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'licenses\LICENSE.md') -Destination (Join-Path $third 'FFmpeg-LICENSE.md')
Copy-Item -LiteralPath (Join-Path $root 'scripts\build-ffmpeg.sh') -Destination $third
Compress-Archive -LiteralPath $portable -DestinationPath (Join-Path $dist ($name + '.zip')) -CompressionLevel Optimal
Copy-Item -LiteralPath (Join-Path $build 'MT001-m3u8-to-mp4.exe') -Destination $dist
Copy-Item -LiteralPath (Join-Path $FfmpegPackage 'ffmpeg-source.tar.gz') -Destination (Join-Path $dist 'ffmpeg-7.1.5-source.tar.gz')
$sourceZip = Join-Path $dist 'MT001-m3u8-to-mp4-v1.0.0-source.zip'
git -C $root archive --format=zip "--output=$sourceZip" HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive generation failed.' }
$checksums = @(Get-ChildItem -LiteralPath $dist -File | Where-Object {$_.Name -ne 'SHA256SUMS.txt'} | Sort-Object Name | ForEach-Object { ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) + '  ' + $_.Name })
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$checksums,(New-Object Text.UTF8Encoding($false)))
Write-Output "Portable package: $portable"
Write-Output "Release assets: $dist"
