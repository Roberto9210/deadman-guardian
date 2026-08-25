# El error espejo — cuándo el guardián actúa sin que corresponda

**Estado: diagnóstico. No se arregló nada.** Fecha: 2026-08-22.

Todo lo probado hasta hoy verifica que el guardián **actúe cuando debe**: 16 corridas sintéticas del
soak y una corrida real con fills el 22-ago (límite sandbox $50, cancelar + aplanar + bloquear en
~500 ms). El error contrario nunca se investigó: **que aplane o bloquee con una posición buena
abierta.** Es el único defecto abierto que puede hacerle perder plata real a un usuario.

Un detalle que cambia el tono de todo lo que sigue: **dos de los escenarios de esta tabla ya
ocurrieron hoy**, en esta máquina, sin que nadie los estuviera buscando.

---

## 1. El mapa: todo camino que aplana o bloquea

### Aplanar — hay exactamente UNO

| | |
|---|---|
| **disparo** | `Guardian.cs` — `snapshot.TotalDayLoss >= _config.PersonalDailyLossLimit` **y además, desde Option A, `_book.HasObservedFill`**: un aplanamiento sólo puede descansar en al menos un fill que este guardián observó en esta sesión. El breach sin fill observado escribe `LIMIT_BREACHED_BASELINE_ONLY` y **bloquea sin aplanar** (condición 1) |
| **camino** | `EnterLockout` (`:563`) → `RunLockoutSteps` (`:575`) → `_broker.CancelAllOrders` (`:585`) → `_broker.Flatten` (`:602`) |
| **condición exacta** | la pérdida del día, **sumada sobre las cuentas cuyo estado es `Ok`**, alcanza o supera el límite personal. `>=`, no `>` |

`RunLockoutSteps` se vuelve a ejecutar desde `Tick` (`:387`) y desde `Start` (`:206`) cuando el estado
es `Locked` y `LockoutVerified` es falso, pero eso **reanuda** un lockout existente: no puede crear
uno.

**Cómo se compone el número que decide** (`PnlAccounting.cs:29`):

```
DayPnl  = GrossRealized + Unrealized - Commissions
DayLoss = DayPnl < 0 ? -DayPnl : 0
```

- `GrossRealized` — **de Core**, reconstruido desde las ejecuciones que vio, en memoria.
- `Unrealized` — **de la plataforma** (`platform.Unrealized`), y sólo si Core cree tener posición abierta.
- `Commissions` — de Core, desde las ejecuciones.

Que el no realizado cuente no es un defecto: es el producto. Un drawdown abierto que toca el límite
**debe** disparar. Pero es el mecanismo por el cual una posición buena termina aplanada, así que
cualquier error en ese número es un error que aplana.

### Bloquear entradas — siete caminos

`EntriesAllowed` es falso cuando el estado es `Locked` o `FailClosed` (`Guardian.cs:24`).

| # | archivo:línea | función | condición exacta |
|---|---|---|---|
| B1 | `Guardian.cs:159` | `Start` | la cadena del ledger está rota |
| B2 | `Guardian.cs:191` | `Start` | la configuración sellada ya no parsea |
| B3 | `Guardian.cs:417` | `Tick` | cualquier problema del snapshot: `AccountUnknown`, `NoPriceForOpenPosition`, `SourcesDisagree`, `InvalidPointValue` |
| B4 | `Guardian.cs:486` | `CheckClock` | el reloj de pared retrocedió más de 120 s |
| B5 | `Guardian.cs:499` | `CheckClock` | con continuidad, el reloj de pared avanzó 120 s más que el monotónico |
| B6 | `Guardian.cs:743` | `Log` | el ledger dejó de poder escribirse |
| B7 | `Guardian.cs:429` | `Tick` | el lockout (arriba) |

### Y un octavo camino que no bloquea: cancela

| | |
|---|---|
| **`Guardian.cs:361`** | `OnOrderObserved` → `_broker.CancelAllOrders(order.Account)` cuando el estado es `Locked` |

**Cancela sobre `order.Account`, sin verificar que esa cuenta sea una de las que guarda.** Core confía
enteramente en quien lo llama. Volvemos sobre esto: es el peor de la tabla.

---

## 2 y 3. Clasificación

| # | escenario | clase | argumento en una línea | costo si ocurre |
|---|---|---|---|---|
| **M1** | `OnOrderObserved` cancela en una cuenta que el guardián no guarda | **ARREGLADO `aa2f32a`** | Core no valida `order.Account` contra `_config.Accounts`; hoy sólo lo contiene que el adaptador se suscriba a una sola cuenta | **cancela un stop de protección en una cuenta ajena — dinero real** |
| **M2** | Reinicio tras haber realizado P&L ⇒ `SourcesDisagree` ⇒ bloqueo hasta que ruede el día | **ARREGLADO (Option A)** — el baseline se readopta corroborado contra el checkpoint del mismo día; test `M2_A_restart_after_a_realised_loss_readopts_the_loss_and_returns_to_armed` pasó de afirmar el defecto a afirmar el arreglo | `_book` es memoria pura; sólo `ResetDay()` la limpia. Al reiniciar Core arranca en 0 y la plataforma sigue reportando lo realizado de la sesión | sin protección y sin poder entrar toda la tarde |
| **M3** | Reinicio con posición ABIERTA ⇒ Core no la ve ⇒ el no realizado se ignora | **ARREGLADO (Option A)** — las posiciones se adoptan del broker con su precio promedio; sin precio, se rehúsa. El test original modelaba un mundo inconsistente (feed con no-realizado sin posición en el broker) y no viró: fue reescrito con el mundo real y la corrección quedó anotada en el propio test | `HasOpenPosition` mira el libro de Core, vacío tras reiniciar ⇒ `unrealized = 0` ⇒ `DayLoss` = 0 con una posición sangrando | el guardián informa cero pérdida mientras el trader pierde: **cree estar protegido y no lo está** |
| **M4** | Suspensión/hibernación de la máquina ⇒ salto de reloj hacia adelante ⇒ bloqueo | **PLAUSIBLE** | tapa de la notebook cerrada: el reloj de pared avanza y el monotónico no lo sigue; `wallDelta - monoDelta > 120 s` | bloqueo de entradas al despertar, sin causa real |
| **M5** | Caída momentánea del feed con posición abierta ⇒ `NoPriceForOpenPosition` ⇒ bloqueo | **PLAUSIBLE** | basta un tick sin `platform.Unrealized` teniendo posición | no puede entrar justo cuando podría necesitar cubrirse |
| **M6** | Desconexión de la cuenta ⇒ `AccountUnknown` ⇒ bloqueo | **PLAUSIBLE — OCURRIÓ HOY, VARIAS VECES** | cualquier corte de conexión; visto repetidamente el 21 y 22 de agosto | bloqueo mientras dure |
| M7 | Ejecución sin `ExecutionId` entregada dos veces ⇒ doble conteo ⇒ lockout prematuro | IMPROBABLE | la deduplicación es condicional: `if (ex.ExecutionId != null && ...)`. Requiere que NT8 emita id nulo Y repita | **aplana una posición buena** |
| M8 | Ejecuciones fuera de orden ⇒ realizado mal calculado | IMPROBABLE | `Apply` es dependiente del orden (precio promedio); requiere que NT8 entregue una salida antes que su entrada | aplana o no aplana, ambos errores |
| M9 | Corrección NTP hacia atrás mayor a 120 s ⇒ bloqueo | IMPROBABLE | necesita un reloj muy desviado; el umbral de 120 s ya absorbe la deriva normal | bloqueo hasta la próxima observación coherente |
| M10 | Ledger corrupto o no escribible ⇒ bloqueo | IMPROBABLE | disco lleno o permisos; ya no hay escritor concurrente desde que los bots tienen archivo propio | bloqueo |
| M11 | Una cuenta ajena suma a `TotalDayLoss` | **IMPOSIBLE** | `Snapshot` itera únicamente `_config.Accounts` (`PnlAccounting.cs:161`) | — |
| M12 | Un `pointValue` malo se usa en silencio | **IMPOSIBLE** | `Apply` rechaza y marca la cuenta; el snapshot devuelve `InvalidPointValue` (`:110-115`) | — |
| M13 | Ejecución CON id entregada dos veces ⇒ doble conteo | **IMPOSIBLE** | deduplicada por `cuenta\|executionId` (`:117`) | — |
| M14 | El corte de las 17:00 mueve por horario de verano | **IMPOSIBLE** | el día se calcula sobre `UtcNow` con `SessionCalendar`; el verano no mueve UTC | — |

**Seis plausibles. Dos ya ocurrieron. M1, M2 y M3 arreglados el 2026-08-22.**

Escenarios nuevos que el arreglo introduce, clasificados con el mismo criterio:

| # | escenario | clase | argumento |
|---|---|---|---|
| M17 | El baseline adoptado dispara un aplanamiento por sí solo | **IMPOSIBLE** | condición 1: la puerta del breach exige `HasObservedFill`, que sólo enciende un fill real aplicado; probado por `C1_...` de punta a punta |
| M18 | Se adopta la cifra más favorable de las dos | **IMPOSIBLE** | condición 2: `min(plataforma, checkpoint)`, probado en ambas direcciones (`C2`, `C2b`) |
| M19 | Se adopta una cifra cuyo período no se pudo corroborar | **IMPOSIBLE** | condición 3: sin checkpoint del día o fuera de tolerancia ⇒ `PNL_BASELINE_REFUSED` + fail-closed (`C3`, `C3b`) |
| M20 | El feed reporta no-realizado ≠ 0 sin que el broker reporte posición alguna | IMPROBABLE, **residual** | dato de plataforma internamente inconsistente; el guardián sigue ciego a ese no-realizado (el snapshot sólo lo lee con posición conocida). Destapado al reescribir M3. Sin arreglar a propósito: exigiría tratar como señal un estado que la plataforma no debería poder producir |
| M21 | Primer arranque de esta versión sobre un ledger viejo con realizado ≠ 0 ⇒ rehúsa hasta que ruede el día | PLAUSIBLE una sola vez por instalación | los checkpoints viejos no llevan `grossRealizedPerAccount`; sin productor no se confía en el consumidor. Se cura solo desde el primer día completo |

---

## 2b. Dos preguntas del adaptador — una es nada, la otra es un defecto nuevo

### M15 — al reiniciar, el adaptador vigilaba `Sim101` dijera lo que dijera el sello. **ARREGLADO (ver abajo).**

`_guardedAccount = "Sim101"` (`DeadmanGuardianAddOn.cs:42`) es un default cableado, y **el único lugar
que lo cambia es la ruta de armado** (`:234`). Pero `SubscribeToAccount()` corre en el arranque
(`:96`), **antes de cualquier armado** — y al reiniciar con un sello ARMED restaurado **la ruta de
armado no se ejecuta**, porque no hace falta rearmar.

Resultado: un trader que guarda `MiCuenta` reinicia NinjaTrader y **el adaptador queda suscripto a
`Sim101`**, mientras Core está armado para `MiCuenta`. Las ejecuciones de `MiCuenta` no llegan nunca a
Core. `OnAccountStatusUpdate` re-suscribe, pero al mismo `_guardedAccount` equivocado.

Hoy es invisible porque en esta máquina la config **es** `Sim101`. Para cualquier otro trader está roto
desde el primer reinicio.

Qué pasa después, trazado: Core sigue leyendo el P&L de `MiCuenta` por el feed (`NtAccountFeed` puede
leer cualquier cuenta, no depende de la suscripción), así que en cuanto haya realizado ≠ 0 la
discrepancia dispara y **falla cerrado — el mecanismo de M2**. Pero antes de eso, con una posición
abierta, Core no la ve y reporta cero pérdida: **M3, exactamente**. O sea que M15 es **la forma general
de M3**: no es sólo que el libro esté vacío, es que la suscripción puede estar apuntando a otra cuenta
durante toda la sesión.

**Decisión pendiente:** `_guardedAccount` tiene que salir de la config sellada al arrancar, no de un
literal. El default cableado debería ser `null`, y sin cuenta resuelta no hay suscripción — que es
ruidoso y correcto, en vez de silencioso y equivocado.

### M16 — una config con más de una cuenta dejaba las demás sin suscripción. **ARREGLADO: se rechaza en el armado.**

`GuardianConfig` **permite** más de una cuenta: sólo rechaza lista vacía y duplicados
(`GuardianConfig.cs:79-80`). El adaptador toma `Accounts[0]` y nada más.

Lo que sí funciona, verificado: `Snapshot` recorre **todas** las configuradas y el lockout aplana
**todas** (`Guardian.cs:576-580`). El feed lee cualquier cuenta sin suscripción. Así que la cuenta 2
**no queda invisible**: en cuanto tenga realizado ≠ 0, Core tendrá 0 contra el valor de la plataforma y
**fallará cerrado**. No miente.

Los dos huecos reales, más chicos de lo que parecía:

1. **Las órdenes post-lockout de la cuenta 2 no se cancelan** — sus `OrderUpdate` nunca llegan.
2. Antes del primer realizado, una posición abierta en la cuenta 2 no cuenta (M3 otra vez).

**Decisión pendiente, y las dos son defendibles:** o el adaptador se suscribe a **todas** las
configuradas, o el armado **rechaza explícitamente** una config con más de una. Lo que no es defendible
es el estado actual, que acepta la config y cumple a medias. Inclinación: rechazar en el armado, porque
suscribirse a varias multiplica la superficie de un producto que hoy no tiene un solo usuario con dos
cuentas — y rechazar es reversible el día que lo tenga.

---

## 4. Pruebas

Escritas en `tests/GuardianCore.Tests/M_MirrorErrorTests.cs`. Los seis plausibles son reproducibles
con los dobles que ya existen — no hizo falta inventar nada, porque Core recibe sus cuatro puertos
inyectados y eso es exactamente lo que permite mentirle de forma controlada.

| # | prueba | qué demuestra |
|---|---|---|
| M1 | `A_lockout_cancels_orders_on_an_account_the_guardian_was_never_asked_to_guard` | Core llama `CancelAllOrders("2127534")` con sólo `Sim101` configurada |
| M2 | `A_restart_after_a_realised_loss_blocks_entries_for_the_rest_of_the_day` | reinicio ⇒ `SourcesDisagree` ⇒ `FailClosed` |
| M3 | `A_restart_with_an_open_position_reports_zero_loss_while_the_position_bleeds` | `DayLoss` 0 con la plataforma informando −800 no realizados |
| M4 | `Waking_from_sleep_looks_like_a_forward_clock_jump_and_blocks_entries` | +1 h de pared, +2 s de monotónico ⇒ `FailClosed` |
| M5 | `One_tick_without_a_price_on_an_open_position_blocks_entries` | un solo tick sin `Unrealized` ⇒ `FailClosed` |
| M6 | `A_disconnection_blocks_entries_while_it_lasts` | conexión caída ⇒ `AccountUnknown` ⇒ `FailClosed` |

### ADVERTENCIA: estas nueve pruebas están en VERDE, y eso no significa lo que parece

Pasan porque **afirman el comportamiento defectuoso tal como está hoy**. Un lector que vea "233 en
verde" y concluya que el error espejo está cubierto habría entendido exactamente lo contrario.

Es la misma forma que este repositorio persigue en todas partes — un verde que dice algo distinto de
lo que el lector supone — y acá está puesta a propósito, porque la alternativa (dejar los escenarios
sin prueba hasta que haya un arreglo) es peor: se olvidan.

**Cuando cada arreglo llegue, su prueba tiene que ponerse ROJA primero.** Ese es el momento en que
hay que reescribirla para que afirme el comportamiento corregido. Una prueba de esta lista que sigue
verde después de su arreglo es señal de que el arreglo no tocó nada.

Las dos excepciones, que sí afirman comportamiento correcto y **deben seguir verdes siempre**:
`M1b` (una cuenta ajena no puede influir en la suma que dispara) y `M13` (una ejecución con id se
cuenta una sola vez). Están ahí para que un arreglo de M1 o de M7 no se lleve puesta la contención
que ya funciona.

**Lo que NO se puede reproducir acá, y hay que decirlo:**

- **M7 y M8** dependen de si NT8 emite alguna vez `ExecutionId` nulo o entrega fills fuera de orden.
  Se puede probar que *Core hace lo incorrecto si eso pasa* — y esa prueba está escrita — pero **no
  se puede probar que pase**. Averiguarlo necesita instrumentar el adaptador durante una sesión real
  y contar: cuántas ejecuciones llegaron sin id, cuántas fuera de secuencia. Eso es medición en la
  plataforma, no un test.
- **M4** reproduce la *aritmética* del salto, no la suspensión. Si `Stopwatch` de Windows avanza o no
  durante S3 es una propiedad de la máquina; hay que medirla suspendiendo una y mirando el ledger.
- El costo de M1 en dinero real **no se puede probar sin una cuenta fondeada conectada**, y no vamos
  a conectarla. La prueba demuestra la llamada; el costo se argumenta.

---

## 5. Orden de arreglo propuesto

Criterio: primero lo que puede hacer perder plata, después lo que puede hacerle creer al trader que
está protegido cuando no lo está.

**1. M1 — validar la cuenta antes de cancelar.** Es el único camino en el que el guardián le da una
orden a un broker sobre una cuenta que no guarda. Hoy lo contiene una propiedad del adaptador, no una
regla de Core: la capa que decide confía en la que llama, que es la inversión exacta de cómo está
construido todo lo demás. Y lo que cancelaría es lo peor que se puede cancelar: un stop de protección
en una cuenta con dinero. Una línea, y es defensa en profundidad sobre el único punto que toca un
broker.

**2. M3 — la ceguera al no realizado tras un reinicio.** No pierde plata por sí solo, pero apaga la
protección **mientras la ventana muestra `ARMED`**. Es el peor caso de la segunda categoría: no falla,
miente. Requiere decidir de dónde sale la posición al arrancar, y esa decisión es la misma que la
salida de diseño que Ventana B tiene marcada como NO AHORA — registrar el último valor conocido en los
ticks normales.

**3. M2 — el bloqueo permanente tras un reinicio.** Es correcto que falle cerrado ante una discrepancia
que no puede explicar. Lo que no es correcto es que no tenga salida: hoy sólo lo cura que ruede el día.
Nota: **M2 y M3 son la misma causa** — el libro de Core es memoria pura y no se reconcilia con la
plataforma al arrancar — y probablemente se arreglen juntos.

**4. M5 y M6 — los bloqueos por feed y conexión.** Son el comportamiento diseñado y no los cambiaría.
Lo que falta es que el usuario entienda qué le pasa, y eso ya está a medio hacer: el titular
`CANNOT SEE YOUR ACCOUNT` sirve para M6 y **es falso para M2**, donde el guardián sí ve la cuenta y lo
que hace es discrepar con ella. Ese titular necesita derivarse de la causa y no del estado.

**5. M4 — el salto de reloj por suspensión.** Primero medirlo. Si `Stopwatch` no avanza durante la
suspensión, toda notebook que cierre la tapa va a despertar bloqueada, y eso deja de ser una rareza
para ser el caso común de un beta.

**M7 a M10 quedan sin arreglar a propósito** hasta que haya evidencia de que ocurren. Escribir código
defensivo contra algo que nadie observó es agregar superficie a cambio de nada — y este repositorio ya
tiene el hábito de exigir un productor antes de confiar en un consumidor.
