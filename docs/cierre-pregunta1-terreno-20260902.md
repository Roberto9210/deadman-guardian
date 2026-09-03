# CIERRE — Pregunta 1: qué tan hostil es el terreno · 2026-09-02

**Una familia se cierra con un documento fechado en el repo, con sus números y su motivo, o NO está
cerrada: está olvidada.** Esto es ese documento.

| | |
|---|---|
| **método** | `docs/prerregistro-pregunta1-terreno.md` + Enmienda 1 |
| **números** | `docs/resultado-pregunta1-terreno.md` |
| **código** | `tools/nt8-data/terreno_pregunta1*.py`, `terreno_m4prime.py` |
| **datos** | 252 CSV de `37a0144`, hasta **2026-08-21** |

**El orden del `git log` es la garantía, y es lo único que un tercero puede leer sin confiarnos:**

```
bd5012a  pre-registro, SOLO       ← ninguna cifra existía
577bbec  script (paró en C1)
8e5f5c8  enmienda 1, SOLA         ← seguía sin existir ninguna cifra de M1-M4
7761eb4  los números
f75d126  control de período + tabla invertida
```

---

## 1 · LO QUE ESTA PREGUNTA NO MIDIÓ

> ### NO MIDIÓ SI EXISTE UNA VENTAJA. NO BUSCÓ NINGUNA.
> Y quedó declarado **antes** de ver un número, no después.

## 2 · Las tres limitaciones declaradas antes de medir — sin ablandar

1. **Supone una posición mantenida de apertura a cierre**, que ninguna estrategia real hace. **Es un
   piso del terreno, no una simulación.** Con barras diarias no se ve el camino intradiario: M1 es
   exacta **sólo** para una entrada en la apertura.
2. **Las fechas descartadas caen en las uniones, que son trimestrales.** 40 fechas en `ES` y `NQ`, 50
   en `GC`/`MGC`, 29 en `MES`/`MNQ`. **No son aleatorias**, y cualquier resultado con estructura
   trimestral hereda ese sesgo.
3. **La ventana de 60 días descansa sobre n = 123–250.** Varios percentiles altos se repiten contra
   el máximo — `GC` marca 53.540 en el 1 %, el 5 % **y** el 10 %. **Esas celdas no distinguen nada
   entre sí.**

**Y una cuarta, agregada al medir M4′**: es la cota de una **tenencia continua desde la apertura**.
El drawdown *trailing* real usa la **marca de agua móvil** de la cuenta, que depende de cuándo
entraste y saliste. **M4′ acota, no simula.**

## 3 · EL HALLAZGO: el período manda más que el instrumento

`MES` salió 35 % por encima de `ES` en la mediana de M1. **Al igualar el período la brecha
desaparece:**

| par | brecha con período igualado |
|---|---|
| `ES`/`MES` | **1,06 %** |
| `NQ`/`MNQ` | **0,35 %** |
| `GC`/`MGC` | **1,71 %** |

Y el mismo instrumento, recortado al período de los micros, se mueve:

| raíz | p50 | p99 |
|---|---|---|
| `ES` | **+34,3 %** | +3,7 % |
| `NQ` | **+44,3 %** | +6,1 % |
| `GC` | +27,2 % | **+19,8 %** |
| `MGC` | +29,3 % | **+15,3 %** |

> **Elegir período cambia la mediana más que elegir instrumento.**

### La asimetría índices / metales, que es la mitad que importa

**En los índices el efecto del régimen se disuelve hacia la cola** — `ES` p99 sólo **+3,7 %**, `NQ`
**+6,1 %**. **En los metales NO**: `GC` y `MGC` se mantienen sobre **+15 %** en todos los
percentiles, incluido p99.

> ## REGLA DE CITADO, que sale de ahí
>
> **Toda cifra de METALES viaja con su ventana. Sin la ventana no significa nada** — se mueve entre
> 15 % y 29 % según el período, en cualquier percentil.
>
> **Las cifras de ÍNDICES EN LA COLA (p95, p99) son estables entre regímenes** y se pueden citar sin
> la ventana. **Sus medianas no**: se mueven 34 % y 44 %.

## 4 · Los cinco controles y sus veredictos

| control | qué comprueba | veredicto | alcance si fallaba |
|---|---|---|---|
| **C1′ multiplicador** | los seis valores **son** los de `MasterInstruments.PointValue`; `50/5`, `20/2`, `100/10` = `10.0000000000` | **PASA** | sólo la sección de dólares |
| **C1b aritmética** | fila trabajada impresa para rehacer a mano — **testigo, no aserción** | *no afirma* | — |
| **C2 escala** | 2 contratos = exactamente el doble, razón `2.000000` en las seis raíces | **PASA** | **todo** |
| **C3 orden** | drawdown real ≠ barajado, semilla `20260902`, en **las 12** combinaciones | **PASA** | sólo M4 |
| **C4 degenerados** | `open==low` o `open==high`: `ES` 1,43 % · `NQ` 1,67 % · `GC` 0,97 % · `MGC` 1,59 % · **`MES` 1,54 % · `MNQ` 2,00 %** | **micros usables** | la distribución de esa raíz |
| **control de período** | brecha grande/micro con período igualado, criterio **declarado antes**: pasa bajo 5 % | **PASA** | **la tabla de decisión entera** |

**El C1 original FALLÓ y el fallo enseñó más que un verde**: probaba dos proposiciones a la vez —que
los multiplicadores son correctos **y** que grande y micro imprimen el mismo OHLC—. La primera es
cierta; **la segunda es falsa y nunca lo fue** (`2019-05-06`: `ES` abre 2917,75 y `MES` 2925,75,
**ocho puntos**, contra el mismo mínimo). De ahí salió la regla: **NINGUNA RAÍZ SE DERIVA DE OTRA.**

## 5 · Los números que sobreviven al cierre

| | |
|---|---|
| **días con excursión adversa > $1.000, 1 contrato** | `MES` **0,54 %** · `MGC` 2,39 % · `MNQ` 4,66 % · `ES` **55,15 %** · `GC` 56,44 % · `NQ` **71,64 %** |
| **medir contra el cierre subestima** | entre **2 % y 32 %** (M4′/M4). Peor en ventana 20 e índices por el lado largo: `ES` **1,291** |
| **la tabla se lee al revés** | uno entra con su número de dólares y sale con el porcentaje de días o ventanas que lo rompen. **Ningún límite de ninguna firma aparece en ningún lado** |

## 6 · Qué queda abierto, nombrado para que no se pierda

- **La serie termina el 2026-08-21** y no se mantiene sola: se actualiza cuando alguien la pide.
- **Los minutales no se decodifican** con el formato del diario (124 de 125 fallan la comprobación de
  tamaño), así que **el camino intradiario sigue sin verse**.
- **Los «faltantes» no se contrastaron contra un calendario de feriados de CME.**
- **No se verificó** que las barras históricas (2016–2025) sean ETH; sí la de hoy.

---

> **Cerrado el 2026-09-02. Se reabre con datos nuevos o con una pregunta distinta — no con una
> relectura de estos mismos números.**
