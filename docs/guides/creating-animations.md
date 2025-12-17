# Guia: Criando Animações Customizadas

Este guia explica como criar e integrar novas animações para o personagem jogador usando o sistema customizado `PlayerAnimatorController`.

## Visão Geral

O sistema de animação do projeto é desacoplado e modular. Em vez de um único script monolítico controlando todas as animações, utilizamos uma interface `IPlayerAnimation` que é gerenciada por um controlador central `PlayerAnimatorController`.

Isso permite que você adicione novos comportamentos de animação (como andar, pular, atacar, piscar, etc.) simplesmente criando novos scripts e adicionando-os ao objeto do jogador.

## Estrutura Principal

### 1. PlayerAnimationContext
Esta estrutura contém todos os dados que uma animação pode precisar para decidir seu estado atual. Ela é passada a cada frame para todas as animações registradas.

```csharp
public struct PlayerAnimationContext
{
    public Transform Avatar;    // O transform do jogador
    public Animator Animator;   // O componente Animator do Unity (se houver)
    public Vector2 Input;       // O vetor de entrada de movimento (Input.GetAxis)
    public bool IsMoving;       // Se o jogador está se movendo
    public Vector2Int GridPos;  // A posição atual no grid
    public float DeltaTime;     // O tempo desde o último frame (Time.deltaTime)
}
```

### 2. Interface IPlayerAnimation
Para criar uma nova animação, você deve implementar esta interface. Ela possui apenas um método:

```csharp
public interface IPlayerAnimation
{
    void Tick(PlayerAnimationContext ctx);
}
```

### 3. PlayerAnimatorController
Este é o componente que deve estar no objeto raiz do Player. Ele busca todos os componentes que implementam `IPlayerAnimation` (se configurados na lista `AnimationBehaviours` ou adicionados dinamicamente) e chama o método `Tick` deles a cada atualização.

---

## Passo a Passo: Criando uma Nova Animação

Vamos criar uma animação simples de exemplo chamada `PulseAnimation`, que faz o personagem "pulsar" (alterar levemente a escala) quando está parado.

### Passo 1: Criar o Script

Crie um novo script C# (ex: `PulseAnimation.cs`) e implemente `MonoBehaviour` e `IPlayerAnimation`.

```csharp
using UnityEngine;
using RpgMmo2d.World.Game; // Namespace onde está o IPlayerAnimation

public class PulseAnimation : MonoBehaviour, IPlayerAnimation
{
    [Header("Configurações")]
    public float PulseSpeed = 5f;
    public float PulseAmount = 0.1f;
    
    private Vector3 _originalScale;

    void Awake()
    {
        // Salva a escala original para referência
        _originalScale = transform.localScale;
    }

    // O método Tick é chamado a cada frame pelo PlayerAnimatorController
    public void Tick(PlayerAnimationContext ctx)
    {
        // Lógica da animação:
        // Se NÃO estiver se movendo, aplica o efeito de pulso
        if (!ctx.IsMoving)
        {
            float scaleOffset = Mathf.Sin(Time.time * PulseSpeed) * PulseAmount;
            ctx.Avatar.localScale = _originalScale + (Vector3.one * scaleOffset);
        }
        else
        {
            // Se estiver se movendo, reseta para a escala original
            ctx.Avatar.localScale = _originalScale;
        }
    }
}
```

### Passo 2: Adicionar ao Jogador

1. Selecione o **Prefab** ou o **GameObject** do Jogador na cena.
2. Certifique-se de que ele já possui o componente `PlayerAnimatorController`.
3. Adicione o seu novo componente `PulseAnimation` ao mesmo GameObject.
4. No componente `PlayerAnimatorController`, localize a lista **Animation Behaviours**.
5. Arraste o componente `PulseAnimation` para dentro dessa lista (ou aumente o tamanho da lista e associe o script).
   - *Nota: O `PlayerAnimatorController` também tenta encontrar animações automaticamente no `Awake` se a lista estiver vazia, mas é recomendável registrar explicitamente.*

### Exemplo: Controlando o Animator do Unity

Se você quiser apenas controlar parâmetros do `Animator` (Mecanim) do Unity, seu script seria assim:

```csharp
public class CombatAnimation : MonoBehaviour, IPlayerAnimation
{
    public void Tick(PlayerAnimationContext ctx)
    {
        if (ctx.Animator == null) return;

        // Supondo que você tenha um parâmetro "IsAttacking" no Animator
        // E que você tenha alguma lógica para detectar ataque (aqui simplificado)
        bool isAttacking = Input.GetKeyDown(KeyCode.Space); 
        
        if (isAttacking)
        {
            ctx.Animator.SetTrigger("Attack");
        }
    }
}
```

---

## Animação Frame-a-Frame (Sem Mecanim)

Se você preferir não usar o Animator do Unity e quiser apenas trocar sprites (ex: uma animação de tiro simples), você pode usar o componente utilitário `SpriteFrameAnimation` que já vem incluso.

### Como usar:

1. Adicione o componente `SpriteFrameAnimation` ao objeto do jogador.
2. Configure no Inspector:
   - **Sprites**: Arraste a lista de sprites da sua animação.
   - **FPS**: Velocidade da animação (quadros por segundo).
   - **Loop**: Se deve repetir.
3. Arraste este componente para a lista `AnimationBehaviours` do `PlayerAnimatorController`.
4. No seu script de controle (ex: `PlayerCombat`), pegue a referência e chame `Play()`:

```csharp
public class PlayerCombat : MonoBehaviour
{
    public SpriteFrameAnimation ShootAnim;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !ShootAnim.IsPlaying)
        {
            ShootAnim.Play(() => 
            {
                Debug.Log("Animação de tiro terminou!");
            });
        }
    }
}
```

O `SpriteFrameAnimation` cuida de contar o tempo e trocar os sprites automaticamente enquanto estiver tocando.

### Referência: Propriedades do SpriteFrameAnimation

| Propriedade | Tipo | Descrição |
| :--- | :--- | :--- |
| **Sprites** | `List<Sprite>` | Lista ordenada dos sprites que compõem a animação. |
| **FPS** | `float` | Quadros por segundo. Define a velocidade da animação. |
| **Loop** | `bool` | Se `true`, a animação reinicia ao terminar. Se `false`, para no último frame e dispara o callback. |
| **PlayOnAwake** | `bool` | Se `true`, inicia a animação automaticamente assim que o objeto nasce. |
| **TargetRenderer** | `SpriteRenderer` | O renderer que será alterado. Se vazio, tenta pegar do próprio objeto. |

### Métodos Públicos

- **`Play(System.Action onComplete = null)`**: Inicia a animação do zero. O callback `onComplete` é chamado ao final (apenas se `Loop` for false).
- **`Stop()`**: Para a animação imediatamente.
- **`IsPlaying`**: Propriedade (get-only) para verificar se a animação está rodando.
