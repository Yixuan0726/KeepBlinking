param(
  [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sourceRoot = Join-Path $ProjectRoot "Tools\ArtSources\CareStation\Worker\Generated"
$outputRoot = Join-Path $ProjectRoot "Assets\KeepBlinking\Resources\CareStation\Worker"
$previewRoot = Join-Path $ProjectRoot "Logs\CareStationWorker"
New-Item -ItemType Directory -Force -Path $outputRoot, $previewRoot | Out-Null

$directionMasterPath = Join-Path $sourceRoot "Worker_DirectionalBodies_Chroma_Master.png"
$componentMasterPath = Join-Path $sourceRoot "Worker_Components_Chroma_Master.png"

function Get-ChromaAlpha([System.Drawing.Color]$color) {
  # The authored chroma backdrop is strongly magenta. Blue-gray bodies, cream eyes,
  # and brown ink never approach this red/blue dominance, so this leaves the art intact.
  $strength = [Math]::Min([int]$color.R, [int]$color.B) - [int]$color.G
  if ($color.R -ge 170 -and $color.B -ge 165 -and $color.G -le 70 -and $strength -ge 150) { return 0 }
  if ($strength -le 20) { return 255 }
  return [int][Math]::Round(255.0 * [Math]::Max(0.0, [Math]::Min(1.0, (200.0 - $strength) / 180.0)))
}

function Convert-ChromaToAlpha([System.Drawing.Bitmap]$source) {
  $result = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $background = @(230.0, 20.0, 225.0)
  for ($y = 0; $y -lt $source.Height; $y++) {
    for ($x = 0; $x -lt $source.Width; $x++) {
      $color = $source.GetPixel($x, $y)
      $alpha = Get-ChromaAlpha $color
      if ($alpha -le 0) {
        $result.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
        continue
      }

      $a = $alpha / 255.0
      if ($a -lt 0.999) {
        $r = [Math]::Max(0, [Math]::Min(255, [Math]::Round(($color.R - (1.0 - $a) * $background[0]) / $a)))
        $g = [Math]::Max(0, [Math]::Min(255, [Math]::Round(($color.G - (1.0 - $a) * $background[1]) / $a)))
        $b = [Math]::Max(0, [Math]::Min(255, [Math]::Round(($color.B - (1.0 - $a) * $background[2]) / $a)))
        $result.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $r, $g, $b))
      } else {
        $result.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $color.R, $color.G, $color.B))
      }
    }
  }
  return $result
}

function Get-AlphaBounds([System.Drawing.Bitmap]$bitmap, [System.Drawing.Rectangle]$search) {
  $left = $search.Right
  $top = $search.Bottom
  $right = $search.Left - 1
  $bottom = $search.Top - 1
  for ($y = $search.Top; $y -lt $search.Bottom; $y++) {
    for ($x = $search.Left; $x -lt $search.Right; $x++) {
      if ($bitmap.GetPixel($x, $y).A -lt 20) { continue }
      if ($x -lt $left) { $left = $x }
      if ($x -gt $right) { $right = $x }
      if ($y -lt $top) { $top = $y }
      if ($y -gt $bottom) { $bottom = $y }
    }
  }
  if ($right -lt $left -or $bottom -lt $top) { throw "No foreground pixels in $search" }
  return [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)
}

function Save-TightCrop(
  [System.Drawing.Bitmap]$source,
  [System.Drawing.Rectangle]$search,
  [string]$outputPath,
  [int]$padding = 6
) {
  $bounds = Get-AlphaBounds $source $search
  $crop = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $cropGraphics = [System.Drawing.Graphics]::FromImage($crop)
  $cropGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $cropGraphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $bounds.Width, $bounds.Height), $bounds, [System.Drawing.GraphicsUnit]::Pixel)
  $cropGraphics.Dispose()

  # Keep only a small transparent extrusion border. Unity UI Image.preserveAspect
  # includes transparent pixels when it fits a sprite. The previous normalized
  # canvases therefore gave every layer a different visible scale and separated
  # the face/limbs from the body at phone size.
  $canvasWidth = $crop.Width + ($padding * 2)
  $canvasHeight = $crop.Height + ($padding * 2)
  $canvas = [System.Drawing.Bitmap]::new($canvasWidth, $canvasHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($canvas)
  $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $graphics.Clear([System.Drawing.Color]::Transparent)
  $graphics.DrawImageUnscaled($crop, $padding, $padding)
  $graphics.Dispose()
  $crop.Dispose()
  $canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $canvas.Dispose()
}

function Get-StableUnityGuid([string]$path) {
  $relative = $path.Substring($ProjectRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/').ToLowerInvariant()
  $bytes = [System.Text.Encoding]::UTF8.GetBytes("keepblinking.worker.art/$relative")
  $md5 = [System.Security.Cryptography.MD5]::Create()
  try {
    return ([System.BitConverter]::ToString($md5.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
  }
  finally {
    $md5.Dispose()
  }
}

function Write-UnityFolderMeta([string]$folderPath) {
  $metaPath = "$folderPath.meta"
  if (Test-Path -LiteralPath $metaPath) { return }
  $guid = Get-StableUnityGuid $folderPath
  $content = @"
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@
  [System.IO.File]::WriteAllText($metaPath, $content.TrimStart(), [System.Text.UTF8Encoding]::new($false))
}

function Write-UnityTextureMeta([string]$pngPath) {
  $metaPath = "$pngPath.meta"
  if (Test-Path -LiteralPath $metaPath) { return }
  $guid = Get-StableUnityGuid $pngPath
  $content = @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 4
  spriteMeshType: 0
  alignment: 9
  spritePivot: {x: 0.5, y: 0}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: iOS
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: 4
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"@
  [System.IO.File]::WriteAllText($metaPath, $content.TrimStart(), [System.Text.UTF8Encoding]::new($false))
}

$directionSource = [System.Drawing.Bitmap]::new($directionMasterPath)
$directionAlpha = Convert-ChromaToAlpha $directionSource
$directionSource.Dispose()
$directions = @("Front", "FrontRight", "Right", "BackRight", "Back", "BackLeft", "Left", "FrontLeft")
for ($index = 0; $index -lt $directions.Count; $index++) {
  $column = $index % 4
  $row = [Math]::Floor($index / 4)
  $search = [System.Drawing.Rectangle]::new($column * 384, $row * 512, 384, 512)
  Save-TightCrop $directionAlpha $search (Join-Path $outputRoot ("Worker_Body_{0}.png" -f $directions[$index])) 8
}
$directionAlpha.Dispose()

$componentSource = [System.Drawing.Bitmap]::new($componentMasterPath)
$componentAlpha = Convert-ChromaToAlpha $componentSource
$componentSource.Dispose()

$components = @(
  @{ Name = "Worker_Eyes_Open"; Rect = @(420, 50, 340, 190) },
  @{ Name = "Worker_Eye_Open"; Rect = @(465, 70, 125, 155) },
  @{ Name = "Worker_Brows_Angry"; Rect = @(780, 55, 330, 180) },
  @{ Name = "Worker_Brows_Focused"; Rect = @(1150, 55, 350, 180) },
  @{ Name = "Worker_Face_Happy"; Rect = @(75, 325, 300, 185) },
  @{ Name = "Worker_Eye_Happy"; Rect = @(105, 345, 105, 90) },
  @{ Name = "Worker_Mouth_Angry"; Rect = @(415, 335, 330, 165) },
  @{ Name = "Worker_Mouth_Focused"; Rect = @(790, 335, 325, 165) },
  @{ Name = "Worker_Mouth_Happy"; Rect = @(1160, 330, 335, 170) },
  # The arm master includes a hand lower in the same column. Crop the painted
  # shaft at its wrist and use the separately-authored hand layer at runtime.
  @{ Name = "Worker_LeftArm"; Rect = @(65, 500, 275, 150) },
  @{ Name = "Worker_RightArm"; Rect = @(430, 500, 295, 150) },
  @{ Name = "Worker_LeftHand"; Rect = @(790, 500, 325, 270) },
  @{ Name = "Worker_RightHand"; Rect = @(1160, 500, 340, 270) },
  @{ Name = "Worker_LeftLeg"; Rect = @(55, 755, 310, 269) },
  @{ Name = "Worker_RightLeg"; Rect = @(420, 755, 320, 269) },
  @{ Name = "Worker_LeftFoot"; Rect = @(775, 755, 350, 269) },
  @{ Name = "Worker_RightFoot"; Rect = @(1145, 755, 370, 269) }
)

foreach ($component in $components) {
  $rect = [System.Drawing.Rectangle]::new($component.Rect[0], $component.Rect[1], $component.Rect[2], $component.Rect[3])
  $path = Join-Path $outputRoot ($component.Name + ".png")
  Save-TightCrop $componentAlpha $rect $path 6
}
$componentAlpha.Dispose()

Write-UnityFolderMeta $outputRoot
Get-ChildItem -LiteralPath $outputRoot -Filter "*.png" -File | ForEach-Object {
  Write-UnityTextureMeta $_.FullName
}

Write-Output "Generated $($directions.Count + $components.Count) transparent Worker runtime sprites in $outputRoot"
