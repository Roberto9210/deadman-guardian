# Qué datos de futuros existen ya en esta máquina — medición del 2026-09-02

**Sólo lectura.** No se exportó nada, no se escribió ningún decodificador, no se tocó NT8 ni el repo.
**Ninguna propuesta de qué estudiar**: primero que exista el dato.

**Por qué existe este archivo**: el foco del proyecto pasó a **futuros y cuentas de fondeo**, y antes
de decidir nada hay que saber qué hay acá **sin pagar nada**.

---

## 1 · La pregunta que decide: **NT8 DESCARGA HACIA ATRÁS.** No sólo acumula en vivo

Probado tres veces, y la tercera no admite otra lectura:

| # | prueba | dato |
|---|---|---|
| 1 | hay diarios de **2016** y el primer log de NT8 en esta máquina es **2026-08-20** | 10 años que esta máquina no pudo observar |
| 2 | los 6 primeros minutales de `ES 09-26` (barras del 16 al 21-ago) están **todos escritos en el mismo instante**: `2026-08-21 08:57` | lote, no goteo |
| 3 | **`MNQ 09-26/20260828`** —viernes, día completo de mercado— se escribió el **2026-08-31 08:23**, 3 días después. **NT8 no tiene ningún log del 28 ni del 29 de agosto**: estaba cerrado | bajó un día que no vivió |

El mecanismo lo nombra el log de la propia plataforma:
**`NinjaTrader Historical Data Server: hds-us-nt-0NN.ninjatrader.com`** — un servidor **aparte** del
feed en vivo, con 6 pares pierde/restaura entre el 21 y el 30-ago. En `Config.xml` figura además la
conexión **`Kinetick – End Of Day (Free)`**.

> **El matiz, y es NO DETERMINABLE**: lo medido es **lo que se pidió**, no **lo que se puede pedir**.

---

## 2 · El inventario

### DIARIO — 11 símbolos, 252 contratos, **14.814 barras**

| símbolo | contratos | años | barras |
|---|---|---|---|
| `MGC` / `GC` | 51 c/u | **2016–2026** | 2.722 / 2.721 |
| `ES` / `NQ` | 41 c/u | **2016–2026** | 2.690 / 2.689 |
| `MNQ` / `MES` | 30 c/u | **2019–2026** | 1.982 / 1.970 |
| `CL` / `MCL` | 2 c/u | 2026 | 9 / 9 |
| `MBT` | 2 | 2026 | 8 |
| `YM` / `MYM` | 1 c/u | 2026 | 7 / 7 |

**Guardado POR CONTRATO EXPIRADO, no como serie continua** (`ES 12-16`, `ES 03-17`, …), ~66 barras
por contrato trimestral. Esos tramos son **compatibles** con embaldosar ≈10 años continuos de días
hábiles (41×66 ≈ 2.706 vs ~2.675 esperados) — **compatible, NO VERIFICADO**: comprobar que empalman
sin huecos exige decodificar.

### MINUTO (1-min, `Last`) — 11 instrumentos, ventana de **dos semanas**

| instrumentos | rango |
|---|---|
| `ES 09-26`, `MES 09-26`, `NQ 09-26`, `MNQ 09-26`, `GC 12-26`, `MGC 12-26` | **2026-08-16 → 2026-09-02** |
| `CL 10-26`, `MCL 10-26`, `MBT 09-26`, `YM 09-26`, `MYM 09-26` | **2026-08-23 → 2026-09-02** |
| `APA` (acción) | carpeta creada, **VACÍA** |

### TICK — **VACÍO**. `db\tick\` tiene 0 subcarpetas y 0 archivos.

### Tamaño

| | |
|---|---|
| **todas las series de precio (`.ncd`)** | **1,78 MB** |
| `NinjaTrader.sqlite` (catálogo, **no** barras) | 4,39 MB |
| **`db\` completo** | **6,18 MB** |

---

## 3 · Formato, y si se lee desde afuera

**`.ncd`, binario propietario de NinjaTrader, sin documentación pública.**

- **Los diarios tienen estructura fija verificada**: cabecera **28 B** + registros de **48 B**
  (`Int64` ticks .NET + 4 `double` OHLC + `Int64` volumen). **Encajan exactos los 306 de 306**, y el
  volcado de `ES 12-16` da `0.25` como `double` en la cabecera — **el tamaño de tick real del ES**,
  que es lo que separa una coincidencia aritmética de un formato.
- **Los minutales NO usan ese formato**: **124 de 125 fallan** la comprobación de tamaño.
  **NO DETERMINABLE** si se pueden leer desde afuera.
- **La ruta soportada es la exportación de la propia NT8.** `export\` existe y está **vacía**.

> ### Decisión tomada: NO se escribe un decodificador
> Reimplementar en un binario propietario sin documentación lo que la plataforma ya hace, para
> evitar un botón que nadie apretó, **no se sostiene**. Una tubería así se rompe con cualquier
> actualización de NT8. Primero se mira qué produce la exportación.

---

## 4 · El sqlite no ayuda, y eso deja mejor fundado el `NO DETERMINABLE`

Abierto `mode=ro` sobre una **copia** verificada byte a byte. **21 tablas.**

| | |
|---|---|
| catálogo | `MasterInstruments` **1.854** (con `TickSize`, `PointValue`, `TradingHours`), `Instruments` **32.128**, `InstrumentLists` 11 |
| trading | `Orders` 97, `OrderUpdates` 470, `Executions` 92, `Accounts` 4, `AccountItems` 75 |
| **vacías** | `Positions`, `Strategies`, `Users`, `Logs`, `JournalEntries`, todas las `Strategy2*` / `User2*` |

Barrido de nombres de columna por `date/from/to/first/last/count/bars/period/days/load/range`:
**46 coincidencias, las 46 falsos positivos** (`MinPrice`/`MaxPrice` son precios de fill,
`StatementDate`/`Time` son marcas de órdenes, `LastName` es un apellido).

⇒ **El inventario de barras no existe como dato: ES el árbol de directorios.** El `NO DETERMINABLE`
deja de ser *«no miré»* y pasa a ser *«el esquema no tiene dónde ponerlo»*.

---

## 5 · Las dos mediciones que faltan — **NO SE HICIERON POR DECISIÓN, NO POR OLVIDO**

> **El operador le indicó a Roberto que hiciera SÓLO el F5 el 2026-09-02, y que lo de los datos podía
> esperar.** Queda escrito acá porque **un «no se hizo» sin causa se lee como descuido dentro de seis
> meses**.

| pendiente | estado al 2026-09-02 |
|---|---|
| **exportación de prueba** — un instrumento, diario, a archivo: qué formato, qué columnas, si conserva precisión | `export\` **vacía** |
| **historia de minutos hacia atrás** — subir el período de carga muy por encima de 5 días, pedir `ES 09-26` **una sola vez**, y volver a medir: hasta qué fecha llegó, cuántos archivos nuevos, cuánto tardó | el más viejo sigue siendo **`20260816`** |

**Línea de base perecedera, ya tomada** (si se pide historia sin tenerla, el «después» no se puede
interpretar):

| | 2026-09-02, antes de cualquier pedido |
|---|---|
| archivos de minuto, total | **136** en 11 instrumentos |
| `ES 09-26` | **15 archivos**, el más viejo **`20260816`** |
| todo `.ncd` en disco | **1,78 MB** |

**Por qué esta medición decide**: de diario hay diez años; de minuto hay dos semanas. **Lo que mata
una cuenta de fondeo es el drawdown INTRADIARIO, que una barra diaria no puede secuenciar.** Si
llegan meses, el camino existe. Si se planta cerca de los 5 días, hay techo del proveedor y todo el
diseño cambia — y conviene saberlo **antes** de pensar qué estudiar.
