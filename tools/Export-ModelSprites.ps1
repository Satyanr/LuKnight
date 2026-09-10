$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\LuKnight.RenderChecks\LuKnight.RenderChecks.csproj'
dotnet run --project $project -- --export
if ($LASTEXITCODE -ne 0) { throw '3D checks/export failed.' }
& (Join-Path $PSScriptRoot 'Build-SpritePreview.ps1')
