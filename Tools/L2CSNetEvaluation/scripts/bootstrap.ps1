param(
  [string]$Python = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $root ".venv"
$requirements = Join-Path $root "requirements.lock"

if (-not $Python) {
  $Python = (Get-Command python -ErrorAction SilentlyContinue).Source
}

if (-not $Python) {
  throw "Pass -Python with a Python 3.12 executable. Do not install Python globally for this evaluation."
}

if (-not (Test-Path -LiteralPath $venv)) {
  & $Python -m venv $venv
}

$venvPython = Join-Path $venv "Scripts\python.exe"
& $venvPython -m pip install -r $requirements

Write-Host "Local ONNX evaluation environment is ready."
