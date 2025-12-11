# 🎮 Creature Realms Online — Game Design Document (GDD)

## 📌 1. Visão Geral

**Creature Realms Online** é um RPG 2D top-down inspirado em mecânicas de captura e batalha de criaturas, com mundo persistente e suporte a multiplayer massivo.

O jogo utiliza mapas independentes operados como **instâncias**, permitindo alta escalabilidade horizontal no backend.

### 🎯 Objetivos do Projeto
- Demonstrar arquitetura cliente-servidor escalável.
- Criar um jogo funcional estilo “captura de criaturas”.
- Usar Unity como cliente 2D.
- Criar backend real-time baseado em WebSockets.
- Desenvolver um mundo modular dividido em dezenas de mapas independentes.

---

## 🎮 2. Plataforma

### Cliente
- **Engine:** Unity 2022+ (2D)
- **Render:** URP / Tilemap System
- **Networking:** WebSocketSharp (ou UnityWebRequest + WS)
- **Builds:** PC, WebGL e Android

### Servidor
- **Linguagem:** C# (.NET)
- **Comunicação:** WebSockets (JSON)
- **Escalabilidade:** Kubernetes + Horizontal Pod Autoscaler
- **Map Instances:** 1 instância por mapa

### Banco de Dados
- **SQL:** PostgreSQL (persistência)
- **Cache:** Redis (estado temporário / jogadores online / cooldowns)

---

## 🌍 3. Ambientação e Tema

O jogador explora o continente fictício chamado **Aetherus**, composto por regiões variadas:
- Florestas
- Praias
- Montanhas
- Cavernas
- Pântanos
- Cidades e vilas

Cada área contém criaturas únicas, eventos, NPCs e desafios.

O estilo gráfico é pixel-art 32x32, com assets gratuitos e livres (ex.: Kenney, 0x72, Caz).

---

## 👤 4. Jogador

### O jogador pode:
- Explorar o mundo no estilo top-down.
- Capturar criaturas selvagens.
- Enfrentar NPCs ou outros jogadores em batalhas por turno.
- Completar missões.
- Entrar e sair de mapas sem telas de loading longas.
- Ver outros jogadores na mesma instância (até limite configurado).

### Progressão
- Nível do treinador
- Nível das criaturas
- Evoluções de criaturas
- Registro de criaturas coletadas
- Missões completas
- Itens adquiridos

---

## 🐾 5. Criaturas

As criaturas são entidades colecionáveis com atributos e tipos variados.

### Atributos principais
- HP  
- Ataque  
- Defesa  
- Velocidade  
- Afinidade elemental (Fogo, Água, Terra etc.)

### Ações
- Ataques normais  
- Habilidades especiais  
- Skills passivas  
- Efeitos de status (queimado, envenenado etc.)

### Evolução
As criaturas podem evoluir com base em:
- Nível  
- Itens  
- Condições especiais (ex.: horário, mapa)

---

## ⚔️ 6. Sistema de Batalha

O combate é **100% calculado no servidor** para evitar cheats.

### Fluxo de batalha
1. Jogador inicia batalha (PvE ou PvP).
2. Servidor cria uma **Battle Instance**.
3. Jogadores escolhem ações.
4. Servidor processa a rodada:
   - Prioridade (velocidade)
   - Ataque vs defesa
   - Efeitos de status
5. Servidor envia resultado da rodada.
6. Cliente exibe animações e feedback.
7. Repetir até fim da batalha.

### Tipos de batalha
- **PvE** (selvagem)
- **PvE NPC** (treinadores / bosses)
- **PvP** (duelos simples)
- **Eventos sazonais** (boss global)

---

## 🗺️ 7. Mapas e Estrutura Modular

O mundo é dividido em **muitos mapas pequenos**:
- Route01
- Route02
- Forest01
- Cave1F
- CityA
- CityB
- Swamp01

Cada mapa é construído como **uma Scene na Unity** ou carregado via **Tilemap JSON**.

### Portais (Map Connections)
Cada mapa contém regiões que permitem transição para outro mapa:

- Saída norte → `/Route02`
- Saída sul → `/CityA`
- Entrada de caverna → `/Cave1F`

---

## 🧱 8. Instâncias de Mapa (MMO Scaling)

Cada mapa existe como uma **instância independente** no servidor.

### Exemplo de Instâncias:
