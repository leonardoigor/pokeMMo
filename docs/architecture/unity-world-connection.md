# Conexão Unity ↔ Mundo (World) e Troca de Regiões

## Visão Geral
- O cliente Unity conecta via TCP diretamente ao serviço World de uma região, usando um protocolo binário simples.
- A descoberta de regiões e endpoints é feita via Gateway (HTTP), que consulta o Directory.
- A troca de mundo acontece de forma coordenada por duas mensagens: GhostZone (pré-conexão secundaria) e Handoff (migração de conexão).

## Componentes
- Cliente Unity
  - `d:\Dev\pokeMMo\RpgMmo2d\Assets\Scripts\World\Net\WorldClient.cs` (TCP, protocolo binário).
  - `d:\Dev\pokeMMo\RpgMmo2d\Assets\Scripts\World\Game\WorldConnector.cs` (descoberta, conexão primária/ secundária, envio de movimentos).
  - `d:\Dev\pokeMMo\RpgMmo2d\Assets\Scripts\World\Game\PlayerController.cs` (input, interpolação visual baseada na posição do servidor).
- Servidor World
  - `d:\Dev\pokeMMo\World\src\World.Server\Program.cs` (HTTP + TCP). Carrega mapas, valida movimento e coordena Ghost/Handoff.
- Infraestrutura
  - `d:\Dev\pokeMMo\Gateway\k8s\*` (exposição HTTP/TCP).  
  - `d:\Dev\pokeMMo\world.regions.json` (matriz de regiões para deploy local).

## Fluxo de Conexão Inicial
1. Unity solicita spawn e endpoints ao Gateway/Directory.
   - `WorldConnector` usa `DirectoryClient` para obter `regionName`, `LocalTcp` e `ClusterTcp`.
2. Unity escolhe o endpoint (preferindo local quando disponível) e abre TCP.
   - `WorldConnector.cs:64–71` configura `Host` e `Port` e chama `Connect()`.
3. Unity envia configuração do cliente (largura da Ghost Zone).
   - `WorldClient.cs:88–97` envia `ClientConfig(ghostZoneWidth)`.
4. Unity instancia o avatar e sincroniza posição inicial via `PositionUpdate`.
   - `WorldConnector.cs:110–120` posiciona e inicializa; `WorldClient.cs:119–125` atualiza `Position` quando o servidor responde.

## Protocolo TCP
- Cabeçalho de 3 bytes: `[version, type, len]`.
  - `version`: sempre `1` atualmente.
  - `type`: enum `PacketType` (`MoveRequest=0`, `PositionUpdate=1`, `Handoff=2`, `ClientConfig=3`, `GhostZoneEnter=4`, `GhostZoneLeave=5`).  
    Referência: `WorldClient.cs:8–16` e `Program.cs:123–131`.
  - `len`: tamanho do payload em bytes.
- Inteiros no payload são big-endian.
  - Escrita/leitura: `WorldClient.cs:154–169` e `Program.cs:479–494`.

### Pacotes
- `ClientConfig` (cliente → servidor)
  - Payload: `int32 ghostZoneWidth`.  
  - Envio: `WorldClient.cs:88–97`.  
  - Uso no servidor: `Program.cs:318–323` (guarda largura de Ghost Zone por cliente).
- `MoveRequest` (cliente → servidor)
  - Payload: `int32 x`, `int32 y` (posições em grid alvo para o próximo passo).  
  - Envio: `WorldClient.cs:76–86`.  
  - Produção no `WorldConnector` a cada input: `WorldConnector.cs:128–140` (throttle 0.2s).
- `PositionUpdate` (servidor → cliente)
  - Payload: `int32 x`, `int32 y`.  
  - Leitura: `WorldClient.cs:119–125`.  
  - Aplicação no avatar: `WorldConnector.cs:142–149` chama `PlayerController.SyncFromServer`.
- `GhostZoneEnter` / `GhostZoneLeave` (servidor → cliente)
  - Payload: `int32 hostLen`, `ascii host`, `int32 port`.  
  - Leitura: `WorldClient.cs:135–148`.  
  - Ação no cliente: `WorldConnector.cs:270–334` (abre conexão secundária) e `WorldConnector.cs:336–347` (fecha secundária).
- `Handoff` (servidor → cliente)
  - Payload: `int32 hostLen`, `ascii host`, `int32 port`, `int32 x`, `int32 y`.  
  - Leitura: `WorldClient.cs:126–134`.  
  - Ação: `WorldConnector.cs:200–268` (promove secundária ou reconecta primária e reenvia posição).

## Validação de Movimento e Colisão
- Autoridade do servidor: cada `MoveRequest` é analisado e o servidor decide a posição final.
- Normalização do passo: se o cliente tentar mover mais que 1 célula, o servidor reduz para um passo cardinal (sem diagonal) por vez.  
  `Program.cs:386–399`.
- Validação por tile e por área (AABB 16×16):
  - Por tile (compatibilidade): `MovementValidator.IsWalkable(map, x, y)` verifica `object_definitions` e `tile_definitions`.  
    `Program.cs:206–234` e `Program.cs:402–414`.
  - Por área: `MovementValidator.IsAreaWalkable(map, centerX, centerY, halfSize=0.5)` verifica interseção do retângulo do player com tiles/objetos.  
    `Program.cs:229–256`.
  - O servidor valida o ponto médio do deslocamento e o destino:  
    `Program.cs:396–404` (`midX`, `midY`) + `Program.cs:404–414` (destino).
- Atualização de estado e resposta:
  - Se válido, atualiza `pos`, `chunk` e `players[c]`.  
    `Program.cs:405–414`.
  - Sempre responde com `PositionUpdate` (mesmo em bloqueio, devolve posição atual).  
    `Program.cs:415–420`.

## Ghost Zone e Handoff (Troca de Mundo)
- Regiões possuem limites (`MinX/MaxX/MinY/MaxY`) e vizinhos (E/W/N/S).  
  `Program.cs:150–184` (carrega do ambiente) e endpoint HTTP `/world/region/bounds`.
- Ghost Zone
  - Quando o player aproxima-se da fronteira, o servidor envia `GhostZoneEnter` com host/port do vizinho.  
    `Program.cs:354–364` e `Program.cs:430–448` (`ResolveNeighborNear`).
  - O cliente abre conexão secundária, sincroniza configuração e posição atual.  
    `WorldConnector.cs:270–334`.
  - Ao afastar da fronteira, o servidor envia `GhostZoneLeave`; o cliente fecha a secundária.  
    `WorldConnector.cs:336–347`.
- Handoff
  - Se o player cruza os limites da região, o servidor calcula o vizinho correto e envia `Handoff` com destino e posição.  
    `Program.cs:370–379` (fora do bounds) e `Program.cs:448–464` (`BuildHandoffMessage`).
  - O cliente promove a conexão secundária à primária, ou reconecta diretamente ao alvo; reenvia posição.  
    `WorldConnector.cs:200–268`.

## Carregamento de Mapas e Fail-Fast
- O servidor carrega `map.json`, `tile_definitions.json`, `object_definitions.json` de `MAP_DATA_DIR` por subpastas (cada uma é uma região).  
  `Program.cs:1–89`.
- Padrão `MAP_DATA_DIR`: `d:\Dev\pokeMMo\World\data` em ambiente local, ou `/app/data` via Kubernetes.
- Sem mapas, o servidor aborta com `no_maps_loaded`.  
  `Program.cs:90–104` e `Program.cs:262–269`.

## Configuração (Ambiente / Kubernetes)
- Variáveis relevantes:
  - `MAP_DATA_DIR`: diretório de dados (mapas).  
  - `PLAYER_MOVE_INTERVAL_MS`: intervalo mínimo entre movimentos (throttle servidor).  
  - `REGION_MIN_X/REGION_MAX_X/REGION_MIN_Y/REGION_MAX_Y`: bounds.  
  - `NEIGHBOR_EAST/WEST/NORTH/SOUTH`: endpoints dos vizinhos `host:port`.
- Exemplos:
  - `d:\Dev\pokeMMo\World\k8s\configmap.yaml` → `MAP_DATA_DIR=/app/data`.  
  - `d:\Dev\pokeMMo\World\k8s\deployment.yaml` → injeta `PLAYER_MOVE_INTERVAL_MS`, bounds e neighbors.
  - `d:\Dev\pokeMMo\scripts\world_apply.ps1` e `World.Deployer` geram YAML com envs.

## Sequência Operacional Resumida
- Inicial
  - Unity obtém região e endpoints via Gateway/Directory.  
  - Conecta TCP ao World primário e envia `ClientConfig`.  
  - Recebe `PositionUpdate` e inicializa avatar.
- Movimento
  - Unity envia `MoveRequest` em passos discretos (a cada 0.2s).  
  - Servidor normaliza passo, valida AABB midpoint+destino e responde `PositionUpdate`.  
  - Unity interpola visualmente até a posição do servidor.
- Troca de mundo
  - Aproximação da fronteira: servidor envia `GhostZoneEnter`; Unity abre conexão secundária.  
  - Cruzou a fronteira: servidor envia `Handoff`; Unity promove secundária ou reconecta ao novo mundo e reenvia posição.
  - Afastou da fronteira: `GhostZoneLeave`; Unity fecha secundária.

## Logs e Observabilidade
- Servidor
  - HTTP e TCP com logging JSON (`AddElasticsearch` e `AddOpenTelemetry`).  
  - Exemplos de logs: `map_load`, `map_loaded`, `socket_accept`, `move_request`, `move_blocked`, `position_update`, `ghost_enter`, `ghost_leave`, `handoff`.
- Cliente
  - Unity `Debug.Log` nas principais etapas: conexão, fallback IPv4/cluster, envio de movimentos, handoff e ghost.

## Notas de Implementação
- O cliente só se move visualmente após receber `PositionUpdate` do servidor.
- O protocolo usa big-endian por simplicidade e portabilidade; qualquer cliente (não-Unity) pode ser implementado com o mesmo contrato.
- A AABB assume tiles de 16px e player 16×16; se o tamanho do player mudar, ajustar `halfSize` no servidor.

