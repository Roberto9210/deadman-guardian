# El aplanado quedó como freno único — qué dicen los 169 `LOCKOUT_INCOMPLETE`

**2026-09-01. Medición, no razonamiento.** Fuente: `ledger.jsonl` de producción, 8.034 entradas,
2026-08-21 → 2026-09-01. Citas de símbolo, no de línea (regla 11).

**La observación que lo pide** (del operador): cuando LT-1 quitó el escritor de
`ORDER_REJECTED_LOCKED`, el aplanado dejó de ser uno de dos frenos y quedó como **el único**. Los 169
`LOCKOUT_INCOMPLETE` eran una molestia mientras había un segundo freno detrás; ahora son **el modo de
falla del producto**. **Quitar un freno redundante asciende en silencio los defectos del que queda.**

---

## 1 · En cuántos episodios caen, y cómo se reparten

**Dos episodios, los dos concentrados. No están repartidos.**

| día | `LOCKOUT_INCOMPLETE` | `FLATTEN_REQUESTED` | `FLATTEN_VERIFIED` |
|---|---|---|---|
| **2026-08-26** | **167** | 167 | **0** |
| **2026-08-31** | **2** | 3 | **1** |

Los 167 son una sola ráfaga alrededor de las **23:44Z** del 26-ago. Los 2 son de las **14:10Z** del
31-ago. **No hay un tercer día con incompletos.**

## 2 · Qué dice el evento sobre el porqué

> **CORRECCIÓN 2026-09-01, sobre esta misma sección.** Escribí *"el evento no da causa"*. **Eso es
> cierto de la forma que dispara y falso del evento**: `LOCKOUT_INCOMPLETE` tiene **DOS formas**, igual
> que `GUARDIAN_STARTED`.
>
> | forma | payload | veces en producción |
> |---|---|---|
> | el `catch` alrededor de `Flatten` | `{account, step:"flatten", error}` — **sí lleva causa** | **0** |
> | la verificación que falla | `{accounts, attempts, exhausted}` | **169** |
>
> **Y la corrección aprieta el hallazgo en vez de aflojarlo**: la forma que llevaría la causa **nunca
> disparó**, o sea que el 26-ago **`Flatten()` volvió sin lanzar 167 veces y la posición siguió
> abierta**. El bróker aceptó y no pasó nada. Ése es el hecho, y es más filoso que "no sabemos".

**La forma que disparó no lleva prosa.** El payload es `{accounts, attempts, exhausted}` — **un
contador, no una causa.**
Hay exactamente **una forma de "por qué"**, y es cuántas veces se intentó:

| forma | dónde |
|---|---|
| `attempts` 1 y 2, **`exhausted: false`** | los 2 del 31-ago |
| `attempts` creciendo hasta **99**, **`exhausted: true`** | la ráfaga del 26-ago |

⇒ **el 26-ago el guardián agotó sus intentos y se rindió**; el 31-ago no llegó a hacerlo.
**El evento no distingue entre "el bróker no confirmó", "la posición volvió a abrirse" y "algo canceló
mi orden"** — las tres se ven igual desde el ledger. Para el 26-ago la causa se conoce por otra vía
(la prueba viva: el guardián cancelaba sus propios aplanados), **no por el evento**.

## 3 · ¿Alguno quedó incompleto y NO fue seguido de un aplanado exitoso?

> ### SÍ. El episodio del 26-ago: 167 intentos, **cero** `FLATTEN_VERIFIED`, terminado en `exhausted: true`.
> ### La posición **nunca se cerró**.

Y el otro lado del dato, que es lo que decide la pregunta del operador:

> **En toda la vida del producto hay UN (1) `FLATTEN_VERIFIED`.** Uno. `seq 8002`,
> 2026-08-31T14:10:30.033Z — después de dos incompletos, dentro del mismo segundo.

**Entonces "la posición se cierra en el ciclo siguiente" no es una propiedad: es una expectativa con
n=1 a favor y un fallo total en contra.** Y cuando falló, **no se degradó: se agotó** — 167 intentos
hasta rendirse.

**El matiz que corresponde, y no achica el hallazgo:** el fallo del 26-ago tiene causa conocida y
**arreglada ese mismo día** (LT-1). Así que no es evidencia de que el código de hoy falle. Es
evidencia de que **el mecanismo del que el producto ahora depende por completo tiene una sola
observación exitosa**, y de que su modo de falla documentado es la exhaustión.

---

## 4 · ¿La decisión quedó escrita? **SÍ, y en dos lugares**

La pregunta era si quitar el escritor de `ORDER_REJECTED_LOCKED` fue **decisión** o **consecuencia**.

**Fue decisión, firmada y fechada.**

1. **El commit `a916bba`** (*"LT-1 fixed: the guardian stops cancelling its own flatten orders, and the
   trader's exits"*), textual:
   > *"ORDER_REJECTED_LOCKED is no longer written, **deliberately**: nothing is rejected, and a name
   > asserting a rejection that did not happen is the defect class this repository chases."*
2. **`AMENDMENTS.md` A11**, 2026-08-26 — *"post-lockout enforcement has two branches, and cancelling on
   observation is not one of them"*. Cierra la cláusula *"Unverified until Step 3"* de §3.3 y enmienda
   §9.5. Trae el qué, el porqué, **la capacidad que falta** (`IBrokerActions` sólo ofrece
   `CancelAllOrders(account)`: cancelar UNA orden no se puede expresar), la **condición de
   restauración**, y **lo que no vuelve nunca** (la cancelación ciega).

**Así que el reproche "nadie firmó ese cambio" no se sostiene: está firmado.**

### Lo que NO está escrito en ningún lado, y es lo que el operador vio

**Que el aplanado quedó como freno único, y que por eso los 169 incompletos cambiaron de estatus.**
A11 nombra las dos ramas y elige; **no dice que a partir de ahí todo depende de una sola.**

**Y hay algo más filoso**: el argumento de cierre de A11 es, textual,
> *"not cancelling wrongly means one order opens exposure and **the next cycle's flatten closes it,
> bounded by one cycle**."*

**Esa cota es exactamente la frase que esta medición acaba de examinar**, y su evidencia es **una
observación**. La enmienda es correcta en su decisión y **su última línea es una afirmación con n=1**
— la clase de la casa dentro del documento que la casa usa para firmar decisiones.

**Ninguna acción se toma acá**: el ascenso silencioso de los defectos del freno restante es un hecho
para la cola, y qué hacer con él es decisión del operador.

---

## 5 · Qué campos debería llevar — medidos contra lo que el adaptador PUEDE distinguir

Lo que el puerto entrega en el instante de escribir el evento, y nada más:

```
IBrokerActions.Flatten(account)            -> void        (lanza, o no)
IBrokerActions.GetPositions(account)       -> PositionSnapshot { Instrument, Quantity (con signo), AveragePrice }
IBrokerActions.GetWorkingOrders(account)   -> OrderSnapshot   { OrderId, Instrument, Action }
```

### Nivel 1 — GRATIS HOY, y el código ya lo tiene en la mano y lo tira

| campo | qué separa | de dónde sale |
|---|---|---|
| **`positionsRemaining`** vs **`ordersRemaining`** | **el hallazgo más barato del lote.** Hoy `remaining.Add(account)` **colapsa las dos condiciones en una**: *"quedó la posición"* y *"quedaron órdenes en reposo"* son fallas distintas, con arreglos distintos, y el ledger **no las distingue** | ya calculadas en la rama de verificación |
| **`instruments` + `quantities` con signo** | qué quedó abierto y de qué lado | `PositionSnapshot.Instrument`, `.Quantity` |
| **`orderIds` + `actions`** | si la orden en reposo **aumenta o reduce** exposición | `OrderSnapshot.OrderId`, `.Action` |
| **`flattenThrew` + `error`** | **unifica las dos formas en una.** Dos formas del mismo nombre es la lección de `GUARDIAN_STARTED`: dos descripciones ciertas de una sola cosa, y el lector no sabe cuál le tocó | ya existe en el `catch` |

### Nivel 2 — cuesta guardar UN snapshot entre intentos

| campo | qué separa |
|---|---|
| **`quantityUnchangedSinceLastAttempt`** | **es el que contesta tu pregunta.** Que nada se mueva entre intentos tiene la forma de *"la plataforma no está actuando"*; que baje a 0 y vuelva tiene la forma de *"la posición se reabrió"*. **No prueba una causa: separa dos formas de falla**, y hoy son indistinguibles |

### Lo que NO se puede distinguir — y por eso NO propongo campo

- **Por qué el bróker no actuó.** `Flatten` devuelve `void`. El 26-ago **volvió sin lanzar 167 veces**:
  ni siquiera el canal de excepción trajo algo. **Ningún campo puede llevar un motivo que el puerto
  nunca recibe** — inventarlo sería exactamente el defecto de la casa.
- **Si alguien canceló nuestra orden de aplanado.** `Flatten(account)` es de cuenta entera y **no
  devuelve id**, así que *"la cancelaron"* y *"nunca se colocó"* son **la misma observación**.
- **Latencia contra rechazo.** Mismo motivo.

### La consecuencia honesta, que hay que decir antes de que alguien la festeje

> **El nivel 1 NO habría diagnosticado el 26-ago.** Habría escrito *"quedan posiciones, la cantidad no
> cambió, el aplanado no lanzó"* — que es una **forma**, no una causa.

Para tener la causa, el puerto necesita que **`Flatten` devuelva algo** (un id, o un resultado). Es una
**capacidad que falta**, de la misma familia que la cancelación selectiva de A11 — y se anota igual que
allá: **acotado por una capacidad ausente, no por una decisión ausente.**
