# Contratos de captura e transmissão

Referências oficiais consultadas em 12/11/2026:

- [Engenharia do Go Live](https://discord.com/blog/how-it-all-goes-live-an-overview-of-discords-streaming-technology): pipeline separado de captura, encoder, transporte e reprodução; captura de áudio da árvore do aplicativo; preferência por hardware; áudio e vídeo sincronizados no receptor.
- [Fluxo do Go Live](https://support.discord.com/hc/en-us/articles/360040816151-Go-Live-and-Screen-Share): escolher janela/tela, ajustar qualidade, iniciar e encerrar, controles de espectador.
- [Captura de janelas do Discord](https://support.discord.com/hc/en-us/articles/9410427556375--Windows-Capturing-Application-Window-for-Screen-Share-and-Go-Live): o Discord documenta múltiplos métodos, incluindo Windows Graphics Capture e captura por injeção. O DiscCast mantém FFmpeg/gdigrab conforme a arquitetura solicitada.
- [Microsoft: PROCESS_LOOPBACK_MODE](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode): inclusão ou exclusão de **uma** árvore; build mínimo 20348.

Essas fontes descrevem o comportamento público, não o código privado completo do Discord. O DiscCast mantém RTSP/TCP na rede privada, sem implementar o backend/WebRTC, negociação de codecs ou controle adaptativo de banda do Go Live.

## Tipos e responsabilidades

`Contracts/StreamingContracts.cs` define `VideoSource`, `AudioSelection`, `StreamProfile`, `BroadcastRequest`, `BroadcastStatus` e `IBroadcastSession`. A interface recolhe escolhas; `BroadcastSession` gerencia processos, credenciais, publicação e limpeza. `ChildProcess` é o único proprietário da leitura de stdout/stderr — inclusive quando não existe áudio.

Estados: `Idle → Starting → Live → Stopping → Idle`. Falhas entram em `Failed` e limpam os recursos. `Live` só é emitido depois que MediaMTX confirma publicação. As configurações ficam bloqueadas durante a sessão; cancelar início e fechar a janela aguardam a limpeza.

## Dois modos de áudio

1. **Um aplicativo**: `INCLUDE_TARGET_PROCESS_TREE`, PID escolhido e seus filhos. A árvore não pode conter Discord ou DiscCast. A captura não muda silenciosamente para áudio geral.
2. **PC sem Discord**: `EXCLUDE_TARGET_PROCESS_TREE`, raiz do Discord desktop e seus filhos. Inclui o áudio de reprodução dos demais processos. Exige uma única árvore identificável de Discord aberta; versões independentes simultâneas, ausência ou reinício do Discord causam ausência de áudio no início ou interrupção durante a sessão. Discord em aba de navegador não é identificável por esse filtro e não é coberto.

O Windows aceita um PID de exclusão por captura. Por isso, o segundo modo obtém uma trava exclusiva de áudio entre instâncias do DiscCast e bloqueia reprodução local enquanto captura o PC. Players usam travas compartilhadas; podem existir vários players, mas nenhum junto da captura do PC. O sistema operacional libera os handles em caso de falha do processo.

Os filtros nativos incluem/excluem automaticamente novos filhos da árvore alvo. Uma verificação adicional, a cada 100 ms, interrompe a sessão se surgirem árvores independentes de Discord ou se o alvo morrer. Essa verificação não constitui garantia contra todas as corridas de criação de processos. Não há acesso a microfone, captura global de fallback, subtração de sinais nem codec próprio.

Windows 10 22H2 build 19045: vídeo sem áudio, com motivo visível. A compatibilidade física nessa versão permanece pendente.

## Evidência desta revisão

- `WindowProbe`: renderização WPF real em tema escuro, alternância dos dois modos e layout compacto; fechamento sem exceções. Imagens em `.tools/ui-modern.png`, `.tools/ui-system.png`, `.tools/ui-compact.png`.
- `AudioProbe`: um processo real reproduz um tom de 997 Hz. Inclusão: amplitude 0,240932; exclusão: 0,000066 (aprox. 71 dB de supressão). Evidência do filtro nativo em processo de teste, não teste de conversa real no Discord.
- Renderização de vídeo no WPF, sincronismo e operação entre dois PCs precisam de validação no ambiente de uso.
