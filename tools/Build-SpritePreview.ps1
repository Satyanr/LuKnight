$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets\Characters\LuKnight'))
$preview = [System.Drawing.Bitmap]::new(1190,1920)
$graphics = [System.Drawing.Graphics]::FromImage($preview)
$graphics.Clear([System.Drawing.Color]::FromArgb(28,37,49))
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',12)
$states = @('Idle','Walk','Sleep','Grabbed','Falling','Hanging','Climbing','Expressions')
$checked = 0
try {
    for ($row=0; $row -lt $states.Count; $row++) {
        $state = $states[$row]
        $files = @(Get-ChildItem -LiteralPath (Join-Path $assetRoot $state) -Filter '*.png')
        $expected = if ($state -eq 'Expressions') {7} else {16}
        if ($files.Count -ne $expected) { throw "Wrong frame count for $state" }
        foreach ($file in $files) {
            $frame = [System.Drawing.Bitmap]::new($file.FullName)
            try {
                if ($frame.Width -ne 510 -or $frame.Height -ne 660) { throw "Wrong canvas: $file" }
                foreach ($x in @(0,509)) { for ($y=0; $y -lt 660; $y++) { if ($frame.GetPixel($x,$y).A -ne 0) { throw "Clipped side: $file" } } }
                foreach ($y in @(0,659)) { for ($x=0; $x -lt 510; $x++) { if ($frame.GetPixel($x,$y).A -ne 0) { throw "Clipped top/bottom: $file" } } }
                $checked++
            } finally { $frame.Dispose() }
        }
        $graphics.DrawString($state,$font,[System.Drawing.Brushes]::White,10,($row*240))
        $names = if ($state -eq 'Expressions') { @('happy','wink','sad','dizzy','angry','surprised','determined') } else { @(0,2,5,8,10,13,15) | ForEach-Object { $state.ToLowerInvariant()+'_'+$_.ToString('000') } }
        for ($col=0; $col -lt 7; $col++) {
            $frame=[System.Drawing.Bitmap]::new((Join-Path (Join-Path $assetRoot $state) ($names[$col]+'.png')))
            try { $graphics.DrawImage($frame,($col*170),($row*240+20),170,220) } finally { $frame.Dispose() }
        }
    }
    $preview.Save((Join-Path $assetRoot 'Reference\sprite_pack_preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "PASS: $checked transparent 510x660 sprites, correct counts and no clipped canvas borders. Preview updated."
} finally { $graphics.Dispose(); $preview.Dispose(); $font.Dispose() }
