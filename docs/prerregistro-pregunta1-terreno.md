# Pre-registro — Pregunta 1: qué tan hostil es el terreno

**Escrito ANTES de correr un solo cálculo.** Este documento se commitea **solo**, sin código y sin
resultados. **El orden del `git log` es la única garantía que un tercero puede leer sin confiar en
nosotros**: si el commit del método es posterior al de los números, el pre-registro no vale nada.

---

## La pregunta

**Sin buscar ninguna ventaja: qué tan hostil es el terreno de una cuenta fondeada.**
Cuánto se mueve en contra un contrato en un día, y cuánto se acumula en una racha.

## Los datos

Los **252 CSV** del commit **`37a0144`**, columnas `date,open,high,low,close,volume`.
**La serie termina el 2026-08-21.**

## La construcción, verificada en `37a0144`

**Por fecha, el contrato de máximo volumen de esa raíz.** Total en **14.115 de 14.115 fechas**.

**Toda medición se calcula SÓLO dentro del mismo contrato.** Las fechas donde el contrato
seleccionado cambia **se descartan**.

> ### LIMITACIÓN DECLARADA
> **Esas fechas descartadas NO son aleatorias: caen exactamente en las uniones, que son
> trimestrales. Cualquier resultado con estructura trimestral hereda ese sesgo.**

### Raíces

| | |
|---|---|
| **INCLUIDAS** | `ES`, `NQ`, `GC`, `MGC` desde 2016 · `MES`, `MNQ` desde 2019 |
| **EXCLUIDAS** | `CL`, `MCL`, `MBT`, `YM`, `MYM` — **7 fechas cada una**. `APA` — **cero** |

## Las mediciones — **todas en PUNTOS primero, sin tocar dólares**

| | |
|---|---|
| **M1 · Excursión adversa desde la apertura** | para un largo, `open − low`; para un corto, `high − open`. **Es EXACTA para una entrada en la apertura, no una cota.** Distribución por raíz: percentiles **50, 90, 95, 99** y el **máximo** |
| **M2 · Rango diario** | `high − low`. **Es la COTA de la excursión para una entrada en cualquier momento del día.** Misma distribución |
| **M3 · Movimiento de cierre a cierre** | dentro del mismo contrato |
| **M4 · Drawdown acumulado** | sobre la secuencia diaria de `close − open`: el drawdown máximo en ventanas móviles de **20** y de **60** días hábiles, y la distribución de esos máximos |

## Conversión a dólares — **paso separado y último**

Los multiplicadores **se declaran con su fuente**. Si no se pueden verificar contra algo que exista
en la máquina, **se marcan NO VERIFICADO y el resultado en puntos queda como el resultado
principal**.

> **Un multiplicador sin verificar no se entierra adentro de una cifra.**

## Los tres controles

**Cada uno tiene que dar lo que dice acá. Si uno no da, ESA SECCIÓN queda invalidada — no el
hallazgo.** Se imprimen **ARRIBA** del resultado, no abajo.

| | |
|---|---|
| **C1 · MULTIPLICADOR** | la razón entre la excursión **en dólares** de `ES` y la de `MES`, **en la MISMA fecha**, tiene que dar **exactamente 10**. Igual `NQ` contra `MNQ`, y `GC` contra `MGC`. **Si no da 10, los multiplicadores están mal y el resultado en dólares no vale** |
| **C2 · ESCALA** | la excursión con **2 contratos** tiene que dar **exactamente el doble** que con 1. **Si no cambia, el parámetro no está cableado y la sección no vale** |
| **C3 · ORDEN** | el drawdown de **M4** sobre la serie real tiene que dar **DISTINTO** al drawdown sobre la misma serie **barajada al azar**, con semilla **`20260902`**. **Si dan igual, o el cómputo ignora el orden o no hay agrupamiento de volatilidad, y hay que decir cuál antes de interpretar M4** |

## Dónde se va a observar

| | |
|---|---|
| salida completa | `scratchpad/terreno_pregunta1.txt` |
| resumen | `docs/resultado-pregunta1-terreno.md` |

---

## LO QUE ESTA MEDICIÓN NO DICE — declarado antes de verla

- **No dice si existe una ventaja. No busca ninguna.**
- **Supone una posición mantenida de apertura a cierre**, que ninguna estrategia real hace. **Es un
  piso del terreno, no una simulación.**
- **Con barras diarias no se ve el camino intradiario.** M1 es exacta **sólo** para una entrada en la
  apertura.
- **No incorpora comisiones ni deslizamiento.**
- **No cubre el objetivo de ganancia ni el mínimo de días operados de ninguna firma.**
