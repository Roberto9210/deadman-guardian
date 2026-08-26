# Prueba viva: el camino de breach de PRODUCCIÓN, con fills reales

**Estado: PLAN. Escrito antes de correr nada. Requiere el OK de Roberto.** 2026-08-26.

## Por qué existe

El camino de breach de la instancia de **producción nunca disparó con fills reales**. Lo del 22-ago
fue la instancia **sandbox** del soak con límite $50; el guardián de producción siguió `ARMED` todo el
tiempo. Antes de que un beta tester toque esto, ese camino tiene que haber corrido una vez en la
instancia real, de punta a punta, sobre su propio ledger.

## El feed, y qué NO prueba esto

**`Trasmisión de datos simulados` = Simulated Data Feed. Precios sintéticos, no mercado real.**
La prueba valida el **mecanismo** — que el guardián detecte, cancele, aplane y bloquee sobre fills
que ocurrieron de verdad en Sim101. **No** valida comportamiento contra un mercado real: ni latencia
de un broker real, ni slippage real, ni un libro que se mueve solo. Ningún resultado de acá puede
usarse para ablandar lo que el `SPEC §17` dice sobre eso.

## Dato leído, no calculado

| | |
|---|---|
| sello de hoy expira | **`2026-08-26T22:00:00.000Z`** = 17:00 CDT — leído de `state.json`, campo `expiresAtUtc` |
| límite personal de producción | `600.00` |
| cuenta | `Sim101` |

**Por qué hay que esperar la expiración:** `Arm` con sello vigente cae en `TryChangeConfig`, que
rechaza **todo** cambio mientras el sello viva, incluido uno más estricto (`SPEC §7.2`). No es que
bajar el límite sería hacer trampa: **el producto lo impide por diseño**. Tras la expiración,
`CheckExpiry` desarma solo y armar con otra config es una **sesión nueva**, que es el camino diseñado.

## CONSECUENCIA QUE HAY QUE ACEPTAR ANTES: esto escribe en la evidencia de producción

El addon usa **rutas fijas** (`DeadmanGuardianAddOn.cs:32-33`) e **ignora los `ledgerPath` y
`statePath` que la config declara**. No hay forma de desviar la prueba a un ledger aparte.

Por lo tanto:

- La sesión de prueba escribe en `deadman-guardian\ledger.jsonl`, el archivo que este producto vende
  como evidencia.
- Va a contener un `LIMIT_BREACHED` **real** con un límite de $40 y su lockout completo.
- Un certificado emitido para el 2026-08-26 va a reportar ese límite.

**No es falsificar nada** — es un registro cierto de lo que pasó. Pero hay que quererlo, y hay que
etiquetarlo. Si Roberto prefiere que la evidencia de producción quede limpia, **esta prueba no se
puede hacer como está** y hay que hacerla contra una instalación NT8 aparte.

*(Nota lateral: que la config declare `ledgerPath` y `statePath` y el addon los ignore es la clase de
la casa en forma de esquema — claves que afirman configurar algo que no configuran. Anotado, no
arreglado en esta tanda.)*

## La config de la sesión de prueba

Vive en `deadman-guardian\config.json` — **no hay otro lugar**: el addon lee esa ruta fija.
Se respalda antes y se restaura después.

```
personalDailyLossLimit : 40.00     (BOT A pierde hasta 50, así que cruza)
firmDailyLossLimit     : 1000.00   (sin cambios)
accounts               : ["Sim101"]
resto                  : idéntico a producción
```

**$40 y no $50 a propósito**: BOT A se detiene solo en $50 por su sandbox. Con el límite de producción
en 40, **el guardián de producción dispara primero** — que es justo lo que se quiere medir. Si el
sandbox de BOT A disparara antes, la prueba mediría otra vez lo que ya sabemos.

## Secuencia exacta

| # | paso | quién |
|---|---|---|
| 1 | Esperar `SEAL_EXPIRED` + `DAY_CLOSED` + `DISARMED` en el ledger, después de 22:00:00Z | reloj |
| 2 | Respaldar `config.json` → `config.json.produccion-20260826` | yo |
| 3 | Escribir la config de prueba (límite 40) | yo |
| 4 | Poner `botA.GO`; confirmar que `soak.GO` sigue aparcado | yo |
| 5 | Reiniciar NT8 **con gracia** (o F5) — BOT A lee su compuerta sólo en `State.Configure` | yo / Roberto |
| 6 | Armar desde la ventana | Roberto |
| 7 | BOT A arranca a los 45 s y pierde a propósito | BOT A |
| 8 | Capturar el ledger | yo |
| 9 | Quitar `botA.GO`, restaurar la config de producción, reiniciar, rearmar | yo / Roberto |

## Qué espero ver en el ledger, en este orden

```
SEAL_EXPIRED        basis=..., dayKey=2026-08-26
DAY_CLOSED          dayKey=2026-08-26
DISARMED
CONFIG_LOADED       configHash=<distinto del de producción>
ARMED               accounts=['Sim101'], personalLimit=40.00
SEAL_CREATED
DAY_OPENED
PNL_CHECKPOINT      dayLoss creciendo: 0.00 → ~10 → ~25 → ...
LIMIT_BREACHED      dayLoss >= 40.00, limit=40.00
ORDERS_CANCELLED    account=Sim101
FLATTEN_REQUESTED   account=Sim101
FLATTEN_VERIFIED    attempts=1
ORDER_REJECTED_LOCKED   × N   (BOT A sigue intentando entrar; cada intento cancelado)
```

Los `ORDER_REJECTED_LOCKED` repetidos son la parte que importa más allá del aplanado: **prueban que
el lockout es un estado permanente y no un aplanado único** (`SPEC §9.5`).

## Qué contaría como FALLO — escrito antes, para no reinterpretarlo después

1. **`LIMIT_BREACHED` no aparece** con la pérdida por encima de 40 ⇒ el camino de producción no
   dispara con fills reales. Es el fallo que la prueba busca.
2. **`LIMIT_BREACHED_BASELINE_ONLY` en vez de `LIMIT_BREACHED`** ⇒ M22 mal arreglado: no debería
   ocurrir, porque habrá fills observados en esta sesión.
3. **`FLATTEN_VERIFIED` ausente o `LOCKOUT_INCOMPLETE` como último evento** ⇒ el aplanado no cerró la
   posición. `LOCKOUT_INCOMPLETE` **transitorio seguido de `FLATTEN_VERIFIED` NO es un fallo** — es
   el reintento normal, medido el 22-ago (502 ms).
4. **Cero `ORDER_REJECTED_LOCKED`** aunque BOT A siga intentando ⇒ el lockout no es un estado.
5. **Cualquier evento sobre una cuenta que no sea `Sim101`** ⇒ paro todo.
6. **La cadena del ledger no verifica** al final ⇒ paro todo.

## Vuelta a producción

1. Borrar `botA.GO`.
2. `config.json.produccion-20260826` → `config.json`, y **verificar por hash** que coincide con el
   respaldo, no sólo que la copia no dio error.
3. Restaurar `soak.GO` (hoy `soak.GO.parked-for-livetest`).
4. Reiniciar NT8 y rearmar; confirmar `ARMED` con `personalLimit=600.00` y el `configHash` de
   producción, el mismo que aparece en los `CONFIG_LOADED` anteriores al 26-ago.

## Lo que esta prueba no puede probar

- Nada sobre mercado real (ver arriba).
- Nada sobre la mitad de la condición 1 de M22 que bloquea sin aplanar: acá habrá fills observados,
  así que el camino es el lockout ordinario. Esa mitad la prueba `M22b` y `C1` en la suite.
- Nada sobre un límite de $600 con fills reales: el número que se ejercita es 40.
