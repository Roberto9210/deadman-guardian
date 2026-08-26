# Prueba viva: el camino de breach de PRODUCCIÓN, con fills reales

**Estado: APROBADO por Roberto el 2026-08-26. Corre contra el ledger de PRODUCCIÓN.**

## La decisión, y por qué

Roberto decidió que la prueba corra sobre el ledger de producción. El motivo, textual, queda acá
porque es lo que hace legítima la decisión:

> Ese ledger **nunca fue un registro de trading**. Ya contiene el soak, BOT A y 66 órdenes del
> 22-ago, todo en Sim101 con precios sintéticos. Es un ledger de desarrollo. Y el evento será
> **cierto**: ese día el guardián estuvo armado en $40 y rompió el límite. No hay nada que falsificar.

**Corrección de fecha, verificada contra el historial antes de correr:** la sesión de prueba **no**
va a llevar `dayKey 2026-08-26`. El corte de sesión es 17:00 CT, así que al expirar el sello el día
rueda y armar después abre **`dayKey 2026-08-27`** — la jornada que empieza la tarde del 26. El
precedente está en el propio ledger: `SEAL_EXPIRED dayKey=2026-08-22` (seq 6336) seguido de
`ARMED dayKey=2026-08-23` (seq 6340). O sea que el día con límite $40 en la evidencia es el
**2026-08-27**, y un certificado del 26-ago no lo toca.

### PROHIBIDO: ningún evento que diga "esto fue una prueba"

**No se agrega al ledger ningún evento, campo ni marca que signifique "esto no cuenta", "tramo de
prueba" o equivalente.** Es una decisión de diseño, no una omisión.

Un formato que aprende a descartar tramos **le da vocabulario a quien quiera esconder un día malo**.
La cadena de hash existe exactamente contra eso: su valor entero es que ningún tramo pueda separarse
del resto después del hecho. Agregar la capacidad de marcar "esto no cuenta" —aunque hoy se use con
honestidad— construye la herramienta que mañana se usa para lo contrario, y la construye adentro de
la pieza que se vende como evidencia.

Así que el ledger dice, sin adornos, lo que pasó: en la sesión `2026-08-27` el límite era $40 y se rompió.
**La explicación de por qué vive AFUERA**, en este archivo, que está en git con su propia historia.
Un auditor que se pregunte por ese día encuentra la respuesta en el repositorio, no en una excepción
codificada dentro del formato.

Es el mismo principio que gobierna todo lo demás acá: **el registro afirma exactamente lo que pasó,
ni una palabra más** — y "esto no cuenta" es una palabra más.

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
- Un certificado emitido para el **2026-08-27** va a reportar ese límite (ver la corrección de fecha arriba).

**No es falsificar nada** — es un registro cierto de lo que pasó. **DECIDIDO: se acepta**, por el
motivo de arriba. El etiquetado vive en este documento, nunca dentro del ledger.

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
SEAL_EXPIRED        basis=..., dayKey=2026-08-26   (cierra la jornada del 26)
DAY_CLOSED          dayKey=2026-08-26
DISARMED
CONFIG_LOADED       configHash=<distinto del de producción>
ARMED               accounts=['Sim101'], personalLimit=40.00, dayKey=2026-08-27
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
7. **`CONFIG_TAMPERED` en cualquier punto** ⇒ alguien tocó `config.json` bajo sello. No debería poder
   pasar si se sigue el orden de arriba; si aparece, la evidencia queda con una acusación falsa y hay
   que documentarlo afuera igual que todo lo demás.

## LA TRAMPA DE LA VUELTA: restaurar la config bajo sello se registra como MANIPULACIÓN

Verificado en el código antes de correr, y corrige el paso 9 del plan original.

`Guardian.OnConfigFileObserved` (`:517-529`) compara el hash de `config.json` en disco contra el
sellado en cada tick del addon (`DeadmanGuardianAddOn.cs:229-237`, o sea a los **segundos** de que el
archivo cambie, no en el reinicio). Si difieren mientras el sello vive:

```
CONFIG_TAMPERED   sealedHash=..., onDiskHash=..., changedKeys=[personalDailyLossLimit]
EnterLockout("the configuration file was edited while sealed")
```

**Restaurar la config de producción dispararía eso.** Y el límite restaurado es **más estricto** — 600
en vez de 40 — así que el ledger acusaría de "editar la configuración para operar por encima del
límite" a una acción que hace exactamente lo contrario. Una acusación falsa, permanente, dentro de la
cadena. Justo lo que este documento existe para no producir.

**Y no hay atajo: no existe un `Disarm` deliberado.** `Ev.Disarmed` se escribe en un único sitio
(`Guardian.cs:813`), dentro de `CheckExpiry`, y es ahí donde `_state.Seal = null` (`:816`).
**La única forma de liberar un sello es que expire.** Eso es correcto por diseño — un sello que se
puede cancelar no es un sello — pero tiene una consecuencia operativa que hay que aceptar de antemano.

### Consecuencia: el guardián queda en $40 toda la jornada 2026-08-27

El sello de la sesión de prueba se crea esta tarde y expira **2026-08-27T22:00:00Z**. Hasta entonces
`config.json` **no se toca por ningún motivo**. La vuelta a producción es mañana.

Es Sim101 con precios sintéticos y sin capital real, así que el costo es nulo — pero hay que quererlo
antes, no descubrirlo después.

Por qué el cambio de ida (paso 3) **sí** es seguro: se hace después de la expiración, cuando
`_state.Seal` ya es `null`, y `OnConfigFileObserved` retorna de entrada en ese caso (`:519`).

## Vuelta a producción — MAÑANA, no esta noche

1. Borrar `botA.GO` (esto sí se puede hacer en cualquier momento: no es config).
2. **Esperar `SEAL_EXPIRED` de la jornada `2026-08-27`** (2026-08-27T22:00:00Z). Hasta que ese evento
   esté en el ledger, `config.json` no se toca.
3. `config.json.produccion-20260826` → `config.json`, y **verificar por hash** que coincide con el
   respaldo (`38a15089c889ac09`), no sólo que la copia no dio error.
4. Restaurar `soak.GO` (hoy `soak.GO.parked-for-livetest`).
5. Reiniciar NT8 y rearmar; confirmar `ARMED` con `personalLimit=600.00` y el `configHash` de
   producción, el mismo que aparece en los `CONFIG_LOADED` anteriores al 26-ago.
6. Confirmar que **no** hay `CONFIG_TAMPERED` en ningún punto del tramo.

## Lo que esta prueba no puede probar

- Nada sobre mercado real (ver arriba).
- Nada sobre la mitad de la condición 1 de M22 que bloquea sin aplanar: acá habrá fills observados,
  así que el camino es el lockout ordinario. Esa mitad la prueba `M22b` y `C1` en la suite.
- Nada sobre un límite de $600 con fills reales: el número que se ejercita es 40.
