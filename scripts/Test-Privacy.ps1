param([string]$PrivateCatalog, [string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$files = @()
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $tracked = @(git -C $root -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git index.' }
    $files += @($tracked | ForEach-Object { Join-Path $root $_ })
} else {
    foreach ($folder in @('src','scripts','.github','docs')) { $files += @(Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {$_.FullName}) }
    $files += @('README.md','README.zh-CN.md','LICENSE','.gitignore','demo-catalog.txt' | ForEach-Object { Join-Path $root $_ })
}
if ($ReleaseDirectory) { $files += @(Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -File | ForEach-Object {$_.FullName}) }
$forbidden = @($files | Where-Object { [IO.Path]::GetFileName($_) -match '(?i)(\.java$|\.mp4(?:\.json)?$|\.log$|^settings\.json$|\.pdb$)' })
if ($forbidden.Count) { throw "Personal/runtime file types in publication inputs: $($forbidden.Count)" }
$patterns = New-Object 'System.Collections.Generic.HashSet[string]'
if ($PrivateCatalog) {
    [void]$patterns.Add([IO.Path]::GetFileName($PrivateCatalog))
    foreach ($line in [IO.File]::ReadAllLines($PrivateCatalog,[Text.Encoding]::UTF8)) {
        foreach ($url in [regex]::Matches($line,'https?://\S+')) { [void]$patterns.Add($url.Value) }
        if ($line -match '^\s*key\s*->\s*(\S+)') { [void]$patterns.Add($Matches[1]) }
        $title=$line.Trim()
        if ($title.Length -ge 8 -and $title -notmatch '->|https?://|^#') { [void]$patterns.Add($title) }
    }
}
$hits = 0
foreach ($file in ($files | Select-Object -Unique)) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing publication input: $file" }
    $bytes=[IO.File]::ReadAllBytes($file)
    $texts=@([Text.Encoding]::UTF8.GetString($bytes),[Text.Encoding]::Unicode.GetString($bytes))
    if ($bytes.Length -gt 1) { $texts += [Text.Encoding]::Unicode.GetString($bytes,1,$bytes.Length-1) }
    foreach ($pattern in $patterns) {
        if (@($texts | Where-Object { $_.Contains($pattern) }).Count) { $hits++; break }
    }
}
if ($hits) { throw "Private content detected in $hits publication file(s). Values were intentionally not printed." }
Write-Output "Privacy check passed: $($files.Count) inputs; $($patterns.Count) private strings checked; no private values printed."
