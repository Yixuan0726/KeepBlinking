param(
  [string]$SourceRoot = "D:\111\KeepBlinking",
  [string]$OutputRoot = "D:\111\KeepBlinking_MacTransfer",
  [string]$DateStamp = "20260804",
  [string]$GitExe = "C:\Users\yuyixuan\AppData\Local\GitHubDesktop\app-3.6.1\resources\app\git\cmd\git.exe"
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Utf8NoBom([string]$Path, [string]$Content) {
  [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Copy-Tree([string]$From, [string]$To, [string[]]$ExcludedDirectories = @()) {
  $arguments = @($From, $To, "/E", "/COPY:DAT", "/DCOPY:DAT", "/R:2", "/W:1", "/XJ", "/NFL", "/NDL", "/NJH", "/NJS", "/NP")
  if ($ExcludedDirectories.Count -gt 0) {
    $arguments += "/XD"
    $arguments += $ExcludedDirectories
  }

  & robocopy.exe @arguments | Out-Null
  if ($LASTEXITCODE -gt 7) {
    throw "Robocopy failed for $From with exit code $LASTEXITCODE."
  }
}

function Assert-SafeTemporaryPath([string]$Path, [string]$ExpectedParent, [string]$RequiredLeafPrefix) {
  $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
  $fullParent = [System.IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\')
  $parent = [System.IO.Directory]::GetParent($fullPath)
  if ($null -eq $parent -or $parent.FullName.TrimEnd('\') -ne $fullParent) {
    throw "Unsafe temporary path outside the transfer directory: $fullPath"
  }
  if (-not [System.IO.Path]::GetFileName($fullPath).StartsWith($RequiredLeafPrefix, [System.StringComparison]::Ordinal)) {
    throw "Unexpected temporary directory name: $fullPath"
  }
}

$source = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
$output = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
$expectedOutput = [System.IO.Path]::GetFullPath("D:\111\KeepBlinking_MacTransfer").TrimEnd('\')
if ($source -ne [System.IO.Path]::GetFullPath("D:\111\KeepBlinking").TrimEnd('\')) {
  throw "Unexpected source root: $source"
}
if ($output -ne $expectedOutput) {
  throw "Unexpected output root: $output"
}
if ($output.StartsWith($source + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "The transfer output must be outside the source project."
}
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
  throw "Source project not found: $source"
}
if (-not (Test-Path -LiteralPath $GitExe -PathType Leaf)) {
  throw "Git executable not found: $GitExe"
}

$zipName = "KeepBlinking_MacTransfer_$DateStamp.zip"
$zipPath = Join-Path $output $zipName
$temporaryParent = "D:\"
$staging = "D:\KBT4"
$verify = "D:\KBV4"
$externalHandoff = Join-Path $output "MAC_HANDOFF.md"
$externalManifest = Join-Path $output "TRANSFER_MANIFEST.txt"
$externalSha = Join-Path $output "TRANSFER_SHA256.txt"

if (Test-Path -LiteralPath $output) {
  throw "Output directory already exists; refusing to overwrite existing transfer files: $output"
}
if (Test-Path -LiteralPath $staging) {
  throw "Temporary staging directory already exists; refusing to overwrite it: $staging"
}
if (Test-Path -LiteralPath $verify) {
  throw "Temporary verification directory already exists; refusing to overwrite it: $verify"
}

[System.IO.Directory]::CreateDirectory($output) | Out-Null
[System.IO.Directory]::CreateDirectory($staging) | Out-Null

Copy-Tree (Join-Path $source "Assets") (Join-Path $staging "Assets")
Copy-Tree (Join-Path $source "Packages") (Join-Path $staging "Packages")
Copy-Tree (Join-Path $source "ProjectSettings") (Join-Path $staging "ProjectSettings")
if (Test-Path -LiteralPath (Join-Path $source "UserSettings")) {
  Copy-Tree (Join-Path $source "UserSettings") (Join-Path $staging "UserSettings")
}

$toolExclusions = @(
  (Join-Path $source "Tools\L2CSNetEvaluation\.venv"),
  (Join-Path $source "Tools\L2CSNetEvaluation\models"),
  (Join-Path $source "Tools\L2CSNetEvaluation\artifacts"),
  (Join-Path $source "Tools\L2CSNetEvaluation\upstream")
)
Copy-Tree (Join-Path $source "Tools") (Join-Path $staging "Tools") $toolExclusions
Copy-Tree (Join-Path $source ".git") (Join-Path $staging ".git")

$rootFiles = @(".gitignore", ".gitattributes", "README.md", "MAC_HANDOFF.md", "KeepBlinking-ui-module-polish.bundle")
foreach ($name in $rootFiles) {
  $from = Join-Path $source $name
  if (Test-Path -LiteralPath $from -PathType Leaf) {
    Copy-Item -LiteralPath $from -Destination (Join-Path $staging $name)
  }
}

$branch = (& $GitExe -C $source branch --show-current 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not read Git branch." }
$commit = (& $GitExe -C $source rev-parse HEAD 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not read Git HEAD." }
$status = (& $GitExe -C $source -c core.quotepath=false status --short --branch 2>&1 | Out-String).TrimEnd()
if ($LASTEXITCODE -ne 0) { throw "Could not read Git status." }
$modified = (& $GitExe -C $source -c core.quotepath=false -c core.autocrlf=false diff --name-status 2>$null | Out-String).TrimEnd()
if ($LASTEXITCODE -ne 0) { throw "Could not read modified file list." }
$untracked = (& $GitExe -C $source -c core.quotepath=false ls-files --others --exclude-standard 2>&1 | Out-String).TrimEnd()
if ($LASTEXITCODE -ne 0) { throw "Could not read untracked file list." }

$baseline = @"
KeepBlinking transfer Git baseline
Generated: 2026-08-04 (Asia/Shanghai)

Branch: $branch
HEAD: $commit

Note: MAC_HANDOFF.md and Tools/CreateMacTransfer.ps1 were added only for this audit/package task.
No commit, push, reset, checkout, clean, or user-file cleanup was performed.

=== git status --short --branch ===
$status

=== Modified tracked files (git diff --name-status) ===
$modified

=== All untracked files (git ls-files --others --exclude-standard) ===
$untracked
"@
Write-Utf8NoBom (Join-Path $staging "TRANSFER_BASELINE_GIT_STATUS.txt") $baseline

$forbiddenNames = Get-ChildItem -LiteralPath $staging -Recurse -Force -File | Where-Object {
  $_.Name -match '(^\.env($|\.)|api.?key|secret|credential|cookie|apple.?id|provision|mobileprovision|\.p12$|\.pfx$|\.cer$|\.pem$|\.key$)'
}
if ($forbiddenNames.Count -gt 0) {
  throw "Sensitive filename candidates found in staging: $($forbiddenNames.FullName -join ', ')"
}

$required = @(
  "Assets",
  "Packages\manifest.json",
  "Packages\packages-lock.json",
  "Packages\com.github.homuler.mediapipe-0.16.3.tgz",
  "ProjectSettings\ProjectVersion.txt",
  "Assets\Scenes\SampleScene.unity",
  "Assets\KeepBlinking\Scripts\Gameplay\EdgeOrbitHarvestMvp.cs",
  "Assets\KeepBlinking\Scripts\Gameplay\FirstLevelSessionController.cs",
  "Assets\KeepBlinking\Scripts\Gameplay\DryCoreBossController.cs",
  "Assets\KeepBlinking\Scripts\Input\EyeInputDebugState.cs",
  "Assets\KeepBlinking\Scripts\Input\CurrentGazeProvider.cs",
  "Assets\KeepBlinking\Scripts\Input\L2CSGazeProvider.cs",
  "Assets\KeepBlinking\Scripts\Input\GazeProviderComparisonController.cs",
  "Assets\KeepBlinking\Shaders\L2CSPreprocess.shader",
  "Assets\KeepBlinking\Resources\L2CSExperimental\l2cs_batch1.onnx",
  ".git",
  ".gitignore",
  ".gitattributes",
  "README.md",
  "MAC_HANDOFF.md",
  "TRANSFER_BASELINE_GIT_STATUS.txt"
)
foreach ($relative in $required) {
  if (-not (Test-Path -LiteralPath (Join-Path $staging $relative))) {
    throw "Required transfer item is missing from staging: $relative"
  }
}

$stagedFiles = Get-ChildItem -LiteralPath $staging -Recurse -Force -File
$fileCount = $stagedFiles.Count
$uncompressedBytes = ($stagedFiles | Measure-Object Length -Sum).Sum
$modelRelative = "Assets\KeepBlinking\Resources\L2CSExperimental\l2cs_batch1.onnx"
$mediaPipeRelative = "Packages\com.github.homuler.mediapipe-0.16.3.tgz"
$modelPath = Join-Path $staging $modelRelative
$mediaPipePath = Join-Path $staging $mediaPipeRelative
$modelInfo = Get-Item -LiteralPath $modelPath
$mediaPipeInfo = Get-Item -LiteralPath $mediaPipePath
$modelHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $modelPath).Hash
$mediaPipeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $mediaPipePath).Hash

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
  $staging,
  $zipPath,
  [System.IO.Compression.CompressionLevel]::Optimal,
  $false
)

$zipInfo = Get-Item -LiteralPath $zipPath
$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash

$manifest = @"
KeepBlinking Mac Transfer Manifest
Generated: 2026-08-04 (Asia/Shanghai)

Archive: $zipName
Archive bytes: $($zipInfo.Length)
Archive SHA-256: $zipHash
Uncompressed file count: $fileCount
Uncompressed bytes: $uncompressedBytes

Git branch: $branch
Git commit: $commit

Key directories:
- Assets (complete, including all .meta files)
- Packages (manifest, lock, and local MediaPipe tarball)
- ProjectSettings
- UserSettings (audited; no credentials found)
- Tools (research scripts/reports; Windows .venv and generated research intermediates excluded)
- .git (including local LFS object)

Local packages:
- com.github.homuler.mediapipe 0.16.3
  Path: $mediaPipeRelative
  Bytes: $($mediaPipeInfo.Length)
  SHA-256: $mediaPipeHash
- com.unity.ai.inference 2.4.1 (Unity Registry; pinned by manifest/packages-lock)

Model:
- Path: $modelRelative
  Bytes: $($modelInfo.Length)
  SHA-256: $modelHash
  Notice: Local research asset. Do not publish or redistribute.

Large transfer dependencies (>= 25 MiB):
$(( $stagedFiles | Where-Object Length -ge 25MB | Sort-Object Length -Descending | ForEach-Object { '- ' + $_.FullName.Substring($staging.Length + 1) + ' | ' + $_.Length + ' bytes' } ) -join "`r`n")

Deliberately excluded:
- Library, Temp, Logs, obj, Build, Builds, MemoryCaptures, Recordings, .vs
- Unity PackageCache
- Windows Python .venv, pip/PyTorch caches
- L2CS duplicate source model directory, upstream repository snapshot, fixed test images/binaries and generated artifacts
- unrelated root outputs/ lesson-plan and presentation files
- credentials, .env files, Apple certificates/profiles/IDs, browser cookies

Validation: pending independent extraction at manifest creation time; the packaging script updates this file only after all checks pass.
"@
Write-Utf8NoBom $externalManifest $manifest
Copy-Item -LiteralPath (Join-Path $source "MAC_HANDOFF.md") -Destination $externalHandoff
Write-Utf8NoBom $externalSha ($zipHash + " *" + $zipName + "`r`n")

[System.IO.Directory]::CreateDirectory($verify) | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $verify)

foreach ($relative in $required) {
  if (-not (Test-Path -LiteralPath (Join-Path $verify $relative))) {
    throw "Archive verification failed; missing: $relative"
  }
}

$verifyFiles = Get-ChildItem -LiteralPath $verify -Recurse -Force -File
if ($verifyFiles.Count -ne $fileCount) {
  throw "Archive verification file count mismatch: expected $fileCount, got $($verifyFiles.Count)."
}

$prohibited = $verifyFiles | Where-Object {
  $relative = $_.FullName.Substring($verify.Length + 1)
  $segments = $relative -split '[\\/]'
  $top = $segments[0]
  $top -in @("Library", "Temp", "Logs", "obj", "Build", "Builds", "MemoryCaptures", "Recordings", ".vs", "outputs") -or
    $segments -contains ".venv" -or
    $segments -contains "PackageCache" -or
    $segments -contains "__pycache__" -or
    $relative -like "Tools\L2CSNetEvaluation\models\*" -or
    $relative -like "Tools\L2CSNetEvaluation\artifacts\*" -or
    $relative -like "Tools\L2CSNetEvaluation\upstream\*"
}
if ($prohibited.Count -gt 0) {
  throw "Archive verification found prohibited paths: $($prohibited.FullName -join ', ')"
}

$verifySensitive = $verifyFiles | Where-Object {
  $_.Name -match '(^\.env($|\.)|api.?key|secret|credential|cookie|apple.?id|provision|mobileprovision|\.p12$|\.pfx$|\.cer$|\.pem$|\.key$)'
}
if ($verifySensitive.Count -gt 0) {
  throw "Archive verification found sensitive filename candidates: $($verifySensitive.FullName -join ', ')"
}

$verifiedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
if ($verifiedHash -ne $zipHash) {
  throw "Archive SHA-256 changed during verification."
}

$validatedManifest = $manifest.Replace(
  "Validation: pending independent extraction at manifest creation time; the packaging script updates this file only after all checks pass.",
  "Validation: PASSED. Independently extracted; required files present; extracted file count $($verifyFiles.Count) matches manifest; prohibited/generated/sensitive paths absent; ZIP SHA-256 recheck matched."
)
Write-Utf8NoBom $externalManifest $validatedManifest

Assert-SafeTemporaryPath $verify $temporaryParent "KBV4"
Assert-SafeTemporaryPath $staging $temporaryParent "KBT4"
Remove-Item -LiteralPath $verify -Recurse -Force
Remove-Item -LiteralPath $staging -Recurse -Force

[PSCustomObject]@{
  ZipPath = $zipPath
  ZipBytes = $zipInfo.Length
  ZipSha256 = $zipHash
  FileCount = $fileCount
  ManifestPath = $externalManifest
  HandoffPath = $externalHandoff
  ShaPath = $externalSha
  Validation = "PASSED"
}
