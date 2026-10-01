# Generates original, local PCM/WAV interface sounds for Power Control PRO.
# No Windows system sound files or external audio are included.

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\assets\sounds'
$warningRoot = Join-Path $root 'warnings'
New-Item -ItemType Directory -Force -Path $warningRoot | Out-Null

$sampleRate = 44100

function Write-Wav([string] $Path, [double[]] $Frequencies, [int] $StepMs, [string] $Wave, [double] $Level) {
  $samplesPerStep = [int]($sampleRate * $StepMs / 1000)
  $totalSamples = $samplesPerStep * $Frequencies.Count
  $dataSize = $totalSamples * 2
  $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
  $writer = [System.IO.BinaryWriter]::new($stream)
  try {
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $dataSize))
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([int]16); $writer.Write([int16]1); $writer.Write([int16]1)
    $writer.Write([int]$sampleRate); $writer.Write([int]($sampleRate * 2)); $writer.Write([int16]2); $writer.Write([int16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]$dataSize)
    for ($step = 0; $step -lt $Frequencies.Count; $step++) {
      $frequency = $Frequencies[$step]
      for ($index = 0; $index -lt $samplesPerStep; $index++) {
        $time = $index / [double]$sampleRate
        $phase = 2 * [Math]::PI * $frequency * $time
        $progress = $index / [double]$samplesPerStep
        $value = switch ($Wave) {
          'square' { 0.75 * [Math]::Sin($phase) + 0.20 * [Math]::Sin($phase * 3) + 0.05 * [Math]::Sin($phase * 5) }
          'triangle' { 0.82 * (2 / [Math]::PI * [Math]::Asin([Math]::Sin($phase))) + 0.18 * [Math]::Sin($phase * 2) }
          'bell' { ([Math]::Sin($phase) + 0.33 * [Math]::Sin($phase * 2.01) + 0.14 * [Math]::Sin($phase * 3.97)) * [Math]::Pow(1 - $progress, 0.7) }
          default { 0.82 * [Math]::Sin($phase) + 0.14 * [Math]::Sin($phase * 2) + 0.04 * [Math]::Sin($phase * 4) }
        }
        $attack = [Math]::Min(1, $index / [double]([Math]::Max(1, $sampleRate / 120)))
        $release = [Math]::Min(1, ($samplesPerStep - $index) / [double]([Math]::Max(1, $sampleRate / 18)))
        $envelope = [Math]::Min($attack, $release)
        $sample = [int16]([Math]::Max(-1, [Math]::Min(1, $value * $envelope * $Level)) * 32767)
        $writer.Write($sample)
      }
    }
  } finally { $writer.Dispose(); $stream.Dispose() }
}

$warnings = @{
  alarm = @(740, 1100, 740, 1100); beep = @(880, 1040); chime = @(523, 784, 1047); siren = @(540, 740, 980, 740, 540)
  double = @(660, 660, 880); triple = @(740, 740, 740); gong = @(294, 440, 587); signal = @(988, 1319, 1568)
  urgent = @(1320, 880, 1320, 880, 1320); digital = @(620, 930, 1240, 1480)
  'system-alert' = @(784, 1047, 784); 'system-warning' = @(523, 659, 784)
}

foreach ($name in $warnings.Keys) {
  # Der klassische Warnton bleibt unverändert erhalten.
  if ($name -eq 'system-warning' -and (Test-Path (Join-Path $warningRoot "$name.wav"))) { continue }
  $wave = if ($name -in 'alarm', 'beep', 'double', 'triple', 'urgent', 'digital', 'system-alert') { 'square' } elseif ($name -eq 'siren') { 'triangle' } elseif ($name -in 'gong', 'chime', 'system-warning') { 'bell' } else { 'sine' }
  Write-Wav (Join-Path $warningRoot "$name.wav") ([double[]]$warnings[$name]) 190 $wave 0.82
}

Write-Host "Created $($warnings.Count) warning sounds in $warningRoot"
