# Propuesta — probar que el riel de cuenta rechaza la cuenta fondeada, sin conectarla

**Estado: propuesta. Nada implementado.** Pedido explícito: una **prueba**, no un argumento, de que
`BotSafety.VerifyAccount` rechaza `2127534`. Y sin ponerla en línea para averiguarlo.

## Por qué hoy no se puede probar

```csharp
try { all = Account.All.ToList(); }        // BotGuardrails.cs:74
```

`Account.All` es un **estático global de NinjaTrader**. La función que decide y la función que
consulta al mundo son la misma, así que para ejercitarla hay que tener la plataforma corriendo, con
las cuentas que uno quiera probar realmente presentes. Para probar que rechaza la cuenta fondeada
habría que **conectar la cuenta fondeada**, que es exactamente lo que no se puede hacer.

Es la misma forma que GuardianCore resolvió el primer día y que el riel de los bots no heredó:
`GuardianCore` no llama a nadie, recibe cuatro puertos inyectados, y por eso sus 175 tests corren sin
plataforma, sin cuenta y sin conexión. `BotSafety` es el único punto del proyecto que decide algo de
riesgo mirando un global.

## La forma propuesta: separar la decisión del mundo

Un archivo nuevo, **puro, sin una sola referencia a NinjaTrader**:

```
nt/bots/BotAccountRule.cs
```

```csharp
public sealed class AccountFacts            // lo mínimo para decidir, y nada más
{
    public string Name { get; }
    public string Provider { get; }
    public bool   Connected { get; }
}

public sealed class AccountVerdict
{
    public bool   Allowed { get; }
    public string Chosen  { get; }          // el nombre elegido, o null
    public string Reason  { get; }          // por qué se negó, en el texto que ya se loguea
}

public static class BotAccountRule
{
    public static AccountVerdict Decide(IReadOnlyList<AccountFacts> accounts, string target);
}
```

`BotSafety.VerifyAccount` queda como lo que debería haber sido siempre: **un adaptador de cinco
líneas** que lee `Account.All`, lo proyecta a `AccountFacts`, llama a `Decide`, y si el veredicto
permite, devuelve el `Account` real correspondiente. Cero lógica de decisión propia.

Nada del comportamiento cambia. Lo único que cambia es **dónde vive la decisión**, y con eso deja de
necesitar la plataforma para ser interrogada.

## Cómo entra en la suite que ya existe

`nt/bots/BotAccountRule.cs` no referencia NinjaTrader, así que el proyecto de tests puede compilarlo
directo, sin tocar el resto de `nt/`:

```xml
<Compile Include="..\..\nt\bots\BotAccountRule.cs" Link="Bots\BotAccountRule.cs" />
```

Queda dentro de `dotnet test`, con los otros 175. Sin plataforma, sin cuenta, sin conexión, en CI.

## Los casos, y el que pediste primero

| caso | cuentas presentes | esperado |
|---|---|---|
| **la cuenta fondeada sola** | `9999999/Provider31` (conectada) | **DENY** — no hay ninguna llamada `Sim101` |
| **la fondeada junto a la buena** | `Sim101/Simulator`, `9999999/Provider31` | **ALLOW, y elige `Sim101`** — el caso real de esta máquina |
| nombre correcto, proveedor real | `Sim101/Provider31` | DENY — `Provider != Simulator` |
| nombre parecido | `Sim1010/Simulator`, `sim101/Simulator` | DENY — la comparación es ordinal y exacta |
| dos con el mismo nombre | `Sim101/Simulator` ×2 | DENY — `matches.Count != 1` |
| la buena, desconectada | `Sim101/Simulator` (desconectada) | DENY |
| lista vacía | — | DENY |

El segundo es el que importa de verdad: **no alcanza con que rechace una lista que sólo tiene la
cuenta mala.** Hay que probar que con las dos delante elige la correcta, porque ése es el estado real
de la plataforma hoy — las 16 corridas del soak registran
`[Backtest/Simulator, Playback101/Playback, Sim101/Simulator, 2127534/Provider31]`.

## El número real NO va en el test

Este repositorio es público, y `2127534` es un número de cuenta fondeada real. Escribirlo en un
archivo de tests lo publica para siempre, en un proyecto que hace lo contrario en todas partes: el
certificado saltea los nombres de cuenta con una sal por instalación **precisamente** para que nadie
correlacione a un trader entre instalaciones (`CERT_CONFORMANCE.md`, A.7).

El test usa `9999999` con `Provider31`. **Lo que se prueba es la forma** — un nombre que no es
`Sim101` con un proveedor que no es `Simulator` — y esa forma es idéntica. El valor de regresión es el
mismo y no se filtra nada. Si alguien alguna vez quiere el número real en la prueba, la respuesta es
no, y la razón está acá escrita.

## Por qué no una prueba de integración

Correrlo dentro de NinjaTrader probaría más, y no serviría: necesita la plataforma, necesita la cuenta
fondeada en línea, no corre en CI, y sólo se ejecuta cuando alguien se acuerda. Un riel de seguridad
que sólo se verifica a mano es un riel verificado la primera vez y nunca más.

La prueba pura corre en cada `dotnet test`. La verificación con la plataforma sigue existiendo y ya la
hacemos: es la línea `Account.All = [...]` que el soak escribe en su reporte cada corrida, que muestra
qué había realmente delante y qué eligió. Las dos juntas cubren las dos preguntas distintas — *"¿la
regla es correcta?"* y *"¿qué vio esta corrida?"*.
