$exe = Join-Path $PSScriptRoot 'dist/DiscCastPortable-v3/DiscCast.exe'
if (-not (Test-Path $exe)) { throw 'Aplicativo ausente. Execute .\build.ps1 primeiro.' }
& $exe
