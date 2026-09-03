# Resultado — Pregunta 1: qué tan hostil es el terreno

**Ejecuta `docs/prerregistro-pregunta1-terreno.md` + Enmienda 1.** El orden del `git log` es la
garantía: `bd5012a` (pre-registro, solo) → `577bbec` (script) → `8e5f5c8` (enmienda, sola) → este
resultado. **Ninguna cifra de M1–M4 existía cuando se escribió el método ni cuando se escribió la
enmienda.**

Salida completa: `scratchpad/terreno_pregunta1.txt`. Datos: los 252 CSV de `37a0144`, hasta
**2026-08-21**.

---

## LOS CONTROLES — arriba del resultado, como se pre-registró

| control | veredicto | alcance si fallaba |
|---|---|---|
| **C1′ multiplicador** | **PASA** — `50/5`, `20/2`, `100/10` = `10.0000000000` exacto, y los seis valores del script **son** los de `MasterInstruments.PointValue` | sólo la sección de dólares |
| **C1b aritmética** | *testigo impreso, no afirma nada* | — |
| **C2 escala** | **PASA** — razón `2.000000` en las seis raíces | **todo** |
| **C3 orden** | **PASA** — real ≠ barajado en las 12 combinaciones raíz×ventana, semilla `20260902` | sólo M4 |
| **C4 degenerados** | **reportado abajo** | la distribución de la raíz afectada |

### C1b — la fila trabajada, para rehacerla a mano

```
date 2016-08-23   root ES   contract ES_09-16
open 2180.75   low 2179.25
points  = open - low         = 2180.75 - 2179.25 = 1.5
dollars = points x 50.0 x 1                      = 75.00
```

### C4 — barras degeneradas

| raíz | barras | `open==low` | % | `open==high` | % | **cualquiera** | **%** |
|---|---|---|---|---|---|---|---|
| `ES` | 2.579 | 20 | 0,78 | 17 | 0,66 | 37 | **1,43** |
| `NQ` | 2.582 | 27 | 1,05 | 16 | 0,62 | 43 | **1,67** |
| `GC` | 2.571 | 11 | 0,43 | 14 | 0,54 | 25 | **0,97** |
| `MGC` | 2.571 | 23 | 0,89 | 18 | 0,70 | 41 | **1,59** |
| `MES` | 1.880 | 18 | 0,96 | 11 | 0,59 | 29 | **1,54** |
| `MNQ` | 1.897 | 23 | 1,21 | 15 | 0,79 | 38 | **2,00** |

**Los micros están en la misma banda que los grandes.** Los valores `0,0000` y `163` de la razón
fallada venían de **dividir dos raíces**, que amplifica un cero raro; la tasa de fondo es 1–2 % en
todas.

---

## RESULTADOS EN PUNTOS

Fechas conservadas tras descartar los cambios de contrato:

| raíz | conservadas | descartadas | tramos |
|---|---|---|---|
| `ES` / `NQ` | 2.539 / 2.542 | 40 | 41 |
| `GC` / `MGC` | 2.521 | 50 | 51 |
| `MES` / `MNQ` | 1.851 / 1.868 | 29 | 30 |

### M1 · Excursión adversa desde la apertura — **exacta** para una entrada en la apertura

| raíz | p50 | p90 | p95 | p99 | máx |
|---|---|---|---|---|---|
| **largo** (`open−low`) | | | | | |
| `ES` | 17,50 | 73,00 | 95,33 | 155,03 | 355,75 |
| `NQ` | 74,25 | 320,18 | 441,91 | 707,10 | 1.632,75 |
| `GC` | 9,20 | 36,70 | 56,00 | 129,98 | 709,60 |
| `MGC` | 9,20 | 37,20 | 55,70 | 132,24 | 710,20 |
| `MES` | 23,75 | 83,75 | 108,12 | 164,38 | 355,75 |
| `MNQ` | 107,50 | 378,77 | 492,14 | 752,23 | 1.631,50 |
| **corto** (`high−open`) | | | | | |
| `ES` | 18,50 | 63,05 | 82,50 | 145,41 | 513,75 |
| `NQ` | 77,88 | 285,20 | 382,96 | 589,45 | 2.166,50 |
| `GC` | 9,60 | 38,00 | 55,50 | 114,36 | 327,10 |
| `MGC` | 9,50 | 37,50 | 55,30 | 114,88 | 333,00 |
| `MES` | 24,00 | 70,75 | 90,62 | 154,50 | 512,75 |
| `MNQ` | 108,88 | 323,57 | 420,79 | 624,23 | 2.162,00 |

### M2 · Rango diario — **la COTA** para una entrada a cualquier hora

| raíz | p50 | p90 | p95 | p99 | máx |
|---|---|---|---|---|---|
| `ES` | 46,75 | 109,55 | 136,55 | 210,10 | 648,25 |
| `NQ` | 210,38 | 501,38 | 614,75 | 945,61 | 2.651,75 |
| `GC` | 22,20 | 70,60 | 102,10 | 181,86 | 779,80 |
| `MGC` | 22,20 | 70,70 | 102,30 | 182,36 | 780,70 |
| `MES` | 58,00 | 120,50 | 147,25 | 224,50 | 646,25 |
| `MNQ` | 265,75 | 547,50 | 658,73 | 990,91 | 2.653,00 |

### M3 · Cierre a cierre, mismo contrato (movimiento absoluto)

| raíz | n | p50 | p90 | p95 | p99 | máx |
|---|---|---|---|---|---|---|
| `ES` | 2.498 | 18,75 | 68,25 | 91,75 | 141,63 | 470,75 |
| `NQ` | 2.501 | 78,75 | 317,00 | 419,50 | 640,50 | 2.045,00 |
| `GC` | 2.470 | 8,80 | 38,10 | 58,46 | 123,80 | 609,70 |
| `MGC` | 2.470 | 8,75 | 38,00 | 57,36 | 123,80 | 609,70 |
| `MES` | 1.821 | 25,25 | 77,75 | 99,50 | 155,85 | 494,25 |
| `MNQ` | 1.838 | 112,88 | 351,75 | 467,07 | 680,34 | 2.045,00 |

### M4 · Drawdown acumulado sobre `close−open`, ventanas móviles

| raíz · ventana | n | p50 | p90 | p95 | p99 | máx |
|---|---|---|---|---|---|---|
| `ES` 20 / 60 | 1.767 / 179 | 104,50 / 214,50 | 302,50 / 501,00 | 374,90 / 671,90 | 501,00 / 724,07 | 759,75 |
| `NQ` 20 / 60 | 1.770 / 182 | 471,50 / 960,00 | 1.510,53 / 2.814,00 | 1.966,51 / 3.205,00 | 2.814,00 / 3.427,00 | 3.427,00 |
| `GC` 20 / 60 | 1.553 / 250 | 54,80 / 103,80 | 168,90 / 231,06 | 266,30 / 453,60 | 453,60 / 453,60 | 923,40 |
| `MGC` 20 / 60 | 1.554 / 250 | 56,60 / 101,10 | 166,50 / 232,06 | 248,06 / 458,00 | 458,00 / 458,00 | 942,10 |
| `MES` 20 / 60 | 1.281 / 123 | 139,25 / 287,75 | 346,75 / 586,60 | 423,25 / 697,00 | 505,75 / 724,25 | 777,25 |
| `MNQ` 20 / 60 | 1.298 / 128 | 625,50 / 1.392,00 | 1.650,50 / 3.014,93 | 2.127,96 / 3.324,25 | 2.909,75 / 3.471,50 | 3.471,50 |

> **La ventana de 60 tiene n = 123–250.** Los percentiles altos de esa fila descansan sobre pocas
> observaciones y varias se repiten contra el máximo.

---

## EN DÓLARES — un contrato, multiplicador leído del catálogo (C1′ pasa)

| raíz | medida | p50 | p90 | p95 | p99 | máx |
|---|---|---|---|---|---|---|
| `ES` | M1 largo | 875 | 3.650 | 4.766 | 7.751 | 17.788 |
| | M2 rango | 2.338 | 5.478 | 6.828 | 10.505 | 32.412 |
| | M4 w20 / w60 | 5.225 / 10.725 | 15.125 / 25.050 | 18.745 / 33.595 | 25.050 / 36.203 | 37.988 |
| `NQ` | M1 largo | 1.485 | 6.404 | 8.838 | 14.142 | 32.655 |
| | M2 rango | 4.208 | 10.028 | 12.295 | 18.912 | 53.035 |
| | M4 w20 / w60 | 9.430 / 19.200 | 30.210 / 56.280 | 39.330 / 64.100 | 56.280 / 68.540 | 68.540 |
| `GC` | M1 largo | 920 | 3.670 | 5.600 | 12.998 | 70.960 |
| | M4 w20 / w60 | 5.480 / 10.380 | 16.890 / 23.106 | 26.630 / 45.360 | 45.360 / 45.360 | 92.340 |
| `MGC` | M1 largo | 92 | 372 | 557 | 1.322 | 7.102 |
| | M4 w20 / w60 | 566 / 1.011 | 1.665 / 2.321 | 2.481 / 4.580 | 4.580 / 4.580 | 9.421 |
| `MES` | M1 largo | 119 | 419 | 541 | 822 | 1.779 |
| | M2 rango | 290 | 602 | 736 | 1.122 | 3.231 |
| | M4 w20 / w60 | 696 / 1.439 | 1.734 / 2.933 | 2.116 / 3.485 | 2.529 / 3.621 | 3.886 |
| `MNQ` | M1 largo | 215 | 758 | 984 | 1.504 | 3.263 |
| | M2 rango | 532 | 1.095 | 1.317 | 1.982 | 5.306 |
| | M4 w20 / w60 | 1.251 / 2.784 | 3.301 / 6.030 | 4.256 / 6.648 | 5.820 / 6.943 | 6.943 |

---

## Lo que esto NO dice — repetido del pre-registro, sin ablandar

- **No dice si existe una ventaja. No buscó ninguna.**
- **Supone una posición mantenida de apertura a cierre**, que ninguna estrategia real hace. **Es un
  piso del terreno, no una simulación.**
- **Con barras diarias no se ve el camino intradiario.** M1 es exacta **sólo** en la apertura.
- **No incorpora comisiones ni deslizamiento.**
- **No cubre objetivo de ganancia ni mínimo de días operados de ninguna firma.**
- **Las fechas descartadas caen en las uniones, que son trimestrales.** Cualquier estructura
  trimestral en estos números hereda ese sesgo.
