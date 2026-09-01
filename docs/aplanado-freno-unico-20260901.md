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

**Nada en prosa.** El payload es `{accounts, attempts, exhausted}` — **un contador, no una causa.**
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
