#Requires -Version 5.1
<#
    build.ps1 - builds the web player (WASM) from the app's pages

    Usage:
      .\build.ps1                         brings the pages and the text catalog from BingoApp and builds
      .\build.ps1 -Serve                  also serves it at http://localhost:18160
      .\build.ps1 -Serve -OrganizerPort 6083
                                          with /api pointing at another organizer port
#>

param(
    [switch] $Serve,
    [int] $Port = 18160,
    [int] $OrganizerPort = 18080,
    # The local Puppeteer checkout's Ncubo.Puppeteer.Adapters.Iroh.Browser folder; without it, HTTP only.
    [string] $IrohBrowser = $env:BINGO_IROH_BROWSER
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$app  = Join-Path $here '..\BingoApp\Resources\Raw\wwwroot'
$web  = Join-Path $here 'wwwroot'
$utf8 = [Text.UTF8Encoding]::new($false)

# The app ships png; the web ships the same images as webp.
function Webp([string] $t) { return [regex]::Replace($t, '(assets/[A-Za-z0-9_-]+)\.png', '$1.webp') }

Write-Host "== pages and text catalog from BingoApp"
Copy-Item (Join-Path $here '..\BingoApp\texts.json') (Join-Path $web 'texts.json') -Force
foreach ($f in 'player.js', 'player.css', 'patterns.js', 'language.js') {
    [IO.File]::WriteAllText((Join-Path $web $f), (Webp ([IO.File]::ReadAllText((Join-Path $app $f), $utf8))), $utf8)
}

# One version stamp for every page file, so a new build never mixes with a cached old one.
$sha = [Security.Cryptography.SHA1]::Create()
$all = foreach ($f in 'player.js', 'player.css', 'patterns.js', 'language.js', 'bridge.js', 'bridge-shim.js', 'texts.json', 'entry.js', 'entry.css') {
    [IO.File]::ReadAllText((Join-Path $web $f), $utf8)
}
$V = ([BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes(($all -join '')))) -replace '-', '').Substring(0, 8).ToLower()

$i = [IO.File]::ReadAllText((Join-Path $app 'index.html'), $utf8)
$head = @(
    '<meta name="theme-color" content="#2a0f3d">',
    '<meta name="google" content="notranslate">',
    '<link rel="icon" type="image/png" href="favicon.png">',
    '<link rel="manifest" href="manifest.webmanifest">',
    '<link rel="apple-touch-icon" sizes="512x512" href="icon-512.png">',
    '<link rel="apple-touch-icon" sizes="192x192" href="icon-192.png">',
    "<script src=`"bridge-shim.js?v=$V`"></script>"
) -join "`n"
$swaps = @(
    @('<script src="_framework/hybridwebview.js"></script>', $head),
    @('<script src="language.js"></script>', "<script src=`"language.js?v=$V`"></script>"),
    @('<link rel="stylesheet" href="player.css">', "<link rel=`"stylesheet`" href=`"player.css?v=$V`">`n<link rel=`"stylesheet`" href=`"entry.css?v=$V`">"),
    @('<script src="patterns.js"></script>', "<script src=`"patterns.js?v=$V`"></script>"),
    @('<script src="player.js"></script>', "<script src=`"player.js?v=$V`"></script>`n<script src=`"entry.js?v=$V`"></script>`n<script type=`"module`" src=`"bridge.js?v=$V`"></script>")
)
foreach ($s in $swaps) {
    if (-not $i.Contains($s[0])) { throw "BingoApp's index.html changed: '$($s[0])' is not there anymore. build.ps1 needs updating." }
    $i = $i.Replace($s[0], $s[1])
}
[IO.File]::WriteAllText((Join-Path $web 'index.html'), (Webp $i), $utf8)

$missing = Select-String (Join-Path $web 'index.html'), (Join-Path $web 'player.js'), (Join-Path $web 'player.css') -Pattern 'assets/[A-Za-z0-9_-]+\.webp' -AllMatches |
    ForEach-Object { $_.Matches.Value } | Sort-Object -Unique | Where-Object { -not (Test-Path (Join-Path $web $_)) }
if ($missing) {
    Write-Host "  the app uses images the web does not have as webp: $($missing -join ', ')" -ForegroundColor Yellow
    Write-Host "  convert them from BingoApp's png into wwwroot\assets" -ForegroundColor Yellow
}
Write-Host "  page version: $V"

Write-Host "== building"
Push-Location $here
try {
    dotnet build -c Release -v quiet -nologo "-p:IrohBrowser=$IrohBrowser"
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
} finally { Pop-Location }

$out = Join-Path $here 'bin\Release\net9.0\wwwroot'
Get-ChildItem $web | Copy-Item -Destination $out -Recurse -Force

$irohWeb = if ($IrohBrowser) { Join-Path $IrohBrowser 'wwwroot' } else { $null }
if ($irohWeb -and (Test-Path (Join-Path $irohWeb 'pkg\iroh_browser_bg.wasm'))) {
    $irohOut = Join-Path $out 'iroh'
    New-Item -ItemType Directory -Force (Join-Path $irohOut 'pkg') | Out-Null
    Copy-Item (Join-Path $irohWeb 'iroh-browser.js') $irohOut -Force
    Copy-Item (Join-Path $irohWeb 'pkg\*') (Join-Path $irohOut 'pkg') -Force
    Write-Host "  with the browser iroh bus (Adapters.Iroh.Browser)"
}
Write-Host "== done in $out" -ForegroundColor Green

if ($Serve) {
    $env:ORGANIZER_PORT = "$OrganizerPort"
    node (Join-Path $here 'serve.js') $out $Port
}
