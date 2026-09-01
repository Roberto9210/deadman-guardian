# Literales que pueden llegar a un certificado como VALOR de un campo

**Para Ventana B — ítem 6 del aviso. 2026-09-01. Enumeración completa, no muestra.**
Lado emisor: `src/GuardianCore/Certificate.cs` y `src/GuardianCore/Ledger.cs`.

---

## 0 · La respuesta corta a lo que bloquea

> ### No existe, y no puede existir, un ESTADO llamado `UNKNOWN` o `NONE` en un campo del certificado.
> **Ningún valor de ningún enum de estado alcanza un campo del documento.** El único campo con un
> conjunto enumerable de literales es `claims.failClosedEpisodes[].triggerEvent`, y su vocabulario es
> el de los **nombres de evento**, no el de los estados.

Los tres enums de estado del núcleo, completos:

| enum | miembros | ¿llega a un campo? |
|---|---|---|
| `StateKind` (`GuardianState.cs:6`) | `Disarmed` · `Armed` · `Locked` · `FailClosed` | **no** — y **no tiene** `Unknown` ni `None` |
| `ConnectionState` (`Ports.cs:78`) | `Connected` · `Disconnected` · **`Unknown`** | **no** — sólo aparece dentro de la PROSA de un `reason`, y el certificado **no lee prosa** |
| `PnlStatus` (`PnlAccounting.cs:10`) | `Ok` · `NoPriceForOpenPosition` · `SourcesDisagree` · `AccountUnknown` · `InvalidPointValue` | **no** — mismo motivo |

**Por qué se puede afirmar y no sólo suponer**: el certificado lee de cada fila del ledger
**exactamente cinco cosas** (`Certificate.cs:165-205`) — `seq`, `tsUtc`, `event`, `payload.dayKey` y
`payload.orderId`. Nada más. `reason`, `state`, `detail`, `account` y el resto del payload **no se
leen nunca**, ni siquiera para decidir.

---

## 1 · El único campo con vocabulario enumerable

| campo | qué lleva |
|---|---|
| `claims.failClosedEpisodes[].triggerEvent` | **un nombre de evento**, como VALOR (`Certificate.cs:417`) |
| `claims.failClosedEpisodes[].reasons` | **nombres de evento como CLAVES** del objeto, con enteros por valor (`:407-408`) |

Ambos salen del mismo lugar: el campo `event` de una fila del ledger.

**Regla de exclusión, y es la única**: los dos delimitadores de episodio **nunca** aparecen, porque el
código los excluye explícitamente (`Boundaries`, `Certificate.cs:157`, usado en `:220` y `:241`):
`FAIL_CLOSED_ENTERED` y `FAIL_CLOSED_CLEARED`.

---

## 2 · Los 37 nombres de evento — el conjunto cerrado que este producto puede producir

Extraídos de `Ledger.cs:11-47`, verbatim. **Los 37 completos** (los dos marcados ⛔ no pueden aparecer
en `triggerEvent` ni en `reasons`, por la exclusión de arriba; los otros 35 sí):

```
ACCOUNT_UNKNOWN                  FLATTEN_VERIFIED                 PNL_BASELINE_ADOPTED
ARMED                            FOREIGN_ACCOUNT_ORDER_OBSERVED   PNL_BASELINE_REFUSED
CLOCK_ANOMALY                    GUARDIAN_STARTED                 PNL_CHECKPOINT
CLOCK_SUSPECT                    GUARDIAN_STOPPED                 PNL_DISAGREEMENT
CONFIG_CHANGE_REJECTED           LEDGER_VERIFY_FAILED             PNL_UNCOMPUTABLE
CONFIG_LOADED                    LIMIT_BREACHED                   SEAL_CREATED
CONFIG_REJECTED                  LIMIT_BREACHED_BASELINE_ONLY     SEAL_EXPIRED
CONFIG_TAMPERED                  LOCKOUT_CLEARED                  SEAL_MISMATCH
DAY_CLOSED                       LOCKOUT_INCOMPLETE               SEAL_VERIFIED
DAY_OPENED                       NOTIFY_FAILED                    STATE_CORRUPT
DISARMED                         ORDERS_CANCELLED                 STATE_RESTORED
FAIL_CLOSED_CLEARED  ⛔           ORDER_REJECTED_LOCKED
FAIL_CLOSED_ENTERED  ⛔           FLATTEN_REQUESTED
```

**Ninguno es igual a `UNKNOWN`, `NONE`, `N/A`, `NULL`, `TBD` ni `-`.** El más cercano por subcadena
es `ACCOUNT_UNKNOWN`, que ya confirmaron que pasa limpio por comparar valor completo. Los otros dos
que contienen la palabra son `PNL_UNCOMPUTABLE` y `SEAL_MISMATCH` — tampoco colisionan por valor
completo. **Si la comparación fuera por subcadena o sin distinguir mayúsculas, `ACCOUNT_UNKNOWN`
rompería el primer certificado con una desconexión, que en esta máquina son varias por semana.**

**Exhaustividad, verificada y no supuesta**: los 37 son `public const string` en `Ledger.cs`, y
**no hay una sola llamada a `Log(` con un literal crudo** en `src/` ni en `nt/` — todas pasan por
`Ev.*`. (Un 38º literal en mayúsculas de ese archivo, `"OK"`, es el `ToString()` de
`LedgerVerifyResult`: nunca es un campo, nunca llega al ledger.)

---

## 3 · La advertencia estructural, que es lo que vuelve propiedad a la suerte

> **`triggerEvent` es un PASAMANOS, no un enum validado.**

`current.TriggerEvent = prev.GetString("event")` (`Certificate.cs:223`). El emisor **no comprueba
contra `Ev`** lo que copia: emite **lo que la fila diga**, excluyendo sólo los dos delimitadores.

Consecuencias para el test de barrido:

- Los 37 son el conjunto que **este build** puede escribir. Un ledger real puede contener nombres de
  **versiones anteriores o futuras** del producto y el certificado los copiaría igual.
- Por eso el barrido no debería probar «los 37 no colisionan» sino **«ningún valor de `triggerEvent`
  ni ninguna clave de `reasons` colisiona con el relleno decorativo, sea cual sea la fila»** — es
  decir, la propiedad sobre el campo, no sobre la lista.
- Hoy el ledger de producción contiene **24 nombres distintos** en 8.027 entradas, todos dentro de
  los 37.

---

## 4 · Los campos de cadena que NO son enumerables — el residuo honesto

Estos no tienen conjunto cerrado y **ninguno de los dos lados puede enumerarlos**. Se listan porque
una lista parcial sería peor que ninguna.

| campo | de dónde sale | riesgo de colisión |
|---|---|---|
| **`subject.alias`** | **texto libre elegido por el usuario** (`alias.txt`); el emisor sólo rehúsa si falta (`C7_no_alias_is_refused_rather_than_invented`) | **el único punto donde un literal con forma de relleno puede aparecer legítimamente.** Si alguien se pone de alias `unknown`, `none` o `n/a`, el certificado lo lleva. **Decisión de ustedes**: excluir `subject.alias` del barrido, o que el emisor rehúse alias con forma de relleno |
| **`session.timezone`** | id IANA del snapshot sellado — **o la cadena vacía `""`** (`Certificate.cs:456`) | **colisión viva HOY si `""` está en su lista.** Sale `""` cuando el snapshot no trae `sessionResetTimeZone`: es la familia de LT-2, el reinicio que restaura `ARMED` desde el sello. Es el único de los siete `?? ""` del emisor que puede dispararse hoy |
| `commitment.personalDailyLossLimit` / `firmDailyLossLimit` | texto del config sellado (`"600.00"`) | numérico en la práctica; sin validación de forma |
| `issuer.version` / `issuer.buildHash` / `issuer.keyId` | los pone el llamador. En producción: `"0.1.0"` y 16 hex | **nuestros C-tests emiten `buildHash: "test"`** — si barren nuestras fixturas, lo van a ver |
| `continuity.gaps[].dayKey` / `.reason` | del llamador | **nada los llena hoy**: `gaps` sale `[]` en los 12 certificados emitidos |
| `anchors[].type` / `.ref` / `.hash` | del llamador | **nada los llena hoy**: `anchors` sale `[]` siempre |
| `limitations[]` | 5 cadenas fijas de prosa (`Certificate.cs:135-155`) | prosa, no valores |

---

## 5 · Los conjuntos cerrados que sí podemos garantizar

| campo | valores posibles, completos |
|---|---|
| `trustLevel` | **`"L1"`** · **`"L2"`** (`:348`) — hoy **siempre `L1`**, porque `anchors` nunca se llena |
| `ledgerDialect` | **`"guardian-core-v1"`** |
| `issuer.tool` | **`"deadman-guardian"`** |
| `signature.alg` | **`"Ed25519"`** — hoy **el bloque `signature` no se emite nunca**: ningún firmante está cableado |
| `certVersion` | `1` (entero, no cadena) |
| `subject.accounts[]` | 16 hex en minúscula (SHA-256 salado, truncado) |
| `session.dayKey` | `YYYY-MM-DD`; **se rehúsa** si falta (`C7_no_daykey_is_refused_rather_than_read_off_the_clock`) |
| `session.openedUtc`, `episodes[].fromUtc`/`toUtc`, `commitment.armedAtUtc`/`sealExpiryUtc` | ISO-8601 UTC |
| `commitment.sealHash`, `certHash` | hex |

---

## 6 · Lo que esta enumeración NO cubre

1. **El HTML** (`Certificate.Render`) no se enumeró: es una vista del mismo JSON y no agrega valores
   propios (`C8_the_html_adds_no_value_that_is_not_in_the_json`).
2. **Los ledgers de los bots y del soak** son archivos aparte y no producen certificados.
3. **Nada de esto valida la lista de relleno de ustedes**: dice qué emite este lado, no qué debería
   rechazar el verificador.
