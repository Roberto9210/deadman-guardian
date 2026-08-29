# Plan — prueba de CONDUCTA de LT-1

**Escrito el 2026-08-29 antes de correr nada y antes de desplegar nada.** Sábado, 11:16 CT.

Hoy sabemos **qué binario está cargado**. No sabemos que **el arreglo funcione en esa máquina**. LT-1
es el único defecto cerrado que costaba dinero y atrapaba al trader en una posición, y lleva dos días
arreglado sin demostrarse.

---

## 0. Estado verificado contra el repo y contra producción

El cuadro externo se confirma en los cuatro puntos:

| afirmación | verificado | cómo |
|---|---|---|
| cambio de código más nuevo: jueves 20:08, LT-2 | **sí** | commit `7bc4587`, jue 27-ago 20:10 |
| `Certificate.cs` intacto desde el 21 | **sí** | último commit que lo toca: `6d5debe`, vie 21-ago 08:59 |
| el sello de $600 venció ayer 17:00 | **sí** | `SEAL_EXPIRED` seq 7963 → `DAY_CLOSED` 7964 → `DISARMED` 7965 |
| el viernes no se trabajó | **sí** | sin commits del 28-ago |

### Tres cosas que no estaban en el cuadro

1. **NinjaTrader está ABIERTO.** PID 39112, arriba desde el jue 27-ago 19:55 — el arranque posterior
   al despliegue de LT-1. El guardián está vivo y `DISARMED`, persistiendo `state.json` cada ciclo
   (`lastSeenUtc 2026-08-29T16:20:59Z`). **El paso 1 empieza por cerrarlo, no por compilar.**

2. **Hay una POSICIÓN ABIERTA en Sim101**, y decide el diseño de la prueba. `PNL_BASELINE_ADOPTED`
   trae `positionsAdopted: 1`, y el último `PNL_CHECKPOINT` del viernes trae
   `perAccount {"Sim101": "18430.00"}` con `grossRealized 0.00`. `perAccount` **es `DayPnl`, que
   incluye no realizado** (`Guardian.cs:1051-1056`, `PnlAccounting.cs:40`).

3. **Esa posición es, casi con certeza, la que LT-1 dejó varada el 26-ago.** En los últimos 400
   eventos del ledger: **32 `FLATTEN_REQUESTED`, 32 `LOCKOUT_INCOMPLETE`, 31 `ORDERS_CANCELLED`, y
   cero `FLATTEN_VERIFIED`.** El defecto no sólo dejó una traza: dejó el objeto.

---

## 1. ¿Hay motivo para NO hacerlo hoy?

**No. Y el sábado no es sólo inocuo: ayuda.** La lectura del feed se confirma con evidencia directa,
no por el nombre de la conexión:

- `Config.xml` declara `<Name>Simulated Data Feed</Name>`, `<Provider>Simulator</Provider>`,
  `<ConnectOnStartup>true</ConnectOnStartup>`. Es el generador sintético **interno** de NT8.
- El log del 27 lo confirma en marcha: `Conectando automáticamente Trasmision de datos simulados` →
  `Conexión primaria=Conectado`.
- **La prueba que no depende de creerle al nombre**: los `PNL_CHECKPOINT` del viernes seguían
  moviéndose a las **21:58 UTC = 16:58 CT**, casi una hora después del cierre real del MES
  (viernes 16:00 CT), y con saltos de $50-100 cada 5 minutos. **El feed no tiene calendario de
  mercado.**

Lo que el sábado sí cambia, a favor: **no hay ninguna posibilidad de confundir la prueba con
actividad real.**

**Lo que la prueba NO valida** (igual que el 26-ago, y por la misma razón): precios sintéticos ⇒ se
valida el **mecanismo**, no el comportamiento contra mercado real. Nada de acá ablanda `SPEC §17`.

---

## 2. HALLAZGO NUEVO DE HOY — LT-4, y cambia lo que la prueba debe esperar

Apareció calculando cuántas vueltas de aplanado esperar. **No por intuición: enumerando el conjunto
completo**, que es chico y por lo tanto exhaustivo.

Todos los call sites de `RunLockoutSteps`, y todas las asignaciones de `LockoutVerified`:

```
Guardian.cs:240   if (Locked && !LockoutVerified) RunLockoutSteps();     tick
Guardian.cs:635   if (!LockoutVerified) RunLockoutSteps();               tick
Guardian.cs:890   RunLockoutSteps();                                     EnterLockout

Guardian.cs:478   = false    Arm
Guardian.cs:851   = false    CheckExpiry, camino a Disarmed
Guardian.cs:886   = false    EnterLockout
Guardian.cs:965   = TRUE     RunLockoutSteps, aplanado verificado
Guardian.cs:972   = false    RunLockoutSteps, quedó algo abierto
```

**Nada la vuelve a `false` mientras el guardián SIGUE `Locked`.** Una vez que el aplanado verifica,
los dos guardas del tick quedan cerrados para siempre y `RunLockoutSteps` no se llama nunca más en
ese lockout. **La exposición abierta después no se cierra jamás.**

Y esto es exactamente la frase que sostiene el arreglo de LT-1, en su propio comentario:

> *"not cancelling wrongly — one order opens exposure during the lockout; the **next cycle's flatten**
> closes it. Loss BOUNDED by one cycle."*

**No hay next cycle's flatten. La cota está afirmada, no implementada.**

Es **más viejo que el arreglo de LT-1**: hasta el 26-ago, `OnOrderObserved` llamaba
`CancelAllOrders` en cada orden observada, y eso —mal, a ciegas, y matando el propio aplanado— era lo
único que tapaba este agujero. Sacar el exceso estuvo bien; también sacó lo único que había acá.
**El patrón de la casa otra vez: un chequeo que existe no es un chequeo que corre — y acá el chequeo
que corría era el defecto.**

Confirmado por máquina, no por lectura: `tests/GuardianCore.Tests/LT4_LockoutStopsEnforcingTests.cs`,
tres tests **verdes afirmando el defecto** (convención M4-M7; deben ponerse rojos cuando se arregle).
`LT4c` prueba que la re-entrada **antes** de verificar sí funciona: el defecto es el pestillo, no el
bucle.

**No se arregla hoy.** El arreglo es una decisión de diseño —re-verificar cada tick, o rearmar los
pasos cuando aparece una posición— y no se toma en el mismo aliento que el hallazgo.

**Consecuencia para esta prueba: BOT A está construido para medir justamente esto** (*"whether the
guardian stayed LOCKED and flattened it AGAIN the next time"*). Es predicción 7 abajo, y **no cuenta
como fallo de LT-1.**

---

### 2b. Lo que LT-4 obliga a escribir hoy — tres cosas, por Roberto

**(i) De dónde salió el error, dicho por quien lo aprobó.**

> *"Aprobé el arreglo mínimo de LT-1 sobre el argumento de que el ciclo siguiente cierra la
> exposición. **No verifiqué que existiera un ciclo siguiente.** Tercera vez esta semana que afirmo
> sin la premisa, y la más cara."* — Roberto, 29-ago

Queda acá y no en una nota al pie porque es el mismo animal que persigue todo el repositorio, esta vez
en una **aprobación** en vez de en un mensaje del producto: **una afirmación cierta apoyada en una
premisa que nadie fue a buscar.** El arreglo de LT-1 se revisó línea por línea; la frase que lo
justificaba, no.

**(ii) `T7` ES FALSA HOY, y no se corrige todavía.**

`SPEC.md:46` dice, en un repositorio público:

> *"Enforcement is continuous, not a one-shot flatten (§9.5) … the order fills and **the next cycle
> closes it** … The property — **no position can be built past the lockout** — is unchanged"*

Con LT-4, las dos mitades subrayadas son falsas: no hay ciclo siguiente, y una posición **sí** puede
construirse después del lockout y quedarse. La aclaramos hace dos días y quedó diciendo lo contrario
de lo que hace el código.

**No se toca hasta después de la corrida de hoy.** Corregir una fila de un modelo de amenazas sobre
una **lectura**, justo después de que una lectura sin verificar nos trajo hasta acá, sería repetir el
error con más confianza. **Si la predicción 8 se cumple, `T7` se corrige con evidencia y no con
inferencia**, y viaja en la misma tanda que el arreglo de LT-4. Si no se cumple, se revisa `LT4a`
antes de tocar nada.

**(iii) Por qué el arreglo de LT-1 fue correcto igual — para que nadie lo revierta por pánico.**

Esto se escribe hoy porque dentro de seis meses alguien va a leer LT-4 y va a querer devolver el
`CancelAllOrders`. **La comparación es ésta:**

| | antes de LT-1 | después de LT-1 (hoy) |
|---|---|---|
| qué hace el guardián | **CAUSA** el daño: cancela las salidas del trader y su propio aplanado | **FALLA EN PREVENIR** un daño |
| quién lo inicia | el guardián | el trader, deliberadamente, **después de que se le avisó** |
| cota | ninguna — el trader queda atrapado en una posición que se hunde | la exposición que él mismo abrió |

**Causar es peor que no prevenir.** El intercambio fue a favor y no se revierte. **Y LT-4 bloquea la
beta igual**: que el arreglo haya sido correcto no lo vuelve suficiente.

---

## 3. Configuración de la sesión, y cómo se vuelve

### Antes de armar: cerrar la posición vieja

`DayPnlObserved = (GrossRealized − AdoptedBaseline) + Unrealized − Commissions`
(`PnlAccounting.cs:40`). El baseline de la Opción A adopta **sólo el realizado**; el **no realizado
entra crudo**. Con +$18.430 flotando, `dayLoss` es 0 y **ningún límite es alcanzable**.

⇒ **Roberto cierra la posición a mano antes de armar**, con el guardián `DISARMED` (lo está, así que
no interfiere). Cerrarla realiza la ganancia; al armar, la Opción A la adopta como baseline y
`DayPnlObserved` arranca ≈ 0. **Bonus: es la primera vez que la Opción A se ejercita sobre una cifra
realizada grande y real.**

### El límite

**$40.00**, el mismo del 26-ago. Dos razones: es comparable con la traza que encontró LT-1, y respeta
un orden que importa — el sandbox de BOT A corta en $50, así que **producción a $40 rompe primero**.

### La secuencia de configuración

1. Respaldo: `config.json` → `config.json.produccion-20260829` (el actual es el de producción, $600).
2. Editar `personalDailyLossLimit` a `40.00`. **Legítimo AHORA y sólo ahora**: no hay sello vigente.
3. Armar.

### La vuelta, y su costo — decirlo antes

**Desde que se arma, `config.json` no se toca hasta las 17:00 CT.** No hay `Disarm` deliberado:
`Ev.Disarmed` se escribe en un solo sitio (`Guardian.cs:846`), dentro de `CheckExpiry`. La única
salida es la expiración del sello, hoy a las **17:00 CT / 22:00 UTC**.

⇒ **producción queda a $40 y en lockout hasta las 17:00 CT de hoy.** Es sábado, la cuenta fundeada no
está en juego y no hay nada corriendo: el costo es aceptable, pero es un costo y queda escrito.
Restaurar el $600 y rearmar es después de las 17:00 CT, o el domingo.

---

## 4. La secuencia esperada, evento por evento

### Al armar (orden verificado contra el ledger real, seq 7674-7678)

```
CONFIG_LOADED
ARMED                  dayKey=2026-08-29   personalLimit=40.00
SEAL_CREATED           expiresAtUtc=2026-08-29T22:00:00.000Z
DAY_OPENED             dayKey=2026-08-29
PNL_CHECKPOINT
PNL_BASELINE_ADOPTED   adopted=<la ganancia realizada>  positionsAdopted=0
```

### El F5 intermedio — y es lo que hace que LT-2 se verifique de verdad

**Armar en fresco NO distingue LT-2 arreglado de LT-2 roto.** El `$0.00` del 26-ago apareció porque
un F5 restauró `ARMED` desde el sello **sin correr la ruta de armado**. Si armamos y rompemos el
límite en el mismo proceso, el código viejo también habría impreso `$40.00`.

⇒ **después de armar y ANTES de correr BOT A: un F5.** Recién ahí los mensajes del lockout salen de un
proceso que restauró, que es el escenario exacto de LT-2. Esperado:

```
GUARDIAN_STOPPED       state=ARMED
GUARDIAN_STARTED       state=ARMED
STATE_RESTORED         dayKey=2026-08-29  state=ARMED
SEAL_VERIFIED
PNL_BASELINE_ADOPTED
```

Esto es la economía que pediste: **una prueba, dos verificaciones.**

### El breach

```
LIMIT_BREACHED         dayLoss >= 40.00  limit=40.00
ORDERS_CANCELLED       count=N            <- UNA sola vez, desde SweepRestingOrders en EnterLockout
FLATTEN_REQUESTED      attempts -> 1
LOCKOUT_INCOMPLETE     attempts=1         <- ESPERADO Y NORMAL. Ver §6.
FLATTEN_REQUESTED
FLATTEN_VERIFIED       attempts=2
```

### Después del lockout

- **Cero `ORDER_REJECTED_LOCKED`** — es el marcador de LT-1.
- **Ningún `ORDERS_CANCELLED` más** — el barrido vive en `EnterLockout`, no en la observación.
- **Ningún `FLATTEN_REQUESTED` más**, aunque BOT A abra exposición nueva. **Eso es LT-4, no LT-1.**

---

## 5. Criterio de éxito — explícito

**LT-1 está arreglado en esta máquina si y sólo si las tres se cumplen juntas:**

| | |
|---|---|
| **E1** | `FLATTEN_VERIFIED` **presente**, con `attempts ≤ 3` |
| **E2** | `ORDER_REJECTED_LOCKED` **ausente** en toda la sesión |
| **E3** | `ORDERS_CANCELLED` **exactamente una vez** (el 26-ago fueron 31) |

Ninguno solo alcanza. **E1 sin E2** significaría que el aplanado zafó pero el barrido ciego sigue en
la ruta de observación. **E2 sin E1** significaría que no se canceló nada porque no llegó a haber
lockout. **E3** es la firma directa de que el barrido se mudó a `EnterLockout`.

---

## 6. Qué contaría como FALLO — escrito ANTES

### El que se malinterpretaría, y por eso va primero

> **`LOCKOUT_INCOMPLETE attempts=1` NO ES UN FALLO. ES LO ESPERADO.**

`RunLockoutSteps` verifica **en el mismo tick** en que pide el aplanado (`Guardian.cs:952-975`), y una
orden de mercado real no se llena en ese instante — o la posición sigue abierta, o la propia orden de
aplanado cuenta como `orders.Count > 0`. Está documentado desde la primera corrida real
(`Messages.cs:236-239`):

> *"in a NORMAL successful lockout the transient `LOCKOUT_INCOMPLETE` appears about half a second
> before `FLATTEN_VERIFIED`"*

**Verlo no es un fallo. Verlo SIN un `FLATTEN_VERIFIED` detrás, sí.**

Segundo malentendido posible: **`LOCKOUT_INCOMPLETE` sin campo `exhausted`** es un evento distinto —
lo emiten dos sitios de excepción por paso. **La ausencia del campo no es `false`.**

### Los fallos reales

| | qué se ve | qué significa |
|---|---|---|
| **F1** | `FLATTEN_VERIFIED` nunca aparece; `attempts` pasa de 3 con `exhausted:true` | **LT-1 no está arreglado**, o hay una segunda causa. Parar. |
| **F2** | aparece **cualquier** `ORDER_REJECTED_LOCKED` | el binario cargado no es el que creemos. Parar y verificar `issuer.buildHash`. |
| **F3** | `ORDERS_CANCELLED` más de una vez | el barrido sigue en la ruta de observación. |
| **F4** | las vueltas vuelven a los cientos | igual que F1, en su forma de libro. |
| **F5** | `LIMIT_BREACHED` nunca dispara | **NO es un fallo del arreglo: es un fallo de la PRUEBA.** Se reporta como tal. **No se sube el tamaño ni se alarga el hold para forzarlo** — la misma cortesía fail-closed que tiene lo que estamos probando. |
| **F6** | `CONFIG_TAMPERED` | alguien tocó `config.json` bajo sello. Aborta todo. |

### Lo que NO es fallo de esta prueba

- Que quede exposición abierta después del `FLATTEN_VERIFIED` → **LT-4**, predicho arriba.
- Que NT8 apague una estrategia al aplanar → es NT8 reaccionando, y el mensaje 1 ya lo dice.

---

## 7. PREDICCIÓN SELLADA — con el lugar de observación de cada una

La lección del 27: **una predicción que no nombra dónde se mira no es medible, y eso es peor que no
predecir.** Ayer las predicciones 1 y 2 no se falsaron — resultaron **inmedibles**, porque la ventana
ARMED sólo muestra `"Watching Sim101. Entries allowed."`, sin límite ni cláusula.

| # | predicción | **dónde se observa** | falsable |
|---|---|---|---|
| 1 | `ORDERS_CANCELLED` aparece **exactamente una vez** | `ledger.jsonl` | sí |
| 2 | `FLATTEN_VERIFIED` aparece, con `attempts` entre 1 y 3 | `ledger.jsonl` | sí |
| 3 | **cero** `ORDER_REJECTED_LOCKED` en toda la sesión | `ledger.jsonl` | sí |
| 4 | la orden `Cerrar` llega a **`Llenado`**, no a `CANCELADO` | `log/log.20260829.*.es.txt` | sí |
| 5 | mensaje 2 dice `$40.00` como límite, **no** `$0.00` | ventana emergente de NT8 | sí |
| 6 | mensaje 2 incluye `until 17:00 (America/Chicago)` | ventana emergente de NT8 | sí |
| 7 | mensaje 2 **NO** contiene `"your record"` | ventana emergente de NT8 | sí |
| 8 | tras `FLATTEN_VERIFIED`, un probe de mercado de BOT A abre posición y **no** se aplana | `ledger.jsonl` + Positions de NT8 | sí |

**La 4 es la más fuerte**: es el inverso exacto de la traza del 26-ago, donde `Cerrar` murió en
`CANCELADO` 110 ms después del envío.

**La 8 es LT-4**, y si NO ocurre, mi lectura del pestillo está mal y hay que revisar `LT4a`.

### Condición de parada

**Si F1 o F2 ocurren, se para y no se toca una línea más hasta entender por qué.** Un arreglo
desplegado que no arregla es peor que el defecto conocido: convierte una lista de pendientes en una
lista de mentiras.

---

## 8. Los mensajes de LT-2 que se esperan, y dónde

Ambos salen por la ventana emergente de NT8 (`Announce`), tras el F5 intermedio ⇒ **por la ruta de
restauración**, que es donde LT-2 mordía.

**Mensaje 1**, al romperse el límite:

> DAILY LOSS LIMIT REACHED. The guardian is closing your day on Sim101. You are down $40.xx and your
> limit is **$40.00**. I am about to cancel your working orders and close your positions. NinjaTrader
> will switch off any strategy running on this account as a result — that is NinjaTrader reacting to
> the positions being closed, not an error, and nothing is broken.

**Mensaje 2**, colgado de `FLATTEN_VERIFIED`:

> LOCKED. N orders cancelled and positions closed on Sim101, at $40.xx against a **$40.00** limit. Any
> new order will be cancelled **until 17:00 (America/Chicago)**. This is what you asked for.

Las tres cosas que se miran, y las tres son de LT-2:

1. el límite dice **$40.00**, no `$0.00` ← el defecto que un humano leyó el 26-ago
2. la cláusula **`until 17:00 (America/Chicago)`** aparece ← desaparecía en silencio
3. **no** aparece `"The figures this message cannot state are in your record"` ← este proceso
   presenció todo, así que la supresión no debe dispararse

**Si el mensaje 2 no llega nunca** — porque `FLATTEN_VERIFIED` no llega — eso es **LT-3**, que sigue
abierto y sin arreglar: el trader lee *"I am about to close your positions"* y después nada.

---

## 9. Las vueltas del aplanado — la corrección a "UNA"

**No es una. Son una o dos, y el número no es el criterio.**

`RunLockoutSteps` verifica en el mismo tick en que pide el aplanado, y la orden de mercado tarda
~100-250 ms en llenarse (medido el 26-ago: aceptada a los 109 ms). Así que **la vuelta 1 casi siempre
escribe `LOCKOUT_INCOMPLETE`** y `FLATTEN_VERIFIED` llega en la 2.

Lo que separa arreglado de roto **no es el conteo, es la terminación**:

| | 26-ago (roto) | esperado hoy |
|---|---|---|
| vueltas | **167**, sin techo | 1-2 |
| `FLATTEN_VERIFIED` | **cero** | presente |
| `ORDERS_CANCELLED` | 31 | 1 |
| `exhausted:true` | desde la vuelta 3 | nunca |

**`MaxFlattenAttempts = 3` (`Guardian.cs:15`) no frena el bucle** — sólo marca el evento con
`exhausted:true` y sigue girando. Por eso el 26-ago llegó a 167.

---

## 10. Los pasos, en orden

| # | quién | qué |
|---|---|---|
| 1 | Roberto | **Cerrar la posición abierta de Sim101** (Control Center → Positions). Anotar el P&L realizado. |
| 2 | Roberto | Cerrar NT8 **desde la bandeja** (`^` → click derecho → Exit). Con gracia; si no cierra, se reporta, **no se mata**. |
| 3 | Claude | Compilar Release y correr `install.ps1` con sus cuatro guardas (2/4/5/6). |
| 4 | Roberto | Abrir NT8 y dar **F5**. |
| 5 | Claude | Verificar el binario por certificado: `issuer.buildHash` = el nuevo. |
| 6 | Claude | Respaldar `config.json` y bajar el límite a `40.00`. **Sin sello vigente.** |
| 7 | Roberto | **Armar.** |
| 8 | Roberto | **F5** (el que hace medible a LT-2). |
| 9 | Roberto | Poner `botA.GO` y dejar correr. |
| 10 | ambos | Observar contra §4 y §7. |
| 11 | Claude | Informe con la traza real. Certificado del día. |
| 12 | — | Después de las **17:00 CT**: restaurar `config.json` a $600, rearmar, reponer `soak.GO`. |

**Nada del 3 al 12 se ejecuta sin tu OK sobre este plan.**
