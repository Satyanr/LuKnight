param([string]$SourceRoot = (Join-Path $PSScriptRoot '../output/imagegen/alive-sprites'), [switch]$RigOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -Path (Join-Path $PSScriptRoot 'ReferenceSpriteImport.cs')
$assets = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Assets/Characters/LuKnight'))
$staging = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../output/sprites/alive-staging'))
New-Item -ItemType Directory -Force $staging | Out-Null
# State, anatomical head width/center and shared baseline in atlas-cell source pixels.
# Side views have their own projection width and center, never silhouette-fit per frame.
$specs=@(@('Idle',235,220,440,250,255),@('Walk',203,298,435,225,335),@('Sleep',232,225,431,250,255),@('Grabbed',220,221,432,250,255),@('Falling',225,221,435,250,255),@('Hanging',224,222,432,250,255),@('Climbing',200,250,435,225,285))
if (-not $RigOnly) {
foreach($spec in $specs) {
    $state=$spec[0]
    $folder=Join-Path $staging $state
    New-Item -ItemType Directory -Force $folder | Out-Null
    $sheet=[Drawing.Bitmap]::new((Join-Path $SourceRoot "$state.png"))
    try {
        for($i=0;$i -lt 8;$i++) {
            $cell=[ReferenceSpriteImport]::Cell($sheet,$i,4,2)
            try { [ReferenceSpriteImport]::Save($cell,(Join-Path $folder ('{0}_{1:000}.png' -f $state.ToLowerInvariant(),$i)),[double]$spec[1],[double]$spec[2],[double]$spec[3],[double]$spec[4],[double]$spec[5]) } finally { $cell.Dispose() }
        }
    } finally { $sheet.Dispose() }
}
}
$rigFolder=Join-Path $staging 'Rig'
New-Item -ItemType Directory -Force $rigFolder | Out-Null
$rig=[Drawing.Bitmap]::new((Join-Path $SourceRoot 'Rig.png'))
try {
    # Isolated cutout pieces: crop once during import, retain transparent rounded joints.
    foreach($part in @(@('body',55,10,545,530),@('near-arm',760,180,170,270),@('far-arm',1220,180,180,270),@('near-leg',200,580,235,360),@('far-leg',680,580,230,360))) {
        $region=$rig.Clone([Drawing.Rectangle]::new($part[1],$part[2],$part[3],$part[4]),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $cell=[ReferenceSpriteImport]::Cell($region,0,1,1)
            try { [ReferenceSpriteImport]::SavePart($cell,(Join-Path $rigFolder "$($part[0]).png"),$(if($part[0] -eq 'body'){0}elseif($part[0].EndsWith('arm')){0.25}else{0.20})) } finally { $cell.Dispose() }
        } finally { $region.Dispose() }
    }
} finally { $rig.Dispose() }
foreach($state in @('Idle','Walk','Sleep','Grabbed','Falling','Hanging','Climbing','Rig')) {
    if ($RigOnly -and $state -ne 'Rig') { continue }
    $target=Join-Path $assets $state
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem -LiteralPath $target -Filter '*.png' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
    Get-ChildItem -LiteralPath (Join-Path $staging $state) -Filter '*.png' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target }
}
if ($RigOnly) { Write-Output 'Imported 5 cutout rig parts.' } else { Write-Output 'Imported 56 state frames and 5 cutout rig parts; all full frames use 510x660 canvas.' }
