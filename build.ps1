param([switch]$SkipDownload)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$cache = Join-Path $root '.tools'
$packages = Join-Path $cache 'packages'
$out = Join-Path $root 'dist/DiscCastPortable-v4'
New-Item -ItemType Directory -Force $cache, $packages | Out-Null

function Get-PinnedFile($url, $path, $sha256) {
    if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $sha256) {
        if ($SkipDownload) { throw "Arquivo ausente ou divergente: $path" }
        Invoke-WebRequest $url -OutFile $path
    }
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $sha256) { throw "SHA256 incorreto: $path" }
}

$nuget = @{
    'libvlcsharp.wpf.3.10.2' = 'CE90B276CB78D5572B3E1053F9BF41BAC3D18FDF5851332F9E0C2C057932341F'
    'libvlcsharp.3.10.2' = 'A7473E6D7C3595C74D3FB42BB0791053224D07B797ADCEE8C16F00E6F99BB8F9'
    'videolan.libvlc.windows.3.0.24' = '3DA3F37EE2B2B10E5C788D830A3B513658C36521EA1B0F966ED86EB69F41EF3C'
    'microsoft.netcore.app.runtime.win-x64.8.0.34' = '8924CFEF40729B5E5EB75112146626C0B81E45D29436917D32C311C3F795D205'
    'microsoft.windowsdesktop.app.runtime.win-x64.8.0.34' = '3F522C10D9FD93F42035BFB9AD8EE8300AD8B634C5D00E409BFF99DFBCEC548F'
    'microsoft.aspnetcore.app.runtime.win-x64.8.0.34' = 'C968D893EAF83B68A1CEC2765A639FFA2D0DF2F765F2FA675DC8234B76EF3493'
}
foreach ($entry in $nuget.GetEnumerator()) {
    $id = $entry.Key -replace '\.[0-9]+\.[0-9]+\.[0-9]+$', ''
    $version = $entry.Key.Substring($id.Length + 1)
    Get-PinnedFile "https://api.nuget.org/v3-flatcontainer/$id/$version/$($entry.Key).nupkg" (Join-Path $packages "$($entry.Key).nupkg") $entry.Value
}

$mtxZip = Join-Path $cache 'mediamtx.zip'
Get-PinnedFile 'https://github.com/bluenviron/mediamtx/releases/download/v1.21.3/mediamtx_v1.21.3_windows_amd64.zip' $mtxZip 'B2F5246B07CB72961DF03B8D5E8CAD43DC657BCF3F49A87BB031BDF22721AF74'
$ffZip = Join-Path $cache 'ffmpeg.zip'
$cloudflared = Join-Path $cache 'cloudflared-2026.11.0.exe'
Get-PinnedFile 'https://github.com/cloudflare/cloudflared/releases/download/2026.11.0/cloudflared-windows-amd64.exe' $cloudflared 'D5C1D82031DA6253DCDCFF64C944CD0FE063016EDEB2E4FD088075FC9BD52D0A'
$tunnelLicense = Join-Path $cache 'cloudflared-LICENSE.txt'
Get-PinnedFile 'https://raw.githubusercontent.com/cloudflare/cloudflared/2026.11.0/LICENSE' $tunnelLicense '58D1E17FFE5109A7AE296CAAFCADFDBE6A7D176F0BC4AB01E12A689B0499D8BD'
Get-PinnedFile 'https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-11-12-15-40/ffmpeg-N-128837-g986bab2b47-win64-gpl.zip' $ffZip '1A205709149F66A7A8F9E2EEE14E6E1D35AF6D72F6A0C5016434088214197E69'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet -or -not (& $dotnet.Source --list-sdks | Select-String '^8\.0\.')) {
    $dotnetExe = Join-Path $cache 'dotnet/dotnet.exe'
    if (-not (Test-Path $dotnetExe)) {
        if ($SkipDownload) { throw 'SDK .NET 8 ausente.' }
        $installer = Join-Path $cache 'dotnet-install.ps1'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        & $installer -Version '8.0.428' -InstallDir (Join-Path $cache 'dotnet') -NoPath
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar SDK .NET 8.' }
    }
    $dotnetExe = Join-Path $cache 'dotnet/dotnet.exe'
} else { $dotnetExe = $dotnet.Source }

$env:DOTNET_CLI_HOME = Join-Path $cache 'home'
$env:NUGET_PACKAGES = Join-Path $cache 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetExe restore (Join-Path $root 'DiscCast/DiscCast.csproj') -r win-x64 -p:SelfContained=true --source $packages
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }
& $dotnetExe build (Join-Path $root 'DiscCast/DiscCast.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build falhou.' }
& $dotnetExe publish (Join-Path $root 'DiscCast/DiscCast.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

if (-not (Test-Path (Join-Path $cache 'mediamtx/mediamtx.exe'))) { Expand-Archive $mtxZip (Join-Path $cache 'mediamtx') -Force }
if (-not (Get-ChildItem (Join-Path $cache 'ffmpeg') -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue | Select-Object -First 1)) { Expand-Archive $ffZip (Join-Path $cache 'ffmpeg') -Force }
$ffmpeg = Get-ChildItem (Join-Path $cache 'ffmpeg') -Recurse -Filter ffmpeg.exe | Select-Object -First 1
$ffLicense = Get-ChildItem (Join-Path $cache 'ffmpeg') -Recurse -Filter LICENSE.txt | Select-Object -First 1
New-Item -ItemType Directory -Force (Join-Path $out 'tools'), (Join-Path $out 'licenses') | Out-Null
Copy-Item $ffmpeg.FullName (Join-Path $out 'tools/ffmpeg.exe') -Force
Copy-Item $cloudflared (Join-Path $out 'tools/cloudflared.exe') -Force
Copy-Item $tunnelLicense (Join-Path $out 'licenses/cloudflared-Apache-2.0.txt') -Force
Copy-Item (Join-Path $cache 'mediamtx/mediamtx.exe') (Join-Path $out 'tools/mediamtx.exe') -Force
Copy-Item $ffLicense.FullName (Join-Path $out 'licenses/FFmpeg-GPL-3.0.txt') -Force
Copy-Item (Join-Path $cache 'mediamtx/LICENSE') (Join-Path $out 'licenses/MediaMTX-MIT.txt') -Force
$lgpl = Join-Path $out 'licenses/LibVLC-LGPL-2.1.txt'
if (-not (Test-Path $lgpl)) { Invoke-WebRequest 'https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt' -OutFile $lgpl }
Copy-Item (Join-Path $root 'start.ps1') (Join-Path $out 'start.ps1') -Force
Copy-Item (Join-Path $root 'THIRD_PARTY.md') (Join-Path $out 'THIRD_PARTY.md') -Force
Copy-Item (Join-Path $root 'README.md') (Join-Path $out 'README.md') -Force
New-Item -ItemType Directory -Force (Join-Path $out 'docs') | Out-Null
Copy-Item (Join-Path $root 'docs/streaming-contracts.md') (Join-Path $out 'docs/streaming-contracts.md') -Force
Write-Host "Pasta portátil: $out"
