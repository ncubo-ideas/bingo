#Requires -Version 5.1
<#
    build.ps1 - builds the bingo APK inside Docker

    You need neither .NET, the Android SDK nor Java: everything lives in the image
    built from Dockerfile. All it takes is Docker Desktop.

        .\Docker\build.ps1      builds; the APK lands in Docker\out\bingo.apk

    The first run takes about 10 minutes because it builds the image. Later runs,
    one or two.
#>

$ErrorActionPreference = 'Continue'

$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$compose = Join-Path $here 'docker-compose.yml'
$apk     = Join-Path $here 'out\bingo.apk'

docker info 2>&1 | Out-Null
if (-not $?) {
    Write-Host "Docker is not responding. Open Docker Desktop and run this again." -ForegroundColor Red
    exit 1
}

Write-Host "building ..." -ForegroundColor Cyan
$start = Get-Date
docker compose -f $compose --profile build run --rm apk
if ($LASTEXITCODE -ne 0) {
    Write-Host "build failed: the error is above" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $apk)) {
    Write-Host "build finished but $apk is missing" -ForegroundColor Red
    exit 1
}
$secs = [int]((Get-Date) - $start).TotalSeconds
$mb   = [int]((Get-Item $apk).Length / 1MB)
Write-Host "done in $secs s - $apk ($mb MB)" -ForegroundColor Green
