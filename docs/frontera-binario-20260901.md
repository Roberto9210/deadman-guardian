# Frontera entre binarios — despliegue del 2026-09-01

**Anotada A MANO porque el ledger no puede anotarla solo.** Ver §3: es un defecto propio.

**Por qué existe este archivo**: el sonido y el panel son **conducta nueva** que vamos a querer
atribuir mañana. Si el registro no distingue las dos versiones, **todo lo que midamos sobre conducta
nueva estará sobre un registro que no sabe qué código lo escribió.** Un minuto de anotación es lo único
que fecha el corte.

---

## 1 · ANTES del despliegue — **valor final, con NT8 CERRADO**

| | |
|---|---|
| **último `seq` escrito por el binario viejo** | **8101** — `GUARDIAN_STOPPED`, 2026-09-01T23:21:49.837Z |
| **hash del binario viejo** | **`12fff6f6c76d838c`** (`GuardianCore.dll`, net48, 2026-08-31 19:09:03) |
| estado en disco al cerrar | `DISARMED`, sin sello, `dayKey 2026-09-01`, `lastSeenUtc` 23:21:49.416Z |
| NT8 | **cerrado** — verificado en `tasklist` |

> ### ⚠ CORRECCIÓN, y el error era de mi propia frase
>
> Esta tabla decía **8100** y agregaba que era *"estable mientras el guardián siga `DISARMED`, porque
> desarmado no escribe nada"*. **Eso era falso, y lo desmintió el cierre 6 minutos después:**
> `GUARDIAN_STOPPED` es un evento **de CICLO DE VIDA**, no de estado. Un guardián desarmado no escribe
> nada **mientras corre** — pero **sí escribe al morir**.
>
> **El número correcto es 8101**, y es definitivo: NT8 está cerrado y el binario viejo no va a escribir
> nada más.
>
> **La familia del día, una vez más**: la nota se escribió **verdadera** y el mundo siguió moviéndose
> debajo. Un documento cuyo único trabajo es registrar un número exacto, **equivocado en ese número, es
> peor que no tenerlo** — porque se lee con la confianza de una medición.

## 2 · DESPUÉS del despliegue — a completar en el momento

| | |
|---|---|
| **hash del binario nuevo** | **`6529a92ae9b47240`** — desplegado 2026-09-02 18:09:57 CDT, 108.032 B (el viejo: 99.840 B) |
| **primer `seq` escrito por el binario nuevo** | **8102** con el `GuardianCore.dll` nuevo · **8105** con el despliegue completo — **son dos, y ver abajo por qué** |
| fecha y hora UTC del arranque | **2026-09-02T23:13:34.506Z** (8102) · **2026-09-02T23:14:52.020Z** (8105) |

> ### ⚠ EL DESPLIEGUE ENTRÓ EN DOS ETAPAS, Y EL LEDGER TIENE ENTRADAS DE UN HÍBRIDO QUE NADIE CONSTRUYÓ
>
> No estaba previsto y no lo predijo nadie. **`GuardianCore.dll` y el addon no entran juntos**: el DLL
> se copia con `install.ps1` y NT8 lo carga **al arrancar**; el addon vive dentro de
> `NinjaTrader.Custom.dll`, que **sólo se recompila con F5**.
>
> | | |
> |---|---|
> | `GuardianCore.dll` copiado | 2026-09-02 18:09:57 CDT, **antes** de abrir NT8 |
> | NT8 abre → **seq 8102** | `GuardianCore` **NUEVO** + addon **VIEJO** (compilado el 31-ago) |
> | `NinjaTrader.Custom.dll` recompilado | **2026-09-02 18:14:51** = `GUARDIAN_STOPPED` **8104** a las 23:14:51.762Z |
> | addon nuevo carga 0,26 s después → **seq 8105** | **todo nuevo** |
>
> ⇒ **`8102`, `8103` y `8104` los escribió una combinación que no corresponde a ningún commit**:
> Core del 2-sep con addon del 31-ago. Duró **77 segundos**. **El F5 no es el momento del
> despliegue: es el segundo de dos.**
>
> #### Y LA CASA YA CONOCÍA ESTA VENTANA — POR LA OTRA PUERTA
>
> No faltaba el dato. **Faltaba la otra lectura.** La ventana está escrita **cuatro veces** en el
> código, siempre como **restricción para cambiar una API**, nunca como **estado operativo**:
>
> | símbolo | qué dice |
> |---|---|
> | `CertificateRequest.DaysCovered` | quitar el parámetro *«rompe el adaptador ya compilado en la ventana entre desplegar este DLL y el F5 que lo recompila»* |
> | `Certificate.Issue`, param `chainVerified` | *«en la ventana entre desplegar el DLL y el humano apretando F5»* |
> | el cálculo de `daysCovered` dentro de `Certificate` | mismo motivo, remoción coordinada |
> | **`PositionSnapshot`, dos constructores** | y **nombra el fallo exacto**: `MissingMethodException` |
>
> **Mismo hecho, dos lecturas, y sólo una estaba escrita.** Es la forma que toma un hallazgo que ya
> estaba delante.
>
> #### La consecuencia operativa, que sale sola
>
> > **Si el guardián hubiera estado ARMADO en esos 77 segundos, habría estado haciendo cumplir un
> > límite con una combinación que ningún test cubre.**
>
> Hoy no pasó porque estaba `DISARMED`. **Eso es suerte de calendario, no una propiedad.**

> ### ✅ 2026-09-02 18:10 CDT — **LOS BYTES ESTÁN PUESTOS. FALTA EL F5, QUE ES DE UNA PERSONA.**
>
> `dotnet build … -c Release` verde (0 warnings, 0 errors) → la nota `STALE-ON-PURPOSE.txt` **caducó
> por su propia regla** (el DLL de al lado pasó a ser más nuevo) y se borró → `nt\install.ps1` copió
> **6 archivos**.
>
> **Verificado por mi cuenta, no por el reporte del instalador**: los 5 `.cs` desplegados son
> **idénticos byte a byte** a los del repo, y el DLL desplegado hashea **`6529a92ae9b47240`**, igual
> que el construido. `SoundChannel.cs` **no estaba** en la lista `<Compile>` del `.csproj` y el
> instalador lo agregó — **es exactamente el defecto que su chequeo de completitud existe para
> atrapar** (el CS0246 en vivo del 2026-08-25), y esta vez lo atrapó.
>
> **Lo que todavía NO pasó, y por eso las dos filas de abajo siguen en blanco:**
> `NinjaTrader.Custom.dll` sigue fechado **2026-08-31 19:18:31** ⇒ **no hubo compilación**.
> NinjaTrader compila NinjaScript **a pedido, desde el editor con F5** — no al arrancar, y no por
> reiniciar. **Es un gesto de teclado dentro de la plataforma: no lo puede hacer una sesión.**
>
> ⇒ **La frontera queda a medias A PROPÓSITO**: el hash nuevo ya es un hecho medido; el `seq` y la
> hora sólo existirán cuando alguien abra NT8 y apriete F5. **La predicción sellada sigue sin
> evaluar hasta ese momento.**

**Comprobación cruzada gratis, y hay que hacerla**: ese primer arranque es el que
`docs/prediccion-sellada-20260901-despliegue.md` predice **evento por evento**. Anotar el `seq` y
comparar contra la predicción **es el mismo minuto de trabajo**.

> ### 🔎 VERIFICACIÓN INTENTADA 2026-09-02 17:49 CDT (22:49Z) — **EL DESPLIEGUE NO OCURRIÓ**
>
> Se pidió cerrar esta tabla. **No se puede: no hay binario nuevo y no hay arranque nuevo.** Los
> huecos siguen vacíos **porque nadie los llenó todavía, no porque nadie haya mirado.**
>
> | medición | valor, 2026-09-02 22:49Z |
> |---|---|
> | `GuardianCore.dll` desplegado | **`12fff6f6c76d838c`** — el **mismo** de la §1, mtime 2026-09-01T00:09:03Z |
> | `bin/Release/net48/GuardianCore.dll` del repo | mismos 99.840 bytes, misma fecha ⇒ **no se reconstruyó** |
> | `NinjaTrader.Custom.dll` | 2026-09-01T00:18:31Z ⇒ **no hubo F5** |
> | `AddOns/DeadmanGuardianAddOn.cs` instalado | 58.846 B (31-ago 19:12); el del repo tiene 65.238 B ⇒ **difieren**: el sonido no está instalado |
> | último `seq` del ledger | **8101**, sin cambio; 8.101 líneas |
> | `state.json` | `DISARMED`, `dayKey 2026-09-01`, sin sello, `runId 1e1b67…` |
> | NinjaTrader corriendo | **no** — 0 procesos |
> | logs de NT8 | el más nuevo es `log.20260901.*` ⇒ **NT8 no se abrió el 2026-09-02** |
> | archivos modificados hoy en el árbol de NT8 **y** en el repo | **cero, en los dos** |
>
> **Y la nota que se vence sola sigue viva**, que es evidencia independiente:
> `bin/Release/net48/STALE-ON-PURPOSE.txt` nombra el hash `12fff6f6c76d838c` y **todavía coincide**
> con el DLL de al lado. Su propia regla —*«si el de al lado es más nuevo, borrala»*— **no se
> disparó**. Es el mecanismo funcionando en el único momento en que se lo puede ver funcionar:
> **cuando no hay nada que reportar, lo dice.**
>
> **Hipótesis de qué pasó — NO VERIFICADA, y hay un discriminador**: `install.ps1` **sale con
> `exit 5` y no copia nada** cuando el build es más viejo que los fuentes, que es exactamente el
> estado de hoy. Una corrida así **no toca ningún archivo**, así que el disco **no la distingue de
> «no se corrió»**. Lo que sí la distingue es la **consola**: habría impreso el mensaje de build
> viejo. Si alguien la corrió y leyó el final sin mirar el código de salida, **el despliegue se dio
> por hecho sin que nada se copiara** — y la guarda habría hecho su trabajo *en silencio*.
>
> **Lo que falta para desplegar, en orden:**
> `dotnet build src\GuardianCore\GuardianCore.csproj -c Release` → borrar `STALE-ON-PURPOSE.txt`
> (su propia nota lo pide) → `nt\install.ps1` **leyendo el código de salida** → F5 en NT8 → volver acá.

---

## 3 · Y esto es un defecto propio, de la familia del día

> ### EL LEDGER NO PUEDE DECIR QUÉ CÓDIGO LO ESCRIBIÓ.

> **YA NO ES UNA PREDICCIÓN: ESTÁ DEMOSTRADO EN VIVO, 2026-09-02.** Se desplegó código distinto
> —`12fff6f6c76d838c` → `6529a92ae9b47240`, 99.840 → 108.032 bytes— **y el registro no lo notó.**
> Comparadas la línea del binario viejo (`seq 8099`) y la del nuevo (`seq 8105`), **borrando sólo los
> cuatro valores que son por-entrada** (`hash`, `prev`, `seq`, `tsUtc`), **las dos líneas son
> idénticas**, y hasta miden **271 bytes cada una**. Lo mismo con las dos de `STATE_RESTORED`.
>
> **Vale más como evidencia que como predicción acertada**: una predicción acertada dice que leímos
> bien el código; esto dice que **la propiedad existe en producción y acaba de ejercerse**. El
> ledger cruzó una frontera de binario sin registrarla.

`GUARDIAN_STARTED` no lleva `buildHash`; sus dos formas completas son `{state, fresh}` y `{state}`.
Así que después de esta noche **las líneas de arranque del binario nuevo serán byte a byte
indistinguibles de las del viejo**, y la frontera **existe pero es inencontrable** desde el artefacto.

**Es exactamente la familia de todo lo demás de hoy**: *el artefacto que viaja no lleva el dato que el
lector necesita*. El ledger viaja —es lo que se cita, lo que se audita, lo que sobrevive— y no lleva su
propia procedencia.

**Dónde SÍ vive el hash hoy**: en el **certificado**, campo `issuer.buildHash`, que el addon calcula
del archivo del ensamblado. O sea: **un artefacto lo tiene y el otro no**, y el que no lo tiene es el
que se escribe siempre.

**El arreglo es el ítem 5 de la cola** —hash del binario en `GUARDIAN_STARTED`— y sigue esperando la
respuesta del contrato de extensión. **Mientras tanto, la frontera se anota a mano, acá.**

### Nota de resolución: dos afirmaciones que parecían contradecirse, y no lo hacían

- **Lo que decía el ítem 5 de la cola** (`CLAUDE.md`): *"el `buildHash` va en LOS DOS, no en uno"* —
  **es una instrucción de diseño para cuando ese ítem se implemente**, no una descripción de hoy. Está
  escrita dentro de un ítem que empieza con *"sólo después del 2"*, o sea pendiente y bloqueado.
- **Lo que se midió el 1-sep**: el evento **no tiene** el campo. Cierto, y sobre el presente.
- **Las dos son verdaderas y hablan de tiempos distintos** — pero la frase se lee como un hecho fuera
  de su contexto, **y así la leyó el operador**. Eso alcanza para considerarla defectuosa: se aclaró en
  `CLAUDE.md` en vez de dejarla como estaba.
- **Y el hash que sí existe hoy está en el certificado** (`issuer.buildHash`), que es de dónde venía la
  sensación de que el dato existía. **Existe: en el otro artefacto.**
