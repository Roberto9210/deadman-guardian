# Corte de luz del 2026-09-01 con producción ARMADA — medición post mortem

**Medido antes de tocar nada, con producción `ARMED` a $600 y sello vigente al momento del corte.**
Todas las lecturas son de **código de producción** (`Ledger.Verify`, `PersistedState.TryParse`,
`Seal.SnapshotMatchesHash`) corrido desde un programa de scratch con un `IFileStore` **que sólo sabe
leer**: sus rutas de escritura lanzan excepción a propósito, para que esta medición **no pueda** tocar
la evidencia ni por accidente.

---

## 0 · La premisa era incorrecta en dos puntos, y hay que decirlo antes que nada

| lo que se me dijo | lo que dice el registro |
|---|---|
| *"NO SE ABRIÓ NT8 TODAVÍA"* | **NT8 se abrió a las 22:54:14Z**, corrió 33 s y **cerró limpio** a las 22:54:47Z. Después **se abrió otra vez a las 22:58:03Z** y **está corriendo mientras escribo esto** |
| *"medí antes de que arranque, porque arrancar pisa la evidencia"* | **el arranque ya ocurrió antes de que llegara el pedido.** La evidencia no se pisó —el ledger es append-only y todo quedó— pero **la ventana de predicción se cerró sola** |

**Consecuencia directa, y es lo único que me niego a hacer: NO escribo la predicción sellada.**
El arranque que había que predecir **ya pasó, y yo ya leí lo que hizo.** Una predicción escrita
después de leer el resultado no es una predicción: es la peor cosa que esta casa puede producir.
La ventana se puede reabrir para el **próximo** arranque, que todavía no ocurrió.

---

## 1 · Cuándo fue el corte

| | |
|---|---|
| última escritura de la sesión que murió | **`PNL_CHECKPOINT`, seq 8091, 2026-09-01T18:56:00.527Z** |
| cadencia de checkpoints | 5 min ⇒ el siguiente vencía **~19:01:00Z** |
| **ventana del corte** | **entre 18:56:00Z y ~19:01:00Z** (≈13:56 CDT) |
| primer arranque posterior | **22:54:14.110Z** ⇒ **≈3 h 58 min** sin máquina |

**Y la marca del corte en el registro es una ausencia**: el `GUARDIAN_STARTED` de las 22:54:14
**no tiene un `GUARDIAN_STOPPED` antes**. Ésa es la firma de una muerte súbita — la sesión que armó a
las 14:10 nunca escribió su parada.

## 2 · El ledger — **INTACTO**

```
Ledger.Verify()   ->  OK
  ok        : True
  brokenSeq : (none)
  lastSeq   : 8098      (al momento de medir)
  head      : 58464f7944e7aa78...
```

- **Última línea completa**: termina en `}` + CRLF. **Sin bytes NUL, sin truncamiento.**
- La condición que el inventario lista como *"plausible, sin evidencia"* —**una última línea truncada
  por corte durante la escritura**— **no ocurrió**, con un corte real y el guardián escribiendo cada
  5 minutos.

## 3 · `state.json` — **PARSEA, y el sello SOBREVIVIÓ**

Al medir yo, el archivo dice `DISARMED` y **sin sello**. Eso **no es pérdida**: es el final ordenado
que el propio arranque post-corte escribió. La prueba de que el sello **sí** sobrevivió al corte está
en el ledger, en este orden y en el mismo milisegundo:

```
8093  STATE_RESTORED   22:54:14.117Z
8094  SEAL_VERIFIED    22:54:14.117Z   <-- el sello estaba, parseó, y SnapshotMatchesHash() dio true
8095  SEAL_EXPIRED     22:54:14.159Z   <-- y recién entonces: su hora ya había pasado (22:00Z)
8096  DAY_CLOSED       22:54:14.160Z
8097  DISARMED         22:54:14.160Z
```

> **`SEAL_VERIFIED` es exactamente la comprobación del hash del snapshot**: `Start()` escribe
> `SEAL_MISMATCH` y bloquea si no coincide, y sólo escribe `SEAL_VERIFIED` si coincide.
> **El sello atravesó un corte de luz real, con producción armada, y volvió íntegro.**

**El sello no expiró por el corte: expiró por su propia hora.** Vencía a las **22:00:00Z** y la máquina
volvió a las 22:54 — casi una hora después. **Nada de esto ejercitó la expiración a través de un
reinicio con el reloj en juego**; el sello simplemente ya estaba vencido cuando alguien lo leyó.

## 4 · El estado guardado, contra lo que sabíamos

| | esperado | encontrado |
|---|---|---|
| `dayKey` | `2026-09-01` | **`2026-09-01`** ✔ |
| sello, expiración | `2026-09-01T22:00:00Z` | **verificado y luego expirado a las 22:54** ✔ |
| límite | $600 | *(el sello ya no está para leerlo; el `ARMED` de las 14:10:21 dice `personalLimit: "600.00"`)* |
| estado ahora | — | **`DISARMED`**, sin sello, ticando (`lastSeenUtc` al segundo) |

## 5 · `adapter.log`

```
22:54:14.2260486Z  no subscription: no guarded account is resolved
22:54:47.1510478Z  shutdown complete
22:58:03.2555056Z  boot; home=...
22:58:03.5086043Z  Core started; state=Disarmed
```

**No hay línea del corte** — no puede haberla: un corte no deja despedida. La última línea *anterior*
al corte es del ciclo normal. `shutdown complete` a las 22:54:47 es el cierre **limpio** de la sesión
corta posterior.

---

## 6 · Qué produjo este incidente, y qué no

**Lo que produjo, y no es poco:**

> **La condición `STATE_CORRUPT` pasó de *"plausible, sin evidencia"* a *"ocurrió el caso y el sistema
> lo aguantó"*.** Corte real, producción armada, escrituras cada 5 min, 8.091 entradas: **cadena
> íntegra, estado parseable, sello verificado al volver.**

Es la primera vez que esa fila del inventario tiene evidencia de campo, y la evidencia es **a favor**.
**No la mueve a "no puede pasar"** — un corte no cayó dentro de una escritura, que es otra cosa que
sigue sin evidencia.

**Lo que NO produjo:**

- **ninguna predicción sellada** — la ventana se cerró antes de que llegara el pedido;
- **ningún arreglo del ledger**: no hubo nada roto, y si lo hubiera habido **no se tocaba**;
- **ningún despliegue, ningún `install.ps1`, ningún armado.** El DLL desplegado sigue en
  `12fff6f6c76d838c`, el repo limpio y el sitio sin tocar.

## 7 · Lo que queda abierto

1. **Producción quedó `DISARMED` y sin sello.** Volver a armar es una decisión del operador; el día
   `2026-09-01` ya está cerrado en el registro (`DAY_CLOSED`).
2. **La ventana de predicción sigue disponible para el PRÓXIMO arranque** — ése no ocurrió todavía.
3. **Segundo corte en 48 h.** No se sacan conclusiones de dos; se anota la frecuencia.
