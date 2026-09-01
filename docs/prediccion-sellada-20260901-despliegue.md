# Predicción sellada — el arranque de NT8 después del despliegue de esta noche

**Escrita 2026-09-01 ~23:10Z, ANTES de que Roberto cierre NT8 y antes del despliegue.**
Sellada en su propio commit. **El arranque que describe no ocurrió y nadie lo leyó.**

Estado de partida, medido: producción **`DISARMED`**, **sin sello**, `dayKey` **`2026-09-01`** ya
cerrado con `DAY_CLOSED`, ledger `Verify() = OK` en seq 8100, DLL desplegado `12fff6f6c76d838c`.

---

## 1 · Qué eventos escribe el arranque

**Exactamente dos, en este orden, y nada más:**

```
GUARDIAN_STARTED   payload {"state":"DISARMED"}
STATE_RESTORED     payload {"state":"DISARMED","dayKey":"2026-09-01","sealHash":""}
```

**Y estos NO se escriben** — cada uno con el motivo por el que no puede ocurrir:

| evento | por qué NO |
|---|---|
| `fresh: true` dentro de `GUARDIAN_STARTED` | esa forma es la del arranque **sin archivo de estado**; el archivo existe |
| `SEAL_VERIFIED` / `SEAL_EXPIRED` / `SEAL_MISMATCH` | el bloque del sello está guardado por `if (_state.Seal != null)`, y **no hay sello** |
| `DAY_CLOSED` / `DAY_OPENED` | `RollDayIfNeeded` se llama **después** del `return` de `Tick` para `Disarmed` |
| `PNL_BASELINE_ADOPTED` / `PNL_BASELINE_REFUSED` | `_baselinePending` sólo se enciende si el estado restaurado **no** es `Disarmed` |
| `PNL_CHECKPOINT` | el checkpoint vive en el camino armado |
| `CLOCK_ANOMALY` / `CLOCK_SUSPECT` | sin reinicio de máquina entre ahora y el arranque, el delta de pared y el monótono coinciden |
| `STATE_CORRUPT` | `state.json` parsea hoy, medido con `PersistedState.TryParse` |

## 2 · Qué `dayKey` abre — y la predicción es que NO abre ninguno

> **`dayKey` se queda en `2026-09-01`**, aunque el día de trading ya rodó (el corte de sesión son las
> 17:00 America/Chicago = 22:00Z, y ya pasaron).

**El motivo es una puerta de orden**: `Tick()` hace `if (_state.Kind == StateKind.Disarmed) { Persist();
return; }` **antes** de `RollDayIfNeeded()`. Desarmado, **el día no rueda**. Rodará recién cuando
alguien arme, y ahí sí saldrán `DAY_CLOSED` de `2026-09-01` y `DAY_OPENED` de `2026-09-02`.

**Confirmación empírica ya disponible**: desde el arranque de las 22:58 hasta ahora (23:10) hay
**cero entradas nuevas** en el ledger, con el guardián ticando cada segundo. Desarmado no escribe nada.

## 3 · Rama de baseline: **ninguna**

`_baselinePending` se enciende sólo si el estado restaurado no es `Disarmed`, tiene sello y su `dayKey`
es el de hoy. **Falla las tres.** `HasObservedFill` **no entra en juego**: pertenece a la puerta de
M22, que vive dentro del camino armado.

## 4 · Qué estado queda en `state.json`

| campo | predicción |
|---|---|
| `state` | **`DISARMED`** |
| `dayKey` | **`2026-09-01`** |
| `seal` | **ausente** |
| `lockoutVerified` | `false` |
| `flattenAttempts` | `0` |
| **`runId`** | **`1e1b67cafa30449f8f4aa20d4ac45c4b`, SIN CAMBIO** |

**El `runId` es la predicción menos intuitiva y por eso la más útil.** El addon genera
`Guid.NewGuid()` en cada arranque, **pero ese id sólo se usa al crear un estado nuevo**; al restaurar,
`Persist()` reescribe el `runId` que venía del archivo. Así que **el id de la primera instalación
sobrevive a todos los arranques** — y ya lo hizo hoy, idéntico antes y después del corte.

## 5 · El binario nuevo — y la predicción es que **el ledger no lo va a mostrar**

El pedido era predecir *"qué cambia en `GUARDIAN_STARTED` por el `buildHash` nuevo"*. **La premisa no se
sostiene, y decirlo es la predicción:**

> **`GUARDIAN_STARTED` no lleva `buildHash`. El campo no existe en este build.** Sus dos formas
> completas son `{"state":…,"fresh":true}` y `{"state":…}`.

Poner el hash del binario ahí es el **ítem 5 de la cola**, bloqueado por el contrato de extensión con
Ventana B, **y no está implementado**.

⇒ **Predicción: después de desplegar un binario distinto, las dos líneas del arranque serán
BYTE A BYTE INDISTINGUIBLES de las de un arranque con el binario viejo.** El único artefacto que
llevaría el hash nuevo es un **certificado** (`issuer.buildHash`), y sólo si alguien emite uno.

**Y eso es una propiedad del producto que conviene ver escrita**: hoy **el registro no puede decirte
qué código lo escribió.**

## 6 · Qué falsaría esta predicción

Cualquiera de estas la rompe, y cada una señala algo distinto:

1. **aparece un tercer evento** en el arranque ⇒ leí mal el orden de `Start`;
2. **aparece `DAY_OPENED`** ⇒ la puerta de `Disarmed` en `Tick` no está donde creo;
3. **el `runId` cambia** ⇒ `Persist()` escribe el id del proceso y no el restaurado;
4. **aparece `fresh: true`** ⇒ el arranque no encontró `state.json`, que sería un hallazgo por sí solo;
5. **`GUARDIAN_STARTED` trae un campo nuevo** ⇒ el despliegue incluyó algo que no está en el repo.
