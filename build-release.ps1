#Requires -Version 5.1
<#
.SYNOPSIS
    Baut, packt und laedt einen Schnack-Release auf GitHub hoch.
.PARAMETER Version
    Versionsnummer (z.B. "1.3.0"). Wenn nicht angegeben, wird sie aus Schnack.csproj gelesen.
.PARAMETER SkipUpload
    Nur lokal bauen und packen, nicht zu GitHub hochladen.
.PARAMETER Force
    Git-Dirty-Check ueberspringen (fuer Notfall-Releases).
#>
param(
    [string]$Version = "",
    [switch]$SkipUpload,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoUrl = "https://github.com/ha-lamb/Schnack"
$PackId  = "Schnack"
$Authors = "Hauke Lamb"
$Icon    = "Schnack/Resources/tray-icon.ico"

# -- 0. .NET Roll-Forward fuer vpk (vpk 0.0.1298 zielt auf net9, Rechner hat net10) --
$env:DOTNET_ROLL_FORWARD = "Major"

# -- 1. vpk CLI pruefen ------------------------------------------------------
Write-Host "Pruefe vpk CLI..." -ForegroundColor Cyan
try {
    $null = & vpk --help 2>&1
} catch {
    Write-Error "vpk ist nicht installiert. Bitte ausfuehren: dotnet tool install -g vpk`nDanach Terminal neu starten und Skript erneut ausfuehren."
    exit 1
}
Write-Host "vpk OK" -ForegroundColor Green

# -- 2. GitHub-Token pruefen (wenn nicht SkipUpload) -------------------------
if (-not $SkipUpload) {
    if (-not $env:VPK_GITHUB_TOKEN) {
        Write-Error "VPK_GITHUB_TOKEN ist nicht gesetzt.`nBitte generieren: GitHub -> Settings -> Developer settings -> Personal access tokens (Classic) -> scope: repo`nDann: setx VPK_GITHUB_TOKEN `"ghp_...`" und Terminal neu starten."
        exit 1
    }
    Write-Host "GitHub-Token: vorhanden" -ForegroundColor Green
}

# -- 3. Git-Status pruefen ---------------------------------------------------
if (-not $Force) {
    $gitStatus = & git status --porcelain 2>&1
    if ($gitStatus) {
        Write-Warning "Git-Tree ist nicht sauber. Nicht committete Aenderungen:`n$gitStatus"
        Write-Warning "Fortfahren mit -Force oder zuerst committen."
        exit 1
    }
    Write-Host "Git-Status: sauber" -ForegroundColor Green
}

# -- 4. Version bestimmen ----------------------------------------------------
if (-not $Version) {
    $csproj = Get-Content "Schnack/Schnack.csproj" -Raw
    if ($csproj -match '<Version>([^<]+)<\/Version>') {
        $Version = $Matches[1].Trim()
    } else {
        Write-Error "Konnte <Version> nicht aus Schnack/Schnack.csproj lesen."
        exit 1
    }
}
Write-Host "Version: $Version" -ForegroundColor Cyan

# -- 5. dotnet publish -------------------------------------------------------
Write-Host "Publiziere Schnack..." -ForegroundColor Cyan
& dotnet publish Schnack `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o publish/win-x64
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish fehlgeschlagen."
    exit 1
}
Write-Host "Publish OK" -ForegroundColor Green

# -- 6. vpk pack -------------------------------------------------------------
Write-Host "Packe mit vpk..." -ForegroundColor Cyan
& vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir publish/win-x64 `
    --mainExe Schnack.exe `
    --packTitle "Schnack" `
    --packAuthors $Authors `
    --icon $Icon `
    --outputDir releases
if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk pack fehlgeschlagen."
    exit 1
}

# -- 7. Verifikation ---------------------------------------------------------
# vpk erzeugt "Schnack-win-Setup.exe" (nicht "Schnack-Setup.exe")
$setupExe = "releases/Schnack-win-Setup.exe"
if (-not (Test-Path $setupExe)) {
    Write-Error "Setup-EXE nicht gefunden: $setupExe"
    exit 1
}
$sizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host "Setup-EXE: $setupExe ($sizeMb MB)" -ForegroundColor Green

# -- 8. Upload zu GitHub -----------------------------------------------------
if (-not $SkipUpload) {
    Write-Host "Lade zu GitHub hoch..." -ForegroundColor Cyan
    & vpk upload github `
        --repoUrl $RepoUrl `
        --publish `
        --releaseName "v$Version" `
        --tag "v$Version" `
        --token $env:VPK_GITHUB_TOKEN
    if ($LASTEXITCODE -ne 0) {
        Write-Error "vpk upload fehlgeschlagen."
        exit 1
    }
    Write-Host ""
    Write-Host "Release erfolgreich hochgeladen!" -ForegroundColor Green
    Write-Host "GitHub-Release: $RepoUrl/releases/tag/v$Version" -ForegroundColor Cyan
} else {
    Write-Host ""
    Write-Host "SkipUpload: kein Upload." -ForegroundColor Yellow
}

Write-Host "Setup-EXE (lokal): $(Resolve-Path $setupExe)" -ForegroundColor Cyan
Write-Host "Fertig." -ForegroundColor Green
