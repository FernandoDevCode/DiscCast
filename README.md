# DiscCast — revisão 4

Aplicativo Windows x64 para compartilhar tela ou janela com áudio pela internet ou pela rede local.

## Usar a versão portátil

1. Baixe `DiscCast-v4-windows-x64.zip` em **Releases** no GitHub.
2. Clique com o botão direito no ZIP e escolha **Extrair tudo**.
3. Abra a pasta extraída e execute `DiscCast.exe`. O pacote já inclui o runtime .NET e as dependências.

1. Quem vai transmitir escolhe a fonte e deixa **Onde assistir** em **Internet · túnel de teste**.
2. Clique em **Iniciar transmissão**. Quando aparecer **Ao vivo**, clique em **Copiar convite**.
3. Quem recebe o link abre **Assistir**, cola o convite e clica em **Conectar**. Para inverter os papéis, a outra pessoa inicia uma sala no próprio PC.

O PC de quem transmite hospeda a sala. O aplicativo abre um [túnel temporário da Cloudflare](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/) e envia vídeo e áudio por HLS. O serviço é destinado a testes, sem garantia de disponibilidade. Há atraso de alguns segundos. O link HTTPS contém a credencial de acesso da sala, então envie apenas a quem deve assistir. Cada sala recebe um endereço novo, encerrado junto com a transmissão. As conexões de saída do Cloudflare Tunnel precisam estar liberadas na rede.

Para usar **Rede local / VPN**, os dois PCs precisam alcançar a porta TCP 8554 do transmissor pela rede privada.

## Compilar a partir do código

```powershell
.\build.ps1
.\run.ps1
```

O build gera a pasta `dist\DiscCastPortable-v4`.

## Dois modos de áudio

Em **Áudio da transmissão**, escolha um modo ou mantenha **Sem áudio**.

- **Um aplicativo:** captura o processo selecionado e seus filhos. Discord e DiscCast são bloqueados como fontes.
- **PC sem Discord:** captura a reprodução do PC excluindo uma árvore de Discord desktop. Mantenha o Discord aberto, com uma única instância independente. Discord em uma aba de navegador não está coberto. A reprodução do DiscCast neste PC fica bloqueada durante esse modo, inclusive entre instâncias novas do app, para impedir retorno. A reprodução volta a ser permitida ao parar.

O filtro do Windows aceita uma árvore de exclusão por vez. Se não for possível identificar/aplicar o filtro, a sessão começa **sem áudio**, com o motivo visível. Se Discord fechar, reiniciar ou surgir outra árvore independente durante a captura, a transmissão é interrompida. A detecção adicional ocorre a cada 100 ms e não é uma garantia absoluta contra corridas de criação de processos. Não há captura de microfone nem fallback para áudio global sem filtro.

Windows 10 22H2 build 19045 não oferece a API mínima utilizada: nesta versão o app publica vídeo sem áudio. É necessário build 20348 ou posterior para os filtros nativos. A validação física em Windows 10 continua pendente.

## O que foi corrigido

- O modo **Internet** abre e encerra um túnel HTTPS junto com a sala. Ambos os PCs usam o mesmo app. Só quem transmite abre o túnel.
- O player é criado ao conectar em Assistir e removido ao sair da aba, desconectar ou receber um erro. Cada aba exibe seu próprio status.
- Fonte, áudio e qualidade ficam em um formulário mais compacto. O botão de iniciar e, durante a transmissão, os botões de copiar e encerrar ficam na barra inferior.
- Modos de áudio separados, associados à API nativa de inclusão/exclusão.
- Leitura única de stderr/stdout do FFmpeg: corrigido o erro “An async read operation has already been started on the stream”.
- `Live` depende da confirmação de publicação do MediaMTX; iniciar/cancelar/parar têm limpeza coordenada.
- Contratos tipados de fonte, áudio, perfil, estado e serviço em `DiscCast/Contracts/StreamingContracts.cs`.

## Evidência de testes

Ambiente Windows build 26300 x64, nas verificações do projeto:

- Build Release sem erros/avisos.
- Fluxo de teste H.264/AAC entregue pelo túnel HTTPS. FFmpeg decodificou vídeo e áudio, o player WPF abriu o convite duas vezes e um caminho local de sala desconhecida foi recusado. O teste usou mídia sintética e um PC.
- `WindowProbe`: tema WPF, seleção de áudio, layout compacto, validação de convite e troca de abas. Imagens em `.tools/ui-transmit-v3.png` e `.tools/ui-watch-v3.png`.
- `smoke-rtsp.ps1 -WindowProbe`: dois ciclos com fluxo de teste RTSP, saída de vídeo nativa no WPF e verificação de que a janela nativa foi destruída ao voltar para Transmitir. O LibVLC emitiu avisos do decodificador e do Direct3D durante a execução, que terminou com sucesso.
- `AudioProbe`: processo real reproduzindo 997 Hz; amplitude incluída 0,240932, excluída 0,000066 (~71 dB de supressão). Comprova inclusão/exclusão nativa do processo de teste, não uma chamada real do Discord.
- `BroadcastProbe`: usa o mesmo serviço do app para publicar captura de desktop, decodificar frames RTSP e testar a queda segura para vídeo sem áudio quando a fonte de áudio não existe.

Para executar as sondagens com o SDK local instalado pelo build:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.tools/nuget'
.\.tools\dotnet\dotnet.exe run --project tests\WindowProbe -c Release --no-restore
.\.tools\dotnet\dotnet.exe run --project tests\AudioProbe -c Release --no-restore
.\.tools\dotnet\dotnet.exe run --project tests\BroadcastProbe -c Release --no-restore
```

Pendentes: dois PCs reais, áudio real do Discord com vários aplicativos simultâneos, sincronismo de tela/áudio e desempenho sustentado 1080p/60.

Pesquisa e decisões inspiradas no Go Live: [docs/streaming-contracts.md](docs/streaming-contracts.md). Fontes oficiais e limites estão nesse documento. Licenças: [THIRD_PARTY.md](THIRD_PARTY.md).
