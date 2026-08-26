# Qué debería significar `daysCovered` — y por qué cert-1 y el defecto de `ledgerRange` son uno solo

**Estado: documento de decisión. Ni una línea de código. Nada compilado.** 2026-08-26.

## Lo que encontré antes de contar nada

La sospecha era que `daysCovered` está cableado en 1 (`Certificate.cs:67`,
`tools/IssueCertificate/Program.cs:87`, `DeadmanGuardianAddOn.cs:315`). Lo está. Pero el problema no
es que esté cableado: **es que es falso**, y el certificado ya publica la prueba de que lo es.

`certificate-2026-08-24.json`, emitido por Roberto, tal cual:

```
dayKey                        "2026-08-24"
daysCovered                   1
ledgerRange                   { "fromSeq": 1, "toSeq": 6815 }
continuity                    { "daysCovered": 1, "gaps": [] }
failClosedEpisodes[0]         { "fromSeq": 29, "fromUtc": "2026-08-21T12:14:58.969Z", ... }
limitRespected                true
lockoutsTriggered             0
```

Un certificado encabezado **2026-08-24, un día cubierto**, cuyo primer episodio de fail-closed está
fechado el **21 de agosto**. `fromSeq: 1` es la primera entrada que existió, del 16-ago.

### El mecanismo, verificado

`DeadmanGuardianAddOn.cs:306` le pasa a `Certificate.Issue` **el ledger entero**
(`ledger.ReadAll().ToList()`), junto con `DayKey = state.DayKey`. Y `Certificate.Issue:247-250` hace:

```csharp
var seqs = entries.Where(e => e.GetInt("seq").HasValue).Select(...).ToList();
long fromSeq = seqs.Min(), toSeq = seqs.Max();
var claims = Recompute(entries, fromSeq, toSeq, chainVerified);
```

`Recompute` recorre **todo**. Así que `limitRespected`, `lockoutsTriggered`,
`ordersRejectedWhileLocked`, `clockAnomalies` y `failClosedEpisodes` son **totales de nueve días**
publicados bajo un encabezado de un día.

### Los dos defectos son el mismo defecto

`cert-1` (`daysCovered` cableado) y el defecto de `ledgerRange` (rango del ledger entero) **no son dos
problemas**: son el mismo visto por sus dos puntas. El certificado **no tiene alcance**. Recorre todo
lo que hay y después le pone una etiqueta de un día.

Corolario incómodo: **`ledgerRange` es el campo honesto.** Dice `fromSeq: 1` y es verdad. El que miente
es el encabezado. Un lector cuidadoso que compare los dos campos encuentra la contradicción publicada
en el propio documento — que es exactamente la forma de defecto de esta casa: **texto que afirma más
de lo que su propio código comprobó**, esta vez dentro de la pieza cuyo valor entero es que nadie
tenga que creerle a nadie.

## Las tres definiciones candidatas

| definición | qué afirma | contable HOY? |
|---|---|---|
| **A. días que abarca el ledger** | "este archivo cubre N días de historia" | **Sí** — contando `DAY_OPENED`/`DAY_CLOSED`, o fechas de `tsUtc` |
| **B. días en que el guardián estuvo ARMED** | "hubo un compromiso vigente N días" | **Sí** — `ARMED` lleva `dayKey` en su payload; `SEAL_CREATED`/`SEAL_EXPIRED` acotan |
| **C. días con ejecuciones observadas** | "el guardián vio operar N días" | **NO. Ver abajo.** |

### C no es contable, y esto cambia el alcance

**El catálogo de eventos no tiene ninguno por ejecución.** Los 37 eventos son:

```
GUARDIAN_STARTED  GUARDIAN_STOPPED  STATE_RESTORED  STATE_CORRUPT  CONFIG_LOADED  CONFIG_REJECTED
ARMED  SEAL_CREATED  SEAL_VERIFIED  SEAL_MISMATCH  CONFIG_TAMPERED  CONFIG_CHANGE_REJECTED
DAY_OPENED  DAY_CLOSED  PNL_CHECKPOINT  PNL_DISAGREEMENT  PNL_UNCOMPUTABLE  ACCOUNT_UNKNOWN
CLOCK_ANOMALY  CLOCK_SUSPECT  FAIL_CLOSED_ENTERED  FAIL_CLOSED_CLEARED  LIMIT_BREACHED
ORDERS_CANCELLED  FLATTEN_REQUESTED  FLATTEN_VERIFIED  LOCKOUT_INCOMPLETE  ORDER_REJECTED_LOCKED
SEAL_EXPIRED  LOCKOUT_CLEARED  DISARMED  LEDGER_VERIFY_FAILED  NOTIFY_FAILED
FOREIGN_ACCOUNT_ORDER_OBSERVED  PNL_BASELINE_ADOPTED  PNL_BASELINE_REFUSED
LIMIT_BREACHED_BASELINE_ONLY
```

Un fill se aplica al libro (`Guardian.OnExecution` → `_book.Apply`) y **sólo deja rastro cuando
falla** (`PNL_UNCOMPUTABLE`). Un día entero de operación exitosa no produce **ninguna** entrada que
diga "acá hubo ejecuciones".

Lo más cerca que se llega es un **proxy**: un `PNL_CHECKPOINT` con `grossRealizedPerAccount ≠ 0`
implica que hubo fills. Pero es proxy y no cuenta:
- un día de fills que netean a cero da `0.00`;
- un día con sólo posición abierta no tiene realizado;
- y `PNL_CHECKPOINT` **no lleva `dayKey`** en su payload — hay que atribuirlo caminando los
  `DAY_OPENED`, que es exactamente lo que hace `LoadSameDayCheckpointGross`.

**Conclusión: C requiere un evento nuevo.** Eso no es un ajuste de conteo, es un cambio de formato, y
el formato es la evidencia. Fuera de alcance para un arreglo de `daysCovered`.

## Cuál supone el lector, y cuál es defendible

**El lector supone C.** Un prop firm que recibe "30 días cubiertos" entiende *"este trader operó bajo
el límite 30 días"*, no *"el archivo abarca 30 días"* ni *"el software estuvo encendido 30 días"*. Es
la única lectura que responde a la pregunta que el lector tiene.

**Y C es la que no podemos contar.** Ésa es la posición incómoda y hay que decirla, no rodearla.

**B es la defendible.** *"El guardián estuvo armado con un límite sellado durante N días"* es una
afirmación acotada, contable con lo que hay, y honesta sobre lo que el producto sabe: **el producto
observa un compromiso, no el volumen de trading.** No promete que el trader operó, y no debe.

**A es la trampa.** Suena a cobertura y sólo mide la edad del archivo. Un ledger de un año con el
guardián armado dos días diría "365". Es la peor de las tres: cuenta algo real y el lector lo lee como
otra cosa. **Reemplazar el `1` por A sería peor que dejar el `1`** — porque el `1` se nota, y A parece
contado.

## La salida que hace verdadero al `1`

Hay una cuarta opción, y creo que es la correcta para hoy:

> **El certificado es un artefacto de UN día. Se acota al día que nombra, y entonces `daysCovered: 1`
> deja de ser mentira y pasa a ser verdad por construcción.**

Lo que hay que cambiar es el **alcance**, no el conteo: filtrar las entradas al tramo del `dayKey`
—que es acotable, porque `DAY_OPENED`/`DAY_CLOSED` delimitan— y pasarle a `Recompute` sólo eso.
`ledgerRange` pasaría a decir el rango real de ese día en vez de `fromSeq: 1`, y **los dos campos
dejarían de contradecirse**.

`Recompute` ya recibe `fromSeq`/`toSeq`: la maquinaria de acotar existe, nadie la usa.

Si más adelante hace falta un certificado de rango (un prop firm que pide 30 días), **`daysCovered`
se vuelve el conteo de días de sesión dentro del rango — definición B**, con el nombre diciendo qué
mide. Eso es una decisión de producto, no un arreglo, y no hace falta hoy.

## Lo que hay que decidir, y no lo decido yo

1. **¿El certificado es de un día o de un rango?** De la respuesta salen `daysCovered` y el alcance.
2. Si es de un día: ¿qué pasa con los certificados **ya emitidos** (22 y 24 de agosto), que publican
   claims de nueve días bajo encabezado de uno? No se pueden reescribir —están firmados y encadenados—
   así que la respuesta vive afuera, igual que con la prueba viva de esta noche.
3. ¿Se agrega alguna vez un evento por ejecución? Habilitaría C, y es lo que el lector supone. Es un
   cambio de formato y necesita su propia decisión.

## CONSECUENCIA INMEDIATA de la prueba viva de esta noche

Con el alcance actual —el ledger entero— el `LIMIT_BREACHED` de esta noche **no queda contenido en la
jornada 2026-08-27**:

- `lockoutsTriggered` pasa de `0` a `1` **en todo certificado futuro de esta instalación**;
- `limitRespected` pasa a `false` **en todo certificado futuro**, para cualquier día, para siempre;
- `ordersRejectedWhileLocked` sube con cada intento de BOT A.

No es motivo para no correr la prueba: es un ledger de desarrollo y la decisión ya está tomada y
escrita. **Pero conviene saberlo antes y no descubrirlo mañana**, y es un argumento fuerte para que el
acotado por día se arregle antes de que exista un usuario beta — porque en una instalación real, **un
solo día malo envenenaría todos los certificados posteriores**, que es lo contrario de lo que un
certificado por día debe hacer.
