param([switch]$Desktop, [switch]$LibVlc, [switch]$WindowProbe)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ffmpeg = Join-Path $root 'dist/DiscCastPortable-v4/tools/ffmpeg.exe'
$mediamtx = Join-Path $root 'dist/DiscCastPortable-v4/tools/mediamtx.exe'
if (-not (Test-Path $ffmpeg) -or -not (Test-Path $mediamtx)) { throw 'Execute build.ps1 antes do teste.' }
$config = Join-Path $env:TEMP "disccast-smoke-$PID.yml"
$port = 18554
$room = 'smoke-test'
$pass = 'testpass'
Set-Content $config "logLevel: warn`nrtsp: true`nrtspAddress: 127.0.0.1:$port`nrtspTransports: [tcp]`nrtmp: false`nhls: false`nwebrtc: false`nsrt: false`nmoq: false`nauthMethod: internal`nauthInternalUsers:`n  - user: publisher`n    pass: $pass`n    ips: [127.0.0.1]`n    permissions:`n      - action: publish`n        path: $room`n  - user: viewer`n    pass: $pass`n    ips: []`n    permissions:`n      - action: read`n        path: $room`npaths:`n  $($room):`n    source: publisher`n"
$server = $null
$publisher = $null
try {
    $server = Start-Process $mediamtx -ArgumentList $config -PassThru -WindowStyle Hidden -WorkingDirectory (Join-Path $root '.tools')
    Start-Sleep -Seconds 1
    if ($server.HasExited) { throw 'MediaMTX não iniciou.' }
    $urlPub = "rtsp://publisher:$pass@127.0.0.1:$port/$room"
    $urlRead = "rtsp://viewer:$pass@127.0.0.1:$port/$room"
    if ($Desktop) {
        $publishArgs = @('-hide_banner','-loglevel','error','-f','gdigrab','-framerate','30','-i','desktop','-vf','scale=1280:720','-c:v','libx264','-preset','ultrafast','-tune','zerolatency','-an','-f','rtsp','-rtsp_transport','tcp',$urlPub)
    } else {
        $publishArgs = @('-hide_banner','-loglevel','error','-re','-f','lavfi','-i','testsrc2=size=320x180:rate=30','-re','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-c:v','libx264','-preset','ultrafast','-tune','zerolatency','-c:a','aac','-f','rtsp','-rtsp_transport','tcp',$urlPub)
    }
    $publisher = Start-Process $ffmpeg -ArgumentList $publishArgs -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    if ($publisher.HasExited) { throw 'FFmpeg não publicou no MediaMTX.' }
    if ($Desktop) { & $ffmpeg -hide_banner -loglevel error -rtsp_transport tcp -i $urlRead -t 2 -map 0:v:0 -f null - 2>&1 | Out-Null }
    else { & $ffmpeg -hide_banner -loglevel error -rtsp_transport tcp -i $urlRead -t 2 -map 0:v:0 -map 0:a:0 -f null - 2>&1 | Out-Null }
    if ($LASTEXITCODE -ne 0) { throw 'Leitura RTSP com vídeo e áudio falhou.' }
    & $ffmpeg -hide_banner -loglevel error -rtsp_transport tcp -i $urlRead -t 2 -f null - 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Segunda leitura RTSP falhou.' }
    $readArgs = @('-hide_banner','-loglevel','error','-rtsp_transport','tcp','-i',$urlRead,'-t','3','-f','null','-')
    $read1 = Start-Process $ffmpeg -ArgumentList $readArgs -PassThru -WindowStyle Hidden -Wait:$false
    $read2 = Start-Process $ffmpeg -ArgumentList $readArgs -PassThru -WindowStyle Hidden -Wait:$false
    $read1.WaitForExit(15000) | Out-Null; $read2.WaitForExit(15000) | Out-Null
    if (-not $read1.HasExited -or -not $read2.HasExited -or $read1.ExitCode -ne 0 -or $read2.ExitCode -ne 0) { throw 'Dois espectadores simultâneos falharam.' }
    if ($LibVlc) {
        $dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
        if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
        $env:DOTNET_CLI_HOME = Join-Path $root '.tools/home'
        $env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
        $probe = Join-Path $root 'tests/PlayerProbe/PlayerProbe.csproj'
        & $dotnet restore $probe --source (Join-Path $root '.tools/packages') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Restore do PlayerProbe falhou.' }
        if ($Desktop) { & $dotnet run --project $probe -c Release --no-restore -- $urlRead }
        else { & $dotnet run --project $probe -c Release --no-restore -- $urlRead --audio }
        if ($LASTEXITCODE -ne 0) { throw 'LibVLC não reproduziu RTSP.' }
    }
    if ($WindowProbe) {
        $dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
        if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
        $env:DOTNET_CLI_HOME = Join-Path $root '.tools/home'
        $env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
        $probe = Join-Path $root 'tests/WindowProbe/WindowProbe.csproj'
        & $dotnet restore $probe --source (Join-Path $root '.tools/packages') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Restore do WindowProbe falhou.' }
        & $dotnet run --project $probe -c Release --no-restore -- $urlRead
        if ($LASTEXITCODE -ne 0) { throw 'A navegação com reprodução no WPF falhou.' }
    }
    if ($Desktop) { Write-Host 'OK: captura real do desktop e dois espectadores RTSP/TCP simultâneos.' }
    else { Write-Host 'OK: publicação H.264/AAC e dois espectadores RTSP/TCP simultâneos.' }
}
finally {
    if ($publisher -and -not $publisher.HasExited) { Stop-Process -Id $publisher.Id -Force }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
    Remove-Item $config -ErrorAction SilentlyContinue
}
