# Packages the portable Electron build into one self-extracting Windows EXE.
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path $PSScriptRoot -Parent
$portableRoot = Join-Path $projectRoot 'outputs\Power-Control-PRO-Windows'
$outputPath = Join-Path $projectRoot 'outputs\Power-Control-PRO.exe'
$sevenZip = 'C:\Users\rx8boss\scoop\shims\7z.exe'
$sfxModule = 'C:\Users\rx8boss\scoop\apps\7zip\current\7z.sfx'

if (!(Test-Path $portableRoot)) { throw 'Der portable Windows-Build fehlt. Zuerst npm run package:win ausführen.' }
if (!(Test-Path $sevenZip) -or !(Test-Path $sfxModule)) { throw '7-Zip mit SFX-Modul wurde nicht gefunden.' }
if (Test-Path $outputPath) { throw "Die Einzel-EXE existiert bereits: $outputPath" }

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PowerControlSfx-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
  $archivePath = Join-Path $temporaryRoot 'power-control-pro.7z'
  $configPath = Join-Path $temporaryRoot 'sfx-config.txt'
  @'
;!@Install@!UTF-8!
Title="Power Control PRO"
BeginPrompt="Power Control PRO wird gestartet."
RunProgram="Power Control PRO.exe"
GUIMode="2"
;!@InstallEnd@!
'@ | Set-Content -LiteralPath $configPath -Encoding ASCII

  Push-Location $portableRoot
  try {
    & $sevenZip a -t7z $archivePath '.\*' -mx=7 -m0=LZMA2 -y | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "7-Zip-Archivierung fehlgeschlagen ($LASTEXITCODE)." }
  } finally { Pop-Location }

  $destination = [System.IO.File]::Create($outputPath)
  try {
    foreach ($sourcePath in @($sfxModule, $configPath, $archivePath)) {
      $source = [System.IO.File]::OpenRead($sourcePath)
      try { $source.CopyTo($destination) } finally { $source.Dispose() }
    }
  } finally { $destination.Dispose() }
} finally {
  if (Test-Path $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

Write-Host "Einzelne Windows-EXE erstellt: $outputPath"
