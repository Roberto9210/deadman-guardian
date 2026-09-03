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

---

# ENMIENDA 1 — 2026-09-02

**Se ANOTA al pie. Nada de arriba se reescribe**: el documento es fechado, y borrar lo que decía
borraría la evidencia de que lo creímos.

> ## LA FRASE QUE NO PUEDE FALTAR
>
> **Al momento de escribir esta enmienda NO SE HABÍA VISTO NINGUNA CIFRA DE M1 A M4, y el orden del
> `git log` lo demuestra**: `bd5012a` (pre-registro, solo) → `577bbec` (script) → esta enmienda, en
> su propio commit, **antes** de que exista un solo resultado.
>
> **Precisión que corresponde hacer en vez de omitir**: el diagnóstico de C1 sí imprimió la excursión
> en puntos de **quince barras sueltas** (cinco por par), como evidencia del control. **Eso no es una
> distribución y no puede revelar una**, pero decirlo es parte de que esta frase valga algo.

## Qué decía C1, textual

> *«La razón entre la excursión en dólares de ES y la de MES, en la MISMA fecha, tiene que dar
> exactamente 10. Igual NQ contra MNQ y GC contra MGC. **Si no da 10, los multiplicadores están mal y
> el resultado en dólares no vale.**»*

**Falló en los tres pares** — exactamente 10 en sólo 14,85 % / 4,88 % / 6,91 % de las fechas.

## Por qué falló: C1 probaba DOS proposiciones a la vez

La razón en dólares factoriza exacto:

```
(puntos_grande × pv_grande) / (puntos_micro × pv_micro)
    =  (pv_grande / pv_micro)  ×  (puntos_grande / puntos_micro)
```

| proposición | veredicto | evidencia |
|---|---|---|
| **los multiplicadores son correctos** | **CIERTA, y verificada** | `50/5 = 10` · `20/2 = 10` · `100/10 = 10`, exactos, leídos de `MasterInstruments.PointValue` en la máquina |
| **el grande y el micro imprimen el mismo OHLC** | **FALSA, y nunca lo fue** | mediana de la razón en puntos `1,0000` / `0,9983` / `1,0000`, **pero idéntica sólo en 15,0 % / 5,4 % / 5,0 %** de las fechas |

**El ejemplo que lo cierra:**

```
2019-05-06    ES: open 2917.75  low 2883.50  ->  34.25 pts
             MES: open 2925.75  low 2883.75  ->  42.00 pts
                       ↑ OCHO PUNTOS de diferencia en el open, contra el mismo low
```

**Un control que falla sin que nada esté roto no mide lo que dice medir.**

## PASO 3 — REGLA NUEVA, y es la lección de fondo

> ### NINGUNA RAÍZ SE DERIVA DE OTRA.
> **`MES` se mide sobre las barras de `MES`.** El grande y el micro son **libros de órdenes
> separados** y **no comparten OHLC**. Ningún cálculo escala uno para obtener el otro.

## Los controles quedan así — **cada uno declara QUÉ tumba si falla**

| control | qué comprueba | **qué tumba si falla** |
|---|---|---|
| **C1′ MULTIPLICADOR** | que los valores que usa el script sean **exactamente** los de `MasterInstruments.PointValue`, y que las razones grande/micro den **exactamente 10**. **Imprime los seis valores** | **sólo la conversión a dólares.** M1–M4 en puntos quedan en pie |
| **C1b ARITMÉTICA** *(nuevo, y NO es un test)* | imprime **UNA fila trabajada entera** — fecha, raíz, `open`, `low`, puntos, multiplicador, dólares — **para que un lector la rehaga a mano**. Un **testigo impreso**, no una aserción que compruebe mi propia definición | nada: no afirma, muestra |
| **C2 ESCALA** | 2 contratos dan exactamente el doble que 1 | **TODO lo que use cantidad de contratos, que es todo** |
| **C3 ORDEN** | drawdown real ≠ drawdown barajado, semilla `20260902` | **sólo M4** |
| **C4 DEGENERADOS** *(nuevo)* | por raíz, cuántas fechas tienen `open == low` y cuántas `open == high`, con conteo y porcentaje. La razón fallada mostró valores de `0,0000` y de `163`, que significan **excursión cero en uno de los dos** | **la distribución de la raíz afectada**, que debe declararse sospechosa **antes** de publicarse |

**La parada deja de ser global y pasa a ser por control.** Un control que falla invalida **su
alcance declarado**, no el hallazgo entero — que es lo que este documento ya decía arriba y el
PASO 3 de la instrucción anterior contradecía.
