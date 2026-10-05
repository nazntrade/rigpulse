# Rebuild the original RigPulse vector mark as a multi-resolution Windows icon.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$assets=Join-Path $root 'src\RigPulse\Assets'
$sizes=@(16,20,24,32,40,48,64,128,256)
$images=@()
foreach($size in $sizes){
 $bitmap=[Drawing.Bitmap]::new($size,$size)
 $g=[Drawing.Graphics]::FromImage($bitmap)
 $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $g.ScaleTransform($size/64.0,$size/64.0)
 $bg=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,16,27,20))
 $green=[Drawing.Pen]::new([Drawing.Color]::FromArgb(255,166,255,111),5)
 $green.StartCap=$green.EndCap=[Drawing.Drawing2D.LineCap]::Round
 $green.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
 $border=[Drawing.Pen]::new([Drawing.Color]::FromArgb(255,79,131,79),2)
 $shape=[Drawing.Drawing2D.GraphicsPath]::new()
 $shape.AddArc(2,2,18,18,180,90);$shape.AddArc(44,2,18,18,270,90);$shape.AddArc(44,44,18,18,0,90);$shape.AddArc(2,44,18,18,90,90);$shape.CloseFigure()
 $g.FillPath($bg,$shape);$g.DrawPath($border,$shape)
 $points=[Drawing.PointF[]]@([Drawing.PointF]::new(11,34),[Drawing.PointF]::new(22,34),[Drawing.PointF]::new(28,19),[Drawing.PointF]::new(36,47),[Drawing.PointF]::new(43,30),[Drawing.PointF]::new(53,30))
 $g.DrawLines($green,$points)
 $stream=[IO.MemoryStream]::new();$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$images+=,@{Size=$size;Bytes=$stream.ToArray()}
 if($size -eq 256){$bitmap.Save((Join-Path $assets 'rigpulse.png'),[Drawing.Imaging.ImageFormat]::Png)}
 $stream.Dispose();$g.Dispose();$bitmap.Dispose();$bg.Dispose();$green.Dispose();$border.Dispose();$shape.Dispose()
}
$file=[IO.File]::Create((Join-Path $assets 'rigpulse.ico'));$writer=[IO.BinaryWriter]::new($file)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
$offset=6+16*$images.Count
foreach($im in $images){$dimension=if($im.Size -eq 256){0}else{$im.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$im.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$im.Bytes.Length}
foreach($im in $images){$writer.Write([byte[]]$im.Bytes)}
$writer.Dispose()
