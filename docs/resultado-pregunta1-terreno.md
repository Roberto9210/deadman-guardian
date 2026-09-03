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

---

# AÑADIDO — 2026-09-02: control de período y la tabla invertida

## CONTROL 1 · Los 35 % eran el PERÍODO, no el instrumento — **PASA**

**Criterio declarado antes de correr**: PASA por debajo del 5 %, FALLA en 10 % o más.
Ventana común tomada de los micros: **2019-05-06 … 2026-09-03**.

| par | mediana grande | mediana micro | **brecha** | (grande en período COMPLETO) |
|---|---|---|---|---|
| `ES`/`MES` | 23,50 | 23,75 | **1,06 %** | 17,50 |
| `NQ`/`MNQ` | 107,12 | 107,50 | **0,35 %** | 74,25 |
| `GC`/`MGC` | 11,70 | 11,90 | **1,71 %** | 9,20 |

**Al igualar el período la brecha desaparece. Las seis raíces son comparables una vez alineadas.**

### Cuánto manda el régimen sobre el instrumento — dato por sí mismo

| raíz | p50 | p90 | p95 | p99 |
|---|---|---|---|---|
| `ES` | **+34,3 %** | +14,0 % | +12,1 % | +3,7 % |
| `NQ` | **+44,3 %** | +17,5 % | +10,3 % | +6,1 % |
| `GC` | +27,2 % | +24,5 % | +28,7 % | +19,8 % |
| `MGC` | +29,3 % | +21,0 % | +29,4 % | +15,3 % |

> **El régimen mueve la mediana de `ES` un 34 % y la de `NQ` un 44 %. Elegir instrumento cambia
> menos que elegir período.** Y el efecto **decae hacia la cola** en los índices (p99 +3,7 % y
> +6,1 %) pero **no en los metales**, donde se mantiene alto en todos los percentiles.

## 2 · LA TABLA AL REVÉS — traés tu número, salís con el porcentaje

**Ningún límite de ninguna firma aparece acá.** Ventana recortada, o sea las seis comparables.

### (a) DIARIO — un límite en esta cifra se rompe en X % de los DÍAS (M1, lado largo)

| raíz | ctr | 1 % | 5 % | 10 % | 25 % | 50 % |
|---|---|---|---|---|---|---|
| `ES` | 1 | 8.035 | 5.341 | 4.162 | 2.331 | 1.175 |
| | 2 | 16.070 | 10.682 | 8.325 | 4.662 | 2.350 |
| | 3 | 24.105 | 16.024 | 12.488 | 6.994 | 3.525 |
| `NQ` | 1 | 15.000 | 9.751 | 7.521 | 4.294 | 2.142 |
| | 2 | 30.000 | 19.502 | 15.042 | 8.588 | 4.285 |
| | 3 | 45.000 | 29.252 | 22.563 | 12.881 | 6.428 |
| `GC` | 1 | 15.568 | 7.210 | 4.570 | 2.410 | 1.170 |
| | 2 | 31.136 | 14.420 | 9.140 | 4.820 | 2.340 |
| | 3 | 46.704 | 21.630 | 13.710 | 7.230 | 3.510 |
| `MGC` | 1 | 1.525 | 721 | 450 | 241 | 119 |
| | 2 | 3.050 | 1.442 | 900 | 482 | 238 |
| | 3 | 4.576 | 2.163 | 1.350 | 723 | 357 |
| `MES` | 1 | **822** | 541 | 419 | 238 | 119 |
| | 2 | 1.644 | 1.081 | 838 | 475 | 238 |
| | 3 | 2.466 | 1.622 | 1.256 | 712 | 356 |
| `MNQ` | 1 | 1.504 | 984 | 758 | 429 | 215 |
| | 2 | 3.009 | 1.969 | 1.515 | 858 | 430 |
| | 3 | 4.513 | 2.953 | 2.273 | 1.287 | 645 |

### (a′) DIARIO — lado corto (`high − open`)

| raíz | ctr | 1 % | 5 % | 10 % | 25 % | 50 % |
|---|---|---|---|---|---|---|
| `ES` | 1 | 7.743 | 4.566 | 3.550 | 2.212 | 1.188 |
| `NQ` | 1 | 12.687 | 8.465 | 6.516 | 4.091 | 2.155 |
| `GC` | 1 | 12.220 | 6.810 | 4.440 | 2.530 | 1.260 |
| `MGC` | 1 | 1.220 | 688 | 446 | 252 | 126 |
| `MES` | 1 | 772 | 453 | 354 | 222 | 120 |
| `MNQ` | 1 | 1.248 | 842 | 647 | 408 | 218 |

*(2 y 3 contratos escalan lineal — la tabla completa está en la salida.)*

### (b) 20 DÍAS — un límite de drawdown acá se rompe en X % de las VENTANAS (M4)

| raíz | ctr | 1 % | 5 % | 10 % | 25 % |
|---|---|---|---|---|---|
| `ES` | 1 | 25.062 | 21.115 | 17.462 | 11.050 |
| | 2 | 50.124 | 42.230 | 34.925 | 22.100 |
| | 3 | 75.186 | 63.345 | 52.388 | 33.150 |
| `NQ` | 1 | 58.080 | 42.943 | 33.025 | 22.479 |
| | 3 | 174.240 | 128.828 | 99.075 | 67.436 |
| `GC` | 1 | 50.049 | 30.622 | 22.440 | 11.890 |
| | 3 | 150.148 | 91.867 | 67.320 | 35.670 |
| `MGC` | 1 | 4.950 | 2.994 | 2.240 | 1.216 |
| | 3 | 14.849 | 8.983 | 6.720 | 3.648 |
| `MES` | 1 | **2.529** | 2.116 | 1.734 | 1.116 |
| | 2 | 5.058 | 4.232 | 3.468 | 2.232 |
| | 3 | 7.586 | 6.349 | 5.201 | 3.349 |
| `MNQ` | 1 | 5.820 | 4.256 | 3.301 | 2.295 |
| | 2 | 11.639 | 8.512 | 6.602 | 4.590 |
| | 3 | 17.458 | 12.768 | 9.903 | 6.885 |

## 3 · Días con excursión adversa > $1.000, un contrato — **contado, no interpolado**

| raíz | días | **largo > $1k** | % | corto > $1k | % |
|---|---|---|---|---|---|
| `MES` | 1.851 | 10 | **0,54 %** | 5 | 0,27 % |
| `MGC` | 1.841 | 44 | **2,39 %** | 41 | 2,23 % |
| `MNQ` | 1.868 | 87 | **4,66 %** | 48 | 2,57 % |
| `ES` | 1.855 | 1.023 | **55,15 %** | 1.053 | 56,77 % |
| `GC` | 1.841 | 1.039 | **56,44 %** | 1.094 | 59,42 % |
| `NQ` | 1.858 | 1.331 | **71,64 %** | 1.409 | 75,83 % |

> **Dos órdenes de magnitud entre los micros y los grandes**, y ese solo número ordena las seis.
