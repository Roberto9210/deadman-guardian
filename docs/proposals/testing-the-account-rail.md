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

## El riel no elige: se niega

*"Un chequeo la habría atajado" es una garantía más débil que "no estaba"* — y esa frase, llevada hasta
el final, cambia la regla. **Elegir bien entre `Sim101` y una cuenta fondeada sigue siendo un chequeo.**
Lo que corresponde a un bot que existe para perder plata a propósito es **no arrancar** mientras haya en
la sesión una cuenta que pueda mover dinero real.

### La contra que hay que resolver primero, porque la regla literal se rompe sola

"Rechazar si hay alguna cuenta que no sea `Simulator`" **rechazaría siempre**. En las 16 corridas del
soak, sin excepción:

```
Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, 2127534/Provider31]
```

**`Playback101` tiene `Provider = Playback`, que no es `Simulator`, y está presente en toda máquina con
playback configurado — o sea, siempre.** Un riel que nunca deja correr no es un riel: es un bot
apagado, y a la semana alguien lo saca "porque no anda".

### La forma correcta: lista blanca de proveedores, y lo desconocido rechaza

```
SafeProviders = { Simulator, Playback }
```

**Se niega si CUALQUIER cuenta de la sesión tiene un proveedor fuera de esa lista** — sin necesidad de
saber qué es. `Provider31` rechaza hoy; un broker nuevo el año que viene rechaza también, sin que nadie
tenga que acordarse de agregarlo a una lista negra. Es la misma forma que el resto del proyecto:
**lista blanca declarada, y lo que no está declarado falla cerrado.**

Consecuencia deliberada, dicha para que nadie la reporte como bug: **en la máquina de un trader real
con su broker configurado, los bots no arrancan nunca.** Correcto. Son instrumentos de prueba nuestros,
no software para terceros.

### La asimetría, escrita para que nadie la "unifique"

**Esta regla es SÓLO para los bots. El guardián NO la lleva, y no puede llevarla.**

| | los bots | el guardián |
|---|---|---|
| qué son | instrumentos de prueba nuestros, uno de ellos diseñado para perder | el producto |
| su mercado | ninguno | **cuentas fondeadas** |
| con una cuenta fondeada presente | **no arrancan** | **trabaja, que es exactamente su razón de existir** |

Un guardián que se negara a funcionar con una cuenta fondeada en la sesión sería absurdo: es el
producto cuyo único cliente es alguien con una cuenta fondeada. La asimetría no es una inconsistencia
que alguien deba venir a resolver — es la diferencia entre una herramienta de prueba y un producto, y
**unificarlas rompería el producto o desprotegería la prueba, según hacia qué lado se unifique.**

## Los casos, y el que pediste primero

| caso | cuentas presentes | esperado |
|---|---|---|
| **la fondeada junto a la buena** | `Sim101/Simulator`, `9999999/Provider31` | **DENY** — hay una cuenta con proveedor fuera de la lista blanca. **NO elige `Sim101`: no arranca** |
| **la sesión sana de esta máquina** | `Backtest/Simulator`, `Playback101/Playback`, `Sim101/Simulator` | **ALLOW**, elige `Sim101` — y prueba que `Playback` no bloquea |
| la cuenta fondeada sola | `9999999/Provider31` | DENY |
| proveedor desconocido, no fondeado | `Sim101/Simulator`, `Algo/Provider99` | DENY — lo no declarado rechaza, sin saber qué es |
| nombre correcto, proveedor real | `Sim101/Provider31` | DENY |
| nombre parecido | `Sim1010/Simulator`, `sim101/Simulator` | DENY — comparación ordinal y exacta |
| dos con el mismo nombre | `Sim101/Simulator` ×2 | DENY — `matches.Count != 1` |
| la buena, desconectada | `Sim101/Simulator` (desconectada) | DENY |
| lista vacía | — | DENY |

El primero es el que cambió de signo y es el que importa: **antes decía "permitir y elegir `Sim101`",
ahora dice RECHAZAR.** Elegir correctamente era el chequeo; no arrancar es "no estaba". Ése es el
estado real de esta máquina hoy, así que es el caso que decide si el riel sirve.

El segundo es su par obligatorio: sin él, la regla nueva pasaría igual estando rota de la forma que
la haría inútil — rechazando siempre.

## El número real NO va en el test

Este repositorio es público, y `2127534` es un número de cuenta fondeada real. Escribirlo en un
archivo de tests lo publica para siempre, en un proyecto que hace lo contrario en todas partes: el
certificado saltea los nombres de cuenta con una sal por instalación **precisamente** para que nadie
correlacione a un trader entre instalaciones (`CERT_CONFORMANCE.md`, A.7).

El test usa `9999999` con `Provider31`. **Lo que se prueba es la forma** — un nombre que no es
`Sim101` con un proveedor que no es `Simulator` — y esa forma es idéntica. El valor de regresión es el
mismo y no se filtra nada. Si alguien alguna vez quiere el número real en la prueba, la respuesta es
no, y la razón está acá escrita.

## El seam mueve el punto ciego, y hay que decir dónde queda

Sacar la decisión a un archivo puro deja probado lo que decide — y **convierte al adaptador de cinco
líneas en la parte no probada**. Si mapea mal (lee `DisplayName` en vez de `Name`, o saca el proveedor
de otro campo, o se olvida de la conexión), la regla pura queda impecable, sus nueve casos en verde, y
el producto falla igual. Es la lección del seam de siempre: mover el límite no borra el riesgo, lo
reubica — y hay que decir adónde.

**La verificación ya existe y es gratis.** El soak imprime, en cada corrida, exactamente la forma que
el adaptador consume:

```
Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, 2127534/Provider31]
```

**Esa línea es la prueba del adaptador.** Los campos exactos que se leen, para que un cambio de mapeo
sea visible contra una corrida real:

| campo | de dónde sale | ¿aparece en la línea del soak? |
|---|---|---|
| nombre | `Account.Name`, comparado con `StringComparison.Ordinal` | **sí** — lo que va antes de la `/` |
| proveedor | `Account.Provider.ToString()` (vía `SafeProvider`) | **sí** — lo que va después de la `/` |
| conexión | `Account.Connection == null` y `Account.ConnectionStatus != Connected` | **NO** |

**Y ahí está el hueco que este mismo análisis destapa:** de los tres campos que deciden, la línea sólo
publica dos. El estado de conexión se lee y no se imprime, así que un error de mapeo *en ese campo* no
sería visible en ninguna corrida pasada ni futura. Es medio adaptador probado, presentado como
adaptador probado.

Se arregla con un carácter por cuenta: que la línea pase a
`Sim101/Simulator/Connected`. Entonces la evidencia que ya se genera sola cubre **los tres**, y
cualquier cambio de mapeo choca contra 16 corridas de historia.

## Por qué no una prueba de integración

Correrlo dentro de NinjaTrader probaría más, y no serviría: necesita la plataforma, necesita la cuenta
fondeada en línea, no corre en CI, y sólo se ejecuta cuando alguien se acuerda. Un riel de seguridad
que sólo se verifica a mano es un riel verificado la primera vez y nunca más.

La prueba pura corre en cada `dotnet test`. La verificación con la plataforma sigue existiendo y ya la
hacemos: es la línea `Account.All = [...]` que el soak escribe en su reporte cada corrida, que muestra
qué había realmente delante y qué eligió. Las dos juntas cubren las dos preguntas distintas — *"¿la
regla es correcta?"* y *"¿qué vio esta corrida?"*.
