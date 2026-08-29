param(
  [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assetRoot = Join-Path $ProjectRoot "Assets\KeepBlinking\Resources\CareStation\Worker"
$previewRoot = Join-Path $ProjectRoot "Logs\CareStationWorker"
New-Item -ItemType Directory -Force -Path $previewRoot | Out-Null

$scale = 4.0
$workerWidth = 118.0
$workerHeight = 170.0
$directions = @("Front", "FrontRight", "Right", "BackRight", "Back", "BackLeft", "Left", "FrontLeft")

function Load-Art([string]$name) {
  return [System.Drawing.Bitmap]::new((Join-Path $assetRoot ($name + ".png")))
}

function New-Canvas([int]$width, [int]$height, [System.Drawing.Color]$color) {
  $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $graphics.Clear($color)
  $graphics.Dispose()
  return $bitmap
}

function Draw-Fitted(
  [System.Drawing.Graphics]$graphics,
  [System.Drawing.Bitmap]$sprite,
  [float]$centerX,
  [float]$centerY,
  [float]$width,
  [float]$height,
  [float]$angle = 0.0
) {
  $aspect = $sprite.Width / [double]$sprite.Height
  $drawWidth = $width
  $drawHeight = $height
  if ($drawWidth / $drawHeight -gt $aspect) { $drawWidth = $drawHeight * $aspect }
  else { $drawHeight = $drawWidth / $aspect }
  $x = $centerX - $drawWidth / 2.0
  $y = $centerY - $drawHeight / 2.0
  $state = $graphics.Save()
  $graphics.TranslateTransform($centerX, $centerY)
  $graphics.RotateTransform($angle)
  $graphics.TranslateTransform(-$centerX, -$centerY)
  $graphics.DrawImage($sprite, [System.Drawing.RectangleF]::new($x, $y, $drawWidth, $drawHeight))
  $graphics.Restore($state)
}

function New-Worker(
  [string]$facing = "Front",
  [string]$expression = "Focused",
  [string]$state = "Idle",
  [float]$phase = 0.0
) {
  $canvas = New-Canvas ([int]($workerWidth * $scale)) ([int]($workerHeight * $scale)) ([System.Drawing.Color]::Transparent)
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

  $walk = [Math]::Sin($phase * [Math]::PI * 2.0)
  $bob = 0.0
  $tilt = 0.0
  $arm = 0.0
  $leg = 0.0
  if ($state -eq "Walk") { $bob = [Math]::Abs($walk) * 3.2; $tilt = $walk * 1.8; $arm = $walk * 11.0; $leg = -$walk * 8.0 }
  elseif ($state -eq "Work") { $bob = $walk * 1.1; $tilt = -1.2 + $walk * 1.4; $arm = 8.0 + $walk * 9.0 }
  elseif ($state -eq "Rest") { $bob = $walk * 1.2 - 3.0; $arm = -8.0 }
  elseif ($state -eq "Cheer") { $bob = [Math]::Sin([Math]::Min(1.0, $phase) * [Math]::PI) * 11.0; $arm = -[Math]::Sin([Math]::Min(1.0, $phase) * [Math]::PI) * 42.0 }
  else { $bob = $walk * 1.4; $arm = [Math]::Sin($phase * [Math]::PI * 2.0 + 0.3) * 2.5 }

  $centerX = $workerWidth / 2.0
  function P([float]$x, [float]$y) { return @((($centerX + $x) * $scale), (($workerHeight - ($y + $bob)) * $scale)) }
  $side = $facing -eq "Left" -or $facing -eq "Right"
  $back = $facing.StartsWith("Back")
  $faceShift = if ($facing -eq "Left") { -13.0 } elseif ($facing -eq "Right") { 13.0 } elseif ($facing -eq "FrontLeft") { -6.0 } elseif ($facing -eq "FrontRight") { 6.0 } else { 0.0 }

  $leftLeg = Load-Art "Worker_LeftLeg"; $rightLeg = Load-Art "Worker_RightLeg"
  $leftFoot = Load-Art "Worker_LeftFoot"; $rightFoot = Load-Art "Worker_RightFoot"
  $leftArm = Load-Art "Worker_LeftArm"; $rightArm = Load-Art "Worker_RightArm"
  $leftHand = Load-Art "Worker_LeftHand"; $rightHand = Load-Art "Worker_RightHand"
  $body = Load-Art ("Worker_Body_" + $facing)

  $p = P -17 29; Draw-Fitted $g $leftLeg $p[0] $p[1] (13*$scale) (51*$scale) $leg
  $p = P 17 29; Draw-Fitted $g $rightLeg $p[0] $p[1] (13*$scale) (51*$scale) (-$leg)
  $p = P -20 8; Draw-Fitted $g $leftFoot $p[0] $p[1] (31*$scale) (20*$scale)
  $p = P 20 8; Draw-Fitted $g $rightFoot $p[0] $p[1] (31*$scale) (20*$scale)
  $p = P -45 79; Draw-Fitted $g $leftArm $p[0] $p[1] (14*$scale) (57*$scale) $arm
  $p = P 45 79; Draw-Fitted $g $rightArm $p[0] $p[1] (14*$scale) (57*$scale) (-$arm)
  $p = P 0 98; Draw-Fitted $g $body $p[0] $p[1] (100*$scale) (140*$scale) $tilt
  $p = P -48 54; Draw-Fitted $g $leftHand $p[0] $p[1] (25*$scale) (29*$scale)
  $p = P 48 54; Draw-Fitted $g $rightHand $p[0] $p[1] (25*$scale) (29*$scale)

  if (-not $back) {
    $openEye = Load-Art "Worker_Eye_Open"
    $happyEye = Load-Art "Worker_Eye_Happy"
    $eye = if ($expression -eq "Happy") { $happyEye } else { $openEye }
    if (-not $side) { $p = P ($faceShift - 15) 105; Draw-Fitted $g $eye $p[0] $p[1] (22*$scale) (29*$scale) }
    $p = P ($faceShift + $(if($side){0}else{15})) 105; Draw-Fitted $g $eye $p[0] $p[1] (22*$scale) (29*$scale)
    if (-not $side -and $expression -ne "Happy") {
      $brows = Load-Art $(if($expression -eq "Angry") { "Worker_Brows_Angry" } else { "Worker_Brows_Focused" })
      $p = P $faceShift 123; Draw-Fitted $g $brows $p[0] $p[1] (51*$scale) (18*$scale)
      $brows.Dispose()
    }
    if (-not $side) {
      $mouth = Load-Art $(if($expression -eq "Angry") { "Worker_Mouth_Angry" } elseif($expression -eq "Happy") { "Worker_Mouth_Happy" } else { "Worker_Mouth_Focused" })
      $p = P $faceShift 83; Draw-Fitted $g $mouth $p[0] $p[1] (29*$scale) (13*$scale)
      $mouth.Dispose()
    }
    $openEye.Dispose(); $happyEye.Dispose()
  }

  $leftLeg.Dispose(); $rightLeg.Dispose(); $leftFoot.Dispose(); $rightFoot.Dispose()
  $leftArm.Dispose(); $rightArm.Dispose(); $leftHand.Dispose(); $rightHand.Dispose(); $body.Dispose()
  $g.Dispose()
  return $canvas
}

function Save-WorkerGrid([string]$name, [array]$workers, [int]$columns, [System.Drawing.Color]$background) {
  $rows = [int][Math]::Ceiling($workers.Count / [double]$columns)
  $margin = 36
  $cellWidth = [int]($workerWidth * $scale + $margin * 2)
  $cellHeight = [int]($workerHeight * $scale + $margin * 2)
  $canvas = New-Canvas ($cellWidth * $columns) ($cellHeight * $rows) $background
  $g = [System.Drawing.Graphics]::FromImage($canvas)
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
  for ($i=0; $i-lt$workers.Count; $i++) {
    $column=$i%$columns; $row=[Math]::Floor($i/$columns)
    $g.DrawImageUnscaled($workers[$i], $column*$cellWidth+$margin, $row*$cellHeight+$margin)
    $workers[$i].Dispose()
  }
  $g.Dispose()
  $canvas.Save((Join-Path $previewRoot $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $canvas.Dispose()
}

$single = New-Worker "Front" "Focused" "Idle" 0.15
$single.Save((Join-Path $previewRoot "Worker_Single_Transparent.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$single.Dispose()

$expressionWorkers = @(
  (New-Worker "Front" "Angry" "Idle" 0.1),
  (New-Worker "Front" "Focused" "Idle" 0.3),
  (New-Worker "Front" "Happy" "Idle" 0.5)
)
Save-WorkerGrid "Worker_Expressions_Transparent.png" $expressionWorkers 3 ([System.Drawing.Color]::Transparent)

$directionWorkers = @()
foreach($direction in $directions) { $directionWorkers += New-Worker $direction "Focused" "Idle" 0.2 }
Save-WorkerGrid "Worker_Eight_Directions_Dark.png" $directionWorkers 4 ([System.Drawing.ColorTranslator]::FromHtml("#0B2525"))

foreach($state in @("Idle","Walk","Work")) {
  for($frame=0;$frame-lt6;$frame++) {
    $worker = New-Worker "Front" "Focused" $state ($frame/6.0)
    $frameCanvas = New-Canvas $worker.Width $worker.Height ([System.Drawing.ColorTranslator]::FromHtml("#0B2525"))
    $frameGraphics = [System.Drawing.Graphics]::FromImage($frameCanvas)
    $frameGraphics.DrawImageUnscaled($worker,0,0)
    $frameGraphics.Dispose(); $worker.Dispose()
    $frameCanvas.Save((Join-Path $previewRoot ("Worker_{0}_{1:00}.png" -f $state,$frame)), [System.Drawing.Imaging.ImageFormat]::Png)
    $frameCanvas.Dispose()
  }
}

$phone = New-Canvas 1320 2868 ([System.Drawing.ColorTranslator]::FromHtml("#0B2525"))
$pg = [System.Drawing.Graphics]::FromImage($phone)
$pg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
$panelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#123333"))
$linePen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#56756F"), 3)
$font = [System.Drawing.Font]::new("Arial", 34, [System.Drawing.FontStyle]::Bold)
$textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#E8D8B6"))
for($level=1; $level -le 3; $level++) {
  $top = 220 + ($level-1)*820
  $pg.FillRectangle($panelBrush,80,$top,1160,700)
  $pg.DrawRectangle($linePen,80,$top,1160,700)
  $pg.DrawString(("WORKER LEVEL {0}" -f $level),$font,$textBrush,120,$top+45)
  $count=$level
  $expression=if($level -eq 1){"Angry"}elseif($level -eq 2){"Focused"}else{"Happy"}
  $displayScale=0.60
  $displayWidth=[int]($workerWidth*$scale*$displayScale)
  $displayHeight=[int]($workerHeight*$scale*$displayScale)
  $spacing=330
  $startX=660-(($count-1)*$spacing/2)
  for($i=0;$i-lt$count;$i++) {
    $worker=New-Worker "Front" $expression $(if($i % 2 -eq 0){"Work"}else{"Idle"}) (($i+1)*0.17)
    $dest=[System.Drawing.Rectangle]::new([int]($startX+$i*$spacing-$displayWidth/2),$top+175,$displayWidth,$displayHeight)
    $pg.DrawImage($worker,$dest)
    $worker.Dispose()
  }
}
$pg.Dispose(); $panelBrush.Dispose(); $linePen.Dispose(); $font.Dispose(); $textBrush.Dispose()
$phone.Save((Join-Path $previewRoot "Worker_1320x2868_Level_1_2_3.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$phone.Dispose()

Write-Output "Worker previews written to $previewRoot"
