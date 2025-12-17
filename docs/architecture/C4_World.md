# Arquitetura C4 — World Server (Região)

Este documento detalha a arquitetura do componente `World Server`, responsável por gerenciar a lógica de jogo, sincronização de jogadores e persistência de estado em memória para uma região específica do mundo.

## Visão Geral

O **World Server** é uma aplicação .NET 8 (Console/BackgroundService) que opera como um servidor autoritativo para uma "região" do jogo (um mapa). Ele aceita conexões TCP de clientes (Unity), valida movimentos, gerencia "Ghost Zones" (áreas de fronteira) e se comunica com o `Directory API` para descoberta de serviços.

---

## Nível 1 — Contexto do Sistema

Diagrama de contexto mostrando como o World Server se encaixa no ecossistema.

```mermaid
flowchart LR
    subgraph Cluster["Cluster de Jogo"]
        World["World Server (Região)"]:::sys
        Directory[Directory API]:::sys
    end

    Player[Jogador / Cliente Unity]:::person
    FS[Sistema de Arquivos]:::ext

    Player -->|TCP 9090| World
    Player -->|HTTP GET| Directory
    
    World -->|HTTP POST| Directory
    World -->|Leitura JSON| FS

    classDef person fill:#08427b,stroke:#052e56,color:#fff;
    classDef sys fill:#1168bd,stroke:#0b4884,color:#fff;
    classDef ext fill:#999999,stroke:#666666,color:#fff;
```

---

## Nível 2 — Containers

O World Server é composto por camadas lógicas internas, mas opera como um único container implantável. Ele interage fortemente com o disco (para carregar mapas) e com a rede.

```mermaid
flowchart TB
    subgraph WorldServer["World Server Container"]
        TCP["TCP Listener (Socket)"]:::cont
        HTTP["HTTP API (Minimal)"]:::cont
        Logic[Game Logic Core]:::cont
        Store["Map Store (In-Memory)"]:::db
    end

    Dir[Directory API]:::ext

    TCP -->|Pacotes| Logic
    HTTP -->|Infos| Store
    Logic -->|Validação| Store
    Logic -->|Heartbeat| Dir

    classDef cont fill:#438dd5,stroke:#2e6295,color:#fff;
    classDef db fill:#2f95d7,stroke:#206897,color:#fff;
    classDef ext fill:#999999,stroke:#666666,color:#fff;
```

---

## Nível 3 — Componentes

Detalhe interno da aplicação `World.Server`. Aqui vemos como o código está estruturado.

### Principais Componentes

1.  **Server (Core)**: O loop principal. Aceita clientes TCP e despacha o stream de dados para o `PacketRouter`.
2.  **PacketRouter**: Analisa o cabeçalho do pacote (`PacketType`) e invoca o `Handler` correspondente.
3.  **Handlers**: Implementam `IPacketHandler`. Cada um trata um tipo de mensagem (ex: `MoveRequest`, `ClientConfig`).
4.  **WorldManager**: O "cérebro". Gerencia a lista de sessões (`ClientSession`), atribui IDs, e executa o broadcast de estado (`PlayersSnapshot`).
5.  **MapStore & MapLoader**: Responsáveis por ler os arquivos JSON (`map.json`, `tile_definitions.json`) e fornecer dados de colisão para o validador.
6.  **MovementValidator**: Garante que os jogadores não atravessem paredes ou saiam dos limites sem autorização.

```mermaid
flowchart TB
    subgraph App["World.Server Application"]
        Server["Server (BackgroundService)"]:::comp
        Router[PacketRouter]:::comp
        Handlers[Packet Handlers]:::comp
        WM[WorldManager]:::comp
        Validator[MovementValidator]:::comp
        Store[MapStore]:::comp
        Loader[MapLoader]:::comp
        DirClient[DirectoryClient]:::comp
    end

    Server -->|Cria Sessão| WM
    Server -->|Payload| Router
    Router -->|Dispatch| Handlers
    Handlers -->|Update State| WM
    Handlers -->|Valida| Validator
    Validator -->|Consulta Colisão| Store
    WM -->|Sync| DirClient
    Loader -->|Load Data| Store

    classDef comp fill:#85bbf0,stroke:#5d82a8,color:#000;
```

## Fluxos Principais

### 1. Conexão de Jogador e Handshake
1.  **TCP Connect**: O cliente conecta na porta 9090.
2.  **Session Created**: `Server` chama `WorldManager.CreateSession`.
3.  **Client Config**: Cliente envia pacote `ClientConfig` (com Username e GhostZoneWidth).
4.  **Response**: Servidor responde com `GhostZoneInfo` (limites do mapa) e `PlayerInfo` (atribui ID ao jogador).
5.  **DeadZones**: Servidor envia lista de obstáculos estáticos para o cliente.

### 2. Movimentação e Sincronização
1.  **MoveRequest**: Cliente envia intenção de movimento (X, Y).
2.  **Validation**: `MoveRequestHandler` usa `MovementValidator` para checar se o destino é válido (não é parede, dentro dos limites).
3.  **Update**: Se válido, `ClientSession` é atualizada com nova posição.
4.  **Broadcast**: Periodicamente (ou por evento), `WorldManager.BroadcastPlayersSnapshot` envia a lista de todos os jogadores visíveis para todos os clientes conectados.

### 3. Ghost Zones (Fronteiras)
- Quando um jogador se aproxima da borda do mapa (definida em `GhostZoneWidth`), o cliente inicia conexão com o servidor da região vizinha.
- O servidor atual continua autoritativo até que o jogador cruze efetivamente a linha de fronteira (Handoff).

## Estrutura de Dados (Mapas)
O servidor carrega mapas de uma pasta configurada (`MAP_DATA_DIR`).
- **map.json**: Matriz de tiles e objetos.
- **tile_definitions.json**: Propriedades dos tiles (walkable, etc).
- **object_definitions.json**: Propriedades dos objetos (blocksMovement, etc).
