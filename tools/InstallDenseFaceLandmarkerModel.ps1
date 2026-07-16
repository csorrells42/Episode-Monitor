param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelFolder = Join-Path $repoRoot "dependencies\vision\dense-face-landmarks"
$manifestPath = Join-Path $modelFolder "face_landmarker_manifest.json"

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Dense face landmarker manifest was not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($manifest.modelUrl)) {
    throw "Dense face landmarker manifest does not include modelUrl."
}

$modelFile = if ([string]::IsNullOrWhiteSpace($manifest.modelFile)) {
    "face_landmarker.task"
}
else {
    $manifest.modelFile
}

New-Item -ItemType Directory -Force -Path $modelFolder | Out-Null
$modelPath = Join-Path $modelFolder $modelFile
if ((Test-Path -LiteralPath $modelPath) -and -not $Force) {
    Write-Host "Dense Face Landmarker model already exists: $modelPath"
    Write-Host "Use -Force to download it again."
}
else {
    Write-Host "Downloading Dense Face Landmarker model..."
    Write-Host $manifest.modelUrl
    Invoke-WebRequest -Uri $manifest.modelUrl -OutFile $modelPath
}

$hash = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash
if (-not [string]::IsNullOrWhiteSpace($manifest.sha256) -and $hash -ne $manifest.sha256) {
    throw "Dense Face Landmarker model SHA256 mismatch. Expected $($manifest.sha256), got $hash"
}

$hashPath = "$modelPath.sha256.txt"
Set-Content -LiteralPath $hashPath -Value $hash -Encoding ASCII

Write-Host "Model path: $modelPath"
Write-Host "SHA256: $hash"
Write-Host "Dense model downloaded. The app will still use OpenCV fallback until the MediaPipe runtime is bundled and the manifest inferenceImplementationStatus is set to ready."
