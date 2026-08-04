$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root ".venv\Scripts\python.exe"

if (-not (Test-Path -LiteralPath $python)) {
  throw "Local virtual environment is missing. Run scripts/bootstrap.ps1 first."
}

& $python (Join-Path $PSScriptRoot "inspect_and_benchmark_onnx.py")
if ($LASTEXITCODE -ne 0) {
  throw "Ailia ONNX technical gate failed. Unity integration must not continue."
}

Write-Host "Ailia ONNX CPU technical gate passed. Unity parity remains required."
