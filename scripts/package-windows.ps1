# Creates a self-contained portable Windows build without downloading a packager.
param([string]$ReleaseDirectory = 'outputs\Power-Control-PRO-Windows')
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path $PSScriptRoot -Parent
$electronDist = Join-Path $projectRoot 'node_modules\electron\dist'
$releaseRoot = Join-Path $projectRoot $ReleaseDirectory
$appRoot = Join-Path $releaseRoot 'resources\app'

if (!(Test-Path $electronDist)) { throw 'Die Electron-Laufzeit wurde nicht gefunden. Bitte zuerst npm install ausführen.' }
if (Test-Path $releaseRoot) { throw "Der Build-Ordner existiert bereits: $releaseRoot. Bitte den alten Build zuerst verschieben oder löschen." }

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
Copy-Item -Path (Join-Path $electronDist '*') -Destination $releaseRoot -Recurse -Force
New-Item -ItemType Directory -Force -Path $appRoot | Out-Null

@('index.html', 'main.js', 'preload.js', 'renderer.js', 'package.json') | ForEach-Object {
  Copy-Item -LiteralPath (Join-Path $projectRoot $_) -Destination $appRoot -Force
}
@('assets', 'css', 'locales') | ForEach-Object {
  Copy-Item -LiteralPath (Join-Path $projectRoot $_) -Destination $appRoot -Recurse -Force
}

$appModules = Join-Path $appRoot 'node_modules'
New-Item -ItemType Directory -Force -Path $appModules | Out-Null
@('auto-launch', 'applescript', 'mkdirp', 'minimist', 'path-is-absolute', 'untildify', 'winreg') | ForEach-Object {
  $source = Join-Path $projectRoot "node_modules\$_"
  if (!(Test-Path $source)) { throw "Benötigte Laufzeit-Abhängigkeit fehlt: $_" }
  Copy-Item -LiteralPath $source -Destination $appModules -Recurse -Force
}

Rename-Item -LiteralPath (Join-Path $releaseRoot 'electron.exe') -NewName 'Power Control PRO.exe'
Write-Host "Portable Windows-App erstellt: $releaseRoot\Power Control PRO.exe"
