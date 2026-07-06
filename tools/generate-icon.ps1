# Agent Hub 아이콘 생성 (GDI+). 허브 모티브: 라운드 사각 그라데이션 + 중앙 노드 + 4개 연결 노드.
Add-Type -AssemblyName System.Drawing

function New-HubBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

  # 라운드 사각 그라데이션 배경
  $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
  $c1 = [System.Drawing.Color]::FromArgb(24, 28, 42)
  $c2 = [System.Drawing.Color]::FromArgb(40, 54, 90)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45)
  $r = [int]($size * 0.20)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $r, $r, 180, 90)
  $path.AddArc($size - $r, 0, $r, $r, 270, 90)
  $path.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
  $path.AddArc(0, $size - $r, $r, $r, 90, 90)
  $path.CloseFigure()
  $g.FillPath($brush, $path)

  # 연결선 + 노드
  $cx = $size / 2.0
  $cy = $size / 2.0
  $R = $size * 0.30
  $nodeR = [Math]::Max(2.0, $size * 0.075)
  $accent = [System.Drawing.Color]::FromArgb(56, 208, 214)   # teal
  $accent2 = [System.Drawing.Color]::FromArgb(150, 120, 255)  # violet
  $pen = New-Object System.Drawing.Pen($accent, [single][Math]::Max(1.0, $size * 0.028))
  $angles = 45, 135, 225, 315
  $pts = @()
  foreach ($a in $angles) {
    $rad = $a * [Math]::PI / 180.0
    $pts += , @(($cx + $R * [Math]::Cos($rad)), ($cy + $R * [Math]::Sin($rad)))
  }
  foreach ($p in $pts) { $g.DrawLine($pen, [single]$cx, [single]$cy, [single]$p[0], [single]$p[1]) }
  $nb = New-Object System.Drawing.SolidBrush($accent2)
  foreach ($p in $pts) {
    $g.FillEllipse($nb, [single]($p[0] - $nodeR), [single]($p[1] - $nodeR), [single]($nodeR * 2), [single]($nodeR * 2))
  }
  $cb = New-Object System.Drawing.SolidBrush($accent)
  $cr = $nodeR * 1.6
  $g.FillEllipse($cb, [single]($cx - $cr), [single]($cy - $cr), [single]($cr * 2), [single]($cr * 2))

  $g.Dispose()
  return $bmp
}

$root = 'C:\GIT\PRIVATE\agent-hub\AgentHub'
New-Item -ItemType Directory -Force "$root\View\Htmls\icons" | Out-Null

(New-HubBitmap 256).Save("$root\Resources\main_icon.png", [System.Drawing.Imaging.ImageFormat]::Png)
(New-HubBitmap 192).Save("$root\View\Htmls\icons\icon-192.png", [System.Drawing.Imaging.ImageFormat]::Png)
(New-HubBitmap 512).Save("$root\View\Htmls\icons\icon-512.png", [System.Drawing.Imaging.ImageFormat]::Png)

# ICO 패킹 (각 크기 PNG를 ICONDIR로 결합)
$sizes = 16, 32, 48, 256
$pngBytes = @()
foreach ($s in $sizes) {
  $ms = New-Object System.IO.MemoryStream
  (New-HubBitmap $s).Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngBytes += , $ms.ToArray()
}
$icoPath = "$root\Resources\trayicon_32x32.ico"
$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)               # reserved
$bw.Write([UInt16]1)               # type = icon
$bw.Write([UInt16]$sizes.Count)    # image count
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $data = $pngBytes[$i]
  $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([Byte]$dim)            # width
  $bw.Write([Byte]$dim)            # height
  $bw.Write([Byte]0)               # palette
  $bw.Write([Byte]0)               # reserved
  $bw.Write([UInt16]1)             # color planes
  $bw.Write([UInt16]32)            # bpp
  $bw.Write([UInt32]$data.Length)  # size
  $bw.Write([UInt32]$offset)       # offset
  $offset += $data.Length
}
foreach ($data in $pngBytes) { $bw.Write($data) }
$bw.Flush(); $fs.Close()

Write-Output "Generated:"
Get-Item "$root\Resources\trayicon_32x32.ico", "$root\Resources\main_icon.png", "$root\View\Htmls\icons\icon-192.png", "$root\View\Htmls\icons\icon-512.png" | Select-Object Name, Length
