# Fluxos e Funcionamento

## Fluxo — infra up

```mermaid
flowchart TB
  A[Start infra up] --> B[Build push Auth IDP Directory]
  B --> C[kubectl apply -k k8s]
  C --> D[rollout restart apps]
  D --> E[rollout restart databases]
  E --> F[Infra OK sem World]
```

## Fluxo — world up e port-forward

```mermaid
flowchart TB
  A[Start world up] --> B[Aplicar regioes via world_json.ps1]
  B --> C[Verificar Services]
  C --> D{Service existe}
  D -->|Sim| E[Port-forward service world-REGIAO 9090]
  D -->|Nao| F[Aplicar recursos da regiao]
  F --> G[Verificar Service novamente]
  G -->|Sim| E
  G -->|Nao| H[Buscar pod por label app.kubernetes.io/name]
  H -->|Encontrado| I[Port-forward pod PODNAME 9090]
  H -->|Nao| J[Pular regiao]
```

## Fluxo — Descoberta contínua (Directory)

```mermaid
flowchart LR
  A[RegionMonitorService loop 30s] --> B[Para cada regiao]
  B --> C[HTTP GET world-regiao 8082 healthz]
  C -->|200| D[Online true]
  C -->|falha| E[Testar TCP world-regiao 9090]
  E -->|conectado| D
  E -->|falha| F[Online false]
  D --> G[Atualiza status online clusterTcp localTcp lastChecked]
  F --> G
```

## Sequência — Handoff do Cliente Unity

```mermaid
sequenceDiagram
  participant U as Unity Client
  participant G as Gateway
  participant D as Directory Api
  participant W1 as World A
  participant W2 as World B

  U->>G: GET /gateway/directory/regions
  G->>D: GET /directory/regions
  D-->>U: Lista com status e endpoints

  U->>G: POST /gateway/directory/resolve region x y ghostZoneWidth
  G->>D: POST /directory/resolve region x y ghostZoneWidth
  D-->>U: currentRegion nextRegion online tcp endpoints

  U->>W1: Conecta TCP 9090
  U->>G: POST /gateway/directory/resolve movendo
  G->>D: POST /directory/resolve movendo
  D-->>U: nextRegion B quando em ghost zone
  U->>W2: Prepara conexao TCP 9090
  U->>W1: Cruza fronteira fecha conexao
  U->>W2: Estabelece conexao e continua escuta
```

## Considerações de Resiliência

- Directory desacoplado do Gateway e dos mundos, permitindo N regiões dinâmicas.
- Port-forward local tolera ausência temporária de Services/Pods (execução rápida com fallback).
- `infra.cmd` não inclui World; mundos sobem pelo `world.cmd` via JSON.
- Unity consome Directory via Gateway: `/gateway/directory/*`.
