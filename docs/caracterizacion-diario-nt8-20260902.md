# Diario de NT8, extraído y caracterizado — 2026-09-02

**Lectura de una sola vez, no una tubería.** Extractor y caracterizador en `tools/nt8-data/`.
Los CSV son derivados y **no se versionan**: se regeneran corriendo el extractor.

**Ninguna conclusión, ninguna propuesta de qué estudiar.** Contar y cubrir rangos, nada más.

| | |
|---|---|
| **CSV** (fuera de los repos) | `…\03cf4965-af02-4a1f-8eb0-bc27e9d414df\scratchpad\nt8-daily-csv\` |
| formato | un archivo por contrato: `date,open,high,low,close,volume` |
| **252 archivos, 1,1 MB, 14.815 barras** | |
| reproducir | `python tools/nt8-data/extract_daily_ncd.py <dir>` → `characterize_daily.py <dir>` |

---

## 1 · LOS CONTROLES, ANTES DE CUALQUIER NÚMERO — **5 de 5 en cero**

**Encajar no es decodificar**: un offset corrido o un endianness al revés también encaja en
28 + 48·n. Por eso cada control puede fallar y ninguno se ajustó para pasar.

| control | qué atrapa | violaciones |
|---|---|---|
| **1 · rejilla de tick** — cada `open/high/low/close` múltiplo exacto del tick, **tomado de `MasterInstruments`** | offset o endianness | **0** / 59.260 precios |
| **1b · tick de la cabecera vs el del catálogo** | ídem, por otra vía | **0** / 306 archivos |
| **2 · `low ≤ open, close ≤ high`** | orden de campos equivocado | **0** / 14.815 barras |
| **3 · año derivado == año del nombre de archivo** | época mal convertida | **0** / 14.815 |
| **4 · volumen ≥ 0 y tiempos crecientes** | corrimiento de registro | **0** / 14.815 |

> ### Y hubo una trampa real en el control, medida y esquivada
>
> **`MasterInstruments.Name` NO es único.** `ES` aparece **tres veces**: el futuro (tick **0,25**),
> una acción (0,01) y un índice (0,01). **Tomar la primera fila habría dado 0,01 — y el control 1
> habría pasado VACÍO, porque todo precio es múltiplo de 0,01.** El extractor filtra por
> `InstrumentType = 0` y **aborta si no hay exactamente una fila** por raíz.
>
> Un control que no puede fallar no es un control. Éste podía, por ese camino, y se cerró.

**Confirmación colateral**: la cabecera de `ES 12-16` traía `0.25` como `double`, y el catálogo
—artefacto independiente— dice `0.25` para el ES futuro. **Dos fuentes que no se conocen, de acuerdo.**

---

## 2 · Por raíz

| raíz | contratos | barras | primera | última | días distintos |
|---|---|---|---|---|---|
| `ES` | 41 | 2.690 | 2016-08-23 | 2026-08-21 | 2.579 |
| `NQ` | 41 | 2.689 | 2016-08-23 | 2026-08-21 | 2.582 |
| `MGC` | 51 | 2.722 | 2016-08-23 | 2026-08-21 | 2.571 |
| `GC` | 51 | 2.721 | 2016-08-23 | 2026-08-21 | 2.571 |
| `MNQ` | 30 | 1.983 | 2019-05-06 | **2026-09-03** | 1.897 |
| `MES` | 30 | 1.970 | 2019-05-06 | 2026-08-21 | 1.880 |
| `CL` / `MCL` | 2 | 9 | 2026-08-13 | 2026-08-21 | 7 |
| `MBT` | 2 | 8 | 2026-08-13 | 2026-08-21 | 7 |
| `YM` / `MYM` | 1 | 7 | 2026-08-13 | 2026-08-21 | 7 |

> **La serie está DETENIDA en 2026-08-21 para 10 de las 11 raíces.** Los archivos diarios se
> escribieron el 22-ago 00:03 y no volvieron a tocarse. La única excepción es `MNQ 09-26`, que se
> actualizó **hoy** a las 23:13:38Z porque NT8 se abrió para el F5. **Lo diario no se mantiene solo:
> se actualiza cuando alguien lo pide.**

---

## 3 · ¿Empalman los tramos por contrato? — **SÍ. Cero huecos, y ya no es «compatible»**

Quedaba como *«compatible con embaldosar ≈10 años, NO VERIFICADO»*. **Ahora está medido:**

| raíz | uniones | limpias | **huecos** | solapamientos |
|---|---|---|---|---|
| `GC` / `MGC` | 50 | 0 | **0** | 50 |
| `ES` / `NQ` | 40 | 0 | **0** | 40 |
| `MES` / `MNQ` | 29 | 0 | **0** | 29 |
| `CL` / `MCL` / `MBT` | 1 | 0 | **0** | 1 |

**Ningún hueco en ninguna raíz. Todas las uniones solapan**, típicamente **4 días calendario** en los
trimestrales y 1–2 en los mensuales. La cobertura es continua **por construcción del solapamiento**,
no por casualidad: cada contrato empieza antes de que termine el anterior.

> ### ⚠ CORRECCIÓN 2026-09-02 — «SOLAPAMIENTO» SIGNIFICA DOS COSAS DISTINTAS, Y ARRIBA NO SE DICE CUÁL
>
> El número de arriba (**«4 días calendario»**) es `último_del_viejo − primero_del_nuevo + 1` **en
> días de calendario**. La medición del mismo día en `tools/nt8-data/audit_splice.py` cuenta otra
> cosa: **fechas en las que los dos contratos tienen barra**. Para `ES`, sobre 40 uniones:
>
> | fechas compartidas | uniones |
> |---|---|
> | 1 | 1 |
> | **2** | **23** |
> | 4 | 16 |
>
> **Las dos cuentas son correctas y miden magnitudes distintas.** Pero *«4 días calendario»* se lee
> como *«4 días de datos solapados»*, que es la cantidad que un lector necesita — y **no es la que
> estaba impresa**. La mediana real de datos compartidos en `ES` es **2**.
>
> **La misma palabra, dos magnitudes, y el documento no decía cuál.**

## 4 · Días hábiles faltantes dentro de la cobertura

Calendario Mon–Fri pelado. **Los feriados de mercado cuentan como faltantes acá y eso es esperado.**

| raíz | tramo | hábiles | presentes | faltan | % |
|---|---|---|---|---|---|
| `ES` | 2016-08-23 → 2026-08-21 | 2.609 | 2.579 | **30** | 1,1 % |
| `NQ` | ídem | 2.609 | 2.582 | **27** | 1,0 % |
| `GC` / `MGC` | ídem | 2.609 | 2.571 | **38** | 1,5 % |
| `MES` | 2019-05-06 → 2026-08-21 | 1.905 | 1.880 | **25** | 1,3 % |
| `MNQ` | 2019-05-06 → 2026-09-03 | 1.914 | 1.897 | **17** | 0,9 % |
| los cinco de 7 días | 2026-08-13 → 08-21 | 7 | 7 | **0** | 0 % |

**Compatible** con cierres completos de mercado (Navidad, Año Nuevo, Viernes Santo, Acción de
Gracias ≈ 3–4 por año × 10 años). **NO VERIFICADO**: no se contrastó contra un calendario de feriados
de CME, y hasta que se haga, «faltante» significa *no hay barra*, no *hubo un hueco de datos*.

**Y una comprobación que podía fallar y no falló: 0 barras caen en sábado o domingo** (14.815 de
14.815 en día hábil).

---

## 5 · Qué sesión representa la barra — **DETERMINADO para hoy, NO DETERMINABLE para la historia**

**Lo que se midió, y es una sola barra la que lo decide:**

| evidencia | dato |
|---|---|
| hora dentro del registro | **`00:00:00` en las 14.815** ⇒ el sello es una FECHA, no dice sesión por sí solo |
| **una barra fechada MAÑANA** | `MNQ 09-26` **2026-09-03**, escrita el **2026-09-02 a las 18:13 CDT** |
| su contenido | `O 29175,75 · H 29208,75 · L 29149,00 · C 29176,75` · **volumen 33.216** contra **2,1–2,5 M** de un día completo |

**18:13 CT es DESPUÉS del corte de sesión de CME (17:00 CT).** Una barra con volumen real y rango
estrecho, fechada el día siguiente y escrita 73 minutos después del corte, **sólo puede ser la sesión
electrónica en curso acumulándose bajo su fecha de negociación**.

> ⇒ **La barra diaria está indexada por FECHA DE NEGOCIACIÓN, y la sesión que acumula ARRANCA a las
> 17:00 CT del día calendario anterior.** Es la sesión electrónica completa, no la rueda regular. Una
> barra de sólo-RTH **no podría existir la tarde anterior**.

**Concuerda con la segunda fuente**: `MasterInstruments.TradingHours` dice **ETH** para las once
raíces (`CME US Index Futures ETH`, `Nymex Metals - Energy ETH`, `CME FX Futures ETH`).

> ### ⚠ El límite, y hay que decirlo porque es fácil extenderlo de más
>
> **Lo anterior está medido sobre UNA barra escrita HOY, y sobre el ajuste ACTUAL del catálogo.**
> `TradingHours` es una propiedad **editable del instrumento hoy**, no un registro de con qué
> plantilla se bajaron las barras de 2016–2025. **Que las 14.813 barras históricas sean también ETH
> es lo esperable y NO ESTÁ VERIFICADO.**
>
> Se verificaría comparando un puñado de barras diarias contra la sesión reconstruida desde los
> minutales — **y ese camino está cerrado hoy**: los `.ncd` de minuto **no** usan el formato fijo del
> diario (124 de 125 fallan la comprobación de tamaño). La otra vía es la exportación de la
> plataforma, que sigue sin correrse.

---

## 6 · Lo que quedó NO DETERMINABLE

1. **Si las barras históricas (2016–2025) son ETH**, por lo de arriba.
2. **Si los «faltantes» son feriados**: falta el calendario de CME.
3. **Cuánta historia diaria serviría el servidor si se pidiera más** — lo medido es lo que se pidió.
4. **Qué hay en los minutales**: formato distinto, sin decodificar y sin intención de hacerlo.
5. **Los dos campos de la cabecera que no se usan** — `int32` (=1) en el offset 0 y un `double` en el
   offset 12 (que en `ES 12-16` vale 2178,0, el mismo `open` de la primera barra). **Se leen y no se
   interpretan.**
