$exe = Join-Path $PSScriptRoot 'DiscCast.exe'
if (-not (Test-Path $exe)) { throw 'DiscCast.exe não encontrado nesta pasta.' }
& $exe
