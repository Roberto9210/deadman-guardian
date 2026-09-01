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

### ⚠ CORRECCIÓN 2026-09-01 (tarde): junté dos hallazgos de fuerza MUY distinta

La versión anterior de esta sección decía que la condición `STATE_CORRUPT` pasó a *"ocurrió el caso y
el sistema lo aguantó"*. **Eso está mal, y el motivo es aritmético.**

**El sello sobrevivió: evidencia real y fuerte.** Estaba escrito en disco, la máquina murió de golpe, y
después `SEAL_VERIFIED` comprobó su hash y dio bien. **Eso prueba durabilidad de un archivo YA ESCRITO
frente a un corte duro**, y es la primera vez que lo tenemos.

**El ledger no se partió: casi no vale nada.** Los `PNL_CHECKPOINT` van **cada 5 minutos** y la
escritura de una línea dura **microsegundos**. La probabilidad de que un corte caiga DENTRO de una
escritura es del orden de microsegundos sobre 300 segundos. **Sobrevivir dos cortes es exactamente lo
esperado tanto si el escritor es seguro como si no lo es** ⇒ los dos cortes **no distinguen las dos
hipótesis, y una medición que no distingue no mide.**

> **EL CASO NO OCURRIÓ.** Ocurrió un corte, que es otra cosa. **El corte pasó dos veces y ninguna de
> las dos cayó dentro de una escritura** — lo esperado por frecuencia, y no dice nada sobre el escritor.

## 6b · La medición que SÍ discrimina: leerle el código al escritor

`NtFileStore.AppendLine`, que es por donde pasa cada línea del ledger:

```csharp
using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
using (var writer = new StreamWriter(fs, Utf8NoBom))
{
    writer.Write(line);
    writer.Write(Environment.NewLine);
    writer.Flush();
    fs.Flush(true);            // SPEC §6: a disco, no sólo a la caché del SO
}
```

**Abre, escribe, fuerza a disco y cierra — por entrada.** Y la respuesta se parte en dos propiedades
que no son la misma:

| propiedad | veredicto | por qué |
|---|---|---|
| **durabilidad** | **SÍ, y es deliberada** | `fs.Flush(true)` es `FlushFileBuffers`: cuando `Append` retorna, los bytes están en el plato, no en la caché. **No se apoya en el buffer del SO** |
| **atomicidad del append** | **NO GARANTIZADA por la API** | un corte *entre* el primer byte y el flush puede dejar una línea parcial. Ninguna API promete lo contrario |

**Lo que acota el riesgo, medido:** el archivo se abre y se cierra **por entrada**, así que no hay
buffer sucio de larga vida — la ventana es un `write`+`flush`. Y el tamaño típico de línea es
**2.768.397 bytes / 8.100 entradas ≈ 342 bytes**, cómodamente bajo un sector o bloque, así que en la
práctica el `StreamWriter` (buffer de 1 KB) lo entrega en **una** escritura.

> **Conclusión honesta: el escritor es estructuralmente sólido en durabilidad y estructuralmente NO
> GARANTIZADO —aunque de ventana muy angosta— frente al desgarro.** Es un defecto conocido con su
> probabilidad acotada, que es infinitamente mejor que una fila del inventario diciendo que aguantamos.

## 6c · El control que el día regaló: muerte violenta y despedida limpia, a 33 segundos

**Explicación del cierre y la reapertura de las 22:54, que sin esto se lee como inestabilidad**:
Roberto había abierto NT8 **antes** de leer el aviso de no abrirlo, lo leyó, **lo cerró**, y lo volvió
a abrir tres minutos después. **Los 33 segundos son eso: una acción humana deliberada, por una
instrucción que llegó tarde.** No es un síntoma.

Y deja un control que casi nunca se consigue gratis: **la misma máquina, el mismo disco, el mismo
binario, adyacentes en el tiempo.**

| | terminación **VIOLENTA** (el corte) | terminación **LIMPIA** (Roberto) |
|---|---|---|
| en el **ledger** | `PNL_CHECKPOINT` 18:56:00Z y **nada más**. El siguiente `GUARDIAN_STARTED` **sin `GUARDIAN_STOPPED` antes** | **`GUARDIAN_STOPPED` 22:54:47.144Z** escrito |
| en **`adapter.log`** | ninguna línea de cierre | **`shutdown complete`** 22:54:47.151Z |
| en **`state.json`** | persistido en el último tick: `ARMED` + sello | persistido en el último tick: `DISARMED`, sin sello |
| rama del arranque siguiente | restauró `ARMED` ⇒ verificó el sello ⇒ lo halló expirado ⇒ cerró el día | restauró `DISARMED` ⇒ **nada** |

**La diferencia de `state.json` NO es la firma de la muerte**: viene de que el sello había expirado
entremedio. **Si Roberto hubiera cerrado limpio estando armado, `state.json` habría quedado idéntico a
como lo dejó el corte.**

### ¿Alguien que mira sólo el disco puede distinguirlas?

> **Desde los archivos de ESTADO, no.** `state.json` guarda lo mismo en los dos casos, y su
> `lastSeenUtc` tampoco delata: una despedida limpia también deja un último tick de hace un segundo.
>
> **Sólo un registro APPEND-ONLY las distingue** — el ledger por `GUARDIAN_STOPPED`, y `adapter.log`
> por `shutdown complete`.

**Y eso es una propiedad del producto que nadie había escrito**: *el archivo que dice "el estado" no
puede decirte si el sistema murió o se despidió; sólo el registro puede.* Es el argumento más concreto
que existe para que el ledger sea append-only, y hasta hoy estaba implícito.

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
