# Componentes de terceiros

- **FFmpeg**, build `ffmpeg-N-128837-g986bab2b47-win64-gpl` do projeto FFmpeg-Builds: GPL-3.0. O executável e o texto da licença estão em `tools/ffmpeg.exe` e `licenses/FFmpeg-GPL-3.0.txt`. Fonte: <https://ffmpeg.org/> e <https://github.com/FFmpeg-Builds/FFmpeg-Builds/tree/autobuild-2026-11-12-15-40>.
- **MediaMTX 1.21.3**: MIT. Binário e licença em `tools/mediamtx.exe` e `licenses/MediaMTX-MIT.txt`. Fonte: <https://github.com/bluenviron/mediamtx/releases/tag/v1.21.3>.
- **LibVLCSharp.WPF e LibVLCSharp 3.10.2**, **VideoLAN.LibVLC.Windows 3.0.24**: LGPL-2.1 ou posterior. Licença em `licenses/LibVLC-LGPL-2.1.txt`. Fontes: <https://code.videolan.org/videolan/LibVLCSharp> e <https://www.videolan.org/vlc/libvlc.html>.
- **.NET 8.0.34**, Microsoft. Avisos em <https://github.com/dotnet/runtime/blob/v8.0.34/LICENSE.TXT>.

O build baixa arquivos de versões fixas e verifica SHA-256 dos pacotes NuGet e dos arquivos FFmpeg e MediaMTX. O SDK .NET é fixado na versão 8.0.428 quando precisa ser obtido.
