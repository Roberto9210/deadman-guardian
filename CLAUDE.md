# deadman-guardian — mapa para sesiones futuras (actualizar al cierre de cada tanda)

Última actualización: 2026-08-26 (**prueba viva CORRIDA: encontró LT-1, el defecto más grave abierto**).

**ESTADO OPERATIVO AL CIERRE DEL 26-ago — leer antes de tocar nada:**
- **NinjaTrader está CERRADO a propósito y debe seguir así hasta mañana 17:00 CT.** El guardián quedó
  `LOCKED` con una posición abierta en Sim101 y un bucle de aplanado que giró 167 veces. **Si se reabre
  antes de que expire el sello, el bucle reinicia** — ~1 evento por segundo en el ledger.
- **`config.json` tiene el límite de prueba ($40), no el de producción ($600).** No se toca hasta que
  expire el sello: editarlo bajo sello escribe `CONFIG_TAMPERED` y acusa de manipulación. El respaldo
  es `config.json.produccion-20260826` (hash `38a15089c889ac09`). La vuelta está en
  `docs/proposals/live-production-breach-test.md`.
- **`install.ps1` no se corre**: `bin\Release` está en `57d36a5d6a8a9113` y lo desplegado en
  `4d20652361c0b468`. Ninguna guarda del instalador detendría ese despliegue.
- `botA.GO` sigue puesto y `soak.GO` sigue aparcado como `soak.GO.parked-for-livetest`.
Es un mapa, no un manual. El historial forense está en `git log`; la especificación en `SPEC.md`,
las decisiones revisadas en `AMENDMENTS.md`, y el diagnóstico del error espejo en
`docs/error_espejo.md`.

## 1. Qué es
Add-on de NinjaTrader 8 que impide a un trader de prop firm romper su propio límite de pérdida
diaria. Observa ejecuciones y P&L de una sola cuenta sellada; al alcanzarse el límite cancela las
órdenes de esa cuenta y aplana sus posiciones, y bloquea entradas por el resto del día. Corre local,
sin red, y su registro es un ledger encadenado por hash que sirve de evidencia.

**Las dos cosas que JAMÁS hace: abrir posiciones y generar señales.** No es una estrategia, no
opina sobre el mercado, y no envía una orden que no sea una cancelación o un aplanado de la cuenta
que le sellaron.

## 2. Método obligatorio
1. **Verificar contra el código antes de diseñar.** Toda afirmación —de un reporte previo, de este
   archivo, o de un prompt— se re-verifica con grep/lectura. Si no se puede verificar, se dice "no
   verificado" y no se construye encima. **Un hallazgo no verificado no es cierto.**
2. **Rojo primero.** La prueba se escribe antes del arreglo, se la ve fallar, y recién después se
   toca el código. **Un verde que no se puso rojo antes no prueba nada.** Si la decisión vive en
   código que un test no puede ejecutar (adaptador), extraerla a código puro primero: que algo sea
   inejecutable en un test es parte del defecto, no una excusa.
3. **Diffs antes de aplicar**, y esperar el OK en cualquier cambio que toque la puerta del breach.
4. **Commits separados por fix, por ruta explícita. Nunca `git add -A`.**
5. **Una sola sesión por repositorio.** Esta ventana es la de `deadman-guardian`; no toca `deadman`
   (Ventana B) ni `deadman-research` (exclusivo de Roberto, ni siquiera lectura).
6. Cada reporte abre identificando la ventana.
7. No declarar "arreglado" sin salida de prueba real. Reportar los fallos tal cual.
8. Herramientas: los heredocs destrozan los backslashes de rutas Windows (`nt\addon` llegó como BEL,
   `nt\bots` como backspace). Escribir los scripts de parche por archivo y correrlos.

## 3. La clase de defecto de la casa
**TEXTO QUE AFIRMA MÁS DE LO QUE SU PROPIO CÓDIGO COMPROBÓ.**

Dos instancias el mismo día, en archivos sin relación:
- `install.ps1` imprimía *"the deployed binary is the one you just built"* — en un script que **no
  compila nada**. Sobrevivió sin un murmullo a un despliegue de un build viejo.
- `Guardian.cs:657` escribe *"daily loss limit reached on adopted figures alone"* sobre una cifra
  adoptada **que puede ser cero** (M22, abajo).

Y una tercera dentro del arreglo de la primera: la reversión de `exit 6` anunciaba `ROLLED BACK`
cuando el backup faltaba y no había restaurado nada. La atrapó su propio test.

**La regla: cada mensaje afirma exactamente lo que su propia comprobación establece, ni una palabra
más.** Si hacen falta dos afirmaciones, se escriben dos (`COPY VERIFIED` / `BUILD CURRENT`).

### Subtipo, y es el más difícil de ver: UNA AFIRMACIÓN CIERTA SOBRE EL CONJUNTO EQUIVOCADO

No siempre el texto afirma de más. A veces afirma algo **perfectamente cierto**, y el conjunto sobre
el que se calculó no es el conjunto del que habla. No hay mentira en la frase: hay un desajuste entre
**el alcance de la afirmación y el alcance del cómputo**.

Los dos hallazgos grandes del 26-ago son el mismo animal:

- **`MATCH` en `install.ps1`** era cierto — los bytes desplegados eran los bytes de `bin\`. Pero el
  conjunto que importaba era "el código que queríamos desplegar", y `bin\` no era ese conjunto.
- **`daysCovered: 1`** habla de **un día** y se calcula sobre **el ledger entero** (`Issue` toma
  `min/max(seq)` de lo que recibe, y el addon le pasa todo). La cifra existe; el alcance no coincide.

Es más peligroso que el sobre-afirmar directo, porque **cada pieza resiste su propia inspección**: el
cómputo es correcto, la frase es correcta, y el defecto vive sólo en la junta entre las dos.

**La pregunta de cacería, ante cualquier afirmación del producto:** *¿sobre qué conjunto se calculó, y
es el mismo conjunto del que habla?* Si no se puede contestar leyendo el código que la produce, la
afirmación no está lista para ir a un documento que alguien va a auditar.

### Segundo subtipo, encontrado por la prueba viva del 26-ago

> **UN DOBLE DE PRUEBA QUE SIMPLIFICA LA REALIDAD NO PRUEBA MENOS — PRUEBA OTRA COSA, Y EL VERDE DICE
> QUE PROBASTE LA QUE NO ERA.**

`FakeBroker.Flatten` borra la posición **de una lista**: sin orden, sin envío, sin aceptación, sin
cancelación posible — **sin ciclo de vida**. El doble hizo el aplanado **atómico**, el diseño asumió esa
atomicidad sin que nadie escribiera la suposición, y en producción el guardián **cancela sus propias
órdenes de aplanado**. Las 16 corridas del soak y los 256 tests eran verdes y **lo siguen siendo**:
prueban un mundo donde aplanar es instantáneo, y ese mundo no existe.

Es hermana del subtipo de arriba, y peor de cazar: ahí el conjunto equivocado era un subconjunto;
acá **es un universo entero construido a propósito**, y su diferencia con el real ES el defecto.
**Pregunta de cacería:** ¿en qué se diferencia mi doble del mundo real, y esa diferencia puede ser
justo donde vive el defecto?

Corolario hermano: **un chequeo que existe no es un chequeo que corre.** Antes de confiar en una
protección, buscar su productor y correrla. Y un chequeo inalcanzable se borra — no se deja
decorando.

## 4. Operativa
- **Instalar**: `.\nt\install.ps1 -WithSoak -WithBots`. Se niega **antes de mutar** en tres casos y
  **revierte** en el cuarto:

  | código | significa |
  |---|---|
  | `2` | NinjaTrader está abierto — tiene el DLL tomado. Nada se copió. |
  | `4` | Hay `.cs` en `nt/addon`, `nt/soak` o `nt/bots` que las listas del script no gestionan. Nada se copió. |
  | `5` | El build es más viejo que su fuente. Compilar primero. Nada se copió. |
  | `6` | Lo desplegado no coincide con el repo. **Revierte solo** y lo dice; si la reversión falla, nombra `-Uninstall`. |

- **NT8 compila NinjaScript recién con F5**, no al arrancar. Un reinicio no compila; borrar
  `NinjaTrader.Custom.dll` hace que NT8 restaure una copia de fábrica en vez de construir una
  (`STEP3_FINDINGS.md §6`).
- **Cerrar NT8 desde la bandeja del sistema** (flechita `^` junto al reloj → click derecho → Exit).
  Cerrar la ventana principal no alcanza. Cerrar con gracia, nunca matar el proceso: un kill duro le
  roba al guardián su `GUARDIAN_STOPPED`.
- **Deshacer**: `.\nt\install.ps1 -Uninstall` borra lo desplegado y restaura el `csproj` del backup.
- **CON SELLO VIGENTE, `config.json` NO SE TOCA POR NINGÚN MOTIVO — ni siquiera para restaurar.**
  `Guardian.OnConfigFileObserved` (`:517-529`) compara el hash en disco contra el sellado en cada tick
  del addon (`DeadmanGuardianAddOn.cs:229-237`), y **cualquier** diferencia escribe `CONFIG_TAMPERED` y
  entra en lockout — **también una más estricta**. Restaurar un límite más duro queda registrado como
  intento de operar por encima del límite: una acusación falsa y permanente en la cadena.
  **No existe un `Disarm` deliberado**: `Ev.Disarmed` se escribe en un solo sitio (`Guardian.cs:813`),
  dentro de `CheckExpiry`, que es donde `_state.Seal = null` (`:816`). La única salida es la expiración.
  Consecuencia operativa: **armar con el límite equivocado se paga hasta el corte de sesión.**
- **La tercera relación que el instalador NO cubre**: desplegado contra *lo que se probó*. Verifica
  build-vs-fuente y desplegado-vs-build, pero nada ata el binario que corrió al que se verificó.
  Contrapropuesta evaluada al `.deploy-pin`: **que `GUARDIAN_STARTED` registre el hash del binario**
  — hoy sólo lleva `state` (`Guardian.cs:154,183`). Ver `docs/proposals/deployed-vs-tested.md`.
- **Verificar qué está corriendo**: el hash corto del DLL desplegado es el mismo valor que el
  certificado reporta como `issuer.buildHash`. La línea del Log de NT8 (`version='0.1.0.0'`) es
  idéntica en todo build jamás hecho: dice que algo cargó, no **cuál**.
- El guardián nunca coloca órdenes de entrada. Los bots de prueba (`DeadmanBotA`/`B`) sí envían
  órdenes fillables, sólo en Sim101 verificada por `Provider == Simulator`, y sólo con su archivo
  `.GO` presente.

## 5. Defectos abiertos (verificados, con archivo:línea)
| # | qué | dónde |
|---|---|---|
| **LT-1** | **EL GUARDIÁN CANCELA SUS PROPIAS ÓRDENES DE APLANADO, Y LAS SALIDAS DEL TRADER.** `CancelAllOrders(order.Account)` es incondicional: no mira lado, ni origen, ni si la orden reduce exposición. Probado en vivo el 26-ago: 167 intentos, `FLATTEN_VERIFIED` cero, y `Sell`/`BuyToCover` cancelados. **PRIMERO EN EL MAPA**, por delante de todo: es el único defecto abierto que cuesta dinero y atrapa al trader en una posición. Informe: `docs/live-test-findings-20260826.md` | `Guardian.cs:576` |
| **LT-2** | **La familia de M15 que no barrí.** `_personalLimit`, `_resetLocalTime` y `_zoneId` se asignan **sólo en la ruta de armado** (`:268`); un reinicio que restaura `ARMED` desde el sello los deja en su default. Consecuencia vista: el mensaje del breach publicó **"your limit is $0.00"** con límite $40, y el "until 17:00" desaparece en silencio de todos los mensajes. | `DeadmanGuardianAddOn.cs:50-52,268` |
| LT-3 | Los dos mensajes del lockout no contemplan *"la promesa no se puede cumplir"*: `LockoutComplete` cuelga de `FLATTEN_VERIFIED`, así que el trader leyó *"I am about to close your positions"* y después nada, 167 vueltas. Correcto por diseño, malo en consecuencia. | `DeadmanGuardianAddOn.cs:427-434` |
| **cert-1 + cert-3** | **Son un solo defecto: el certificado no tiene ALCANCE.** El addon le pasa a `Certificate.Issue` el ledger ENTERO (`:306`), `Issue` toma `min/max(seq)` de lo que recibe (`:247-250`), y `Recompute` recorre todo — así que `limitRespected`, `lockoutsTriggered` y `failClosedEpisodes` son totales de nueve días bajo un encabezado de un día, y `daysCovered: 1` **es falso**, no sólo cableado. Evidencia publicada: `certificate-2026-08-24.json` trae `ledgerRange {fromSeq:1}` y un episodio del 21-ago. **ES LA PRÓXIMA TANDA**, aprobada: acotar el certificado al día que nombra, con la maquinaria que `Recompute` ya expone y nadie llama — arregla tres defectos de una vez y hace que `daysCovered: 1` sea verdad por construcción. Rojo primero. `limitRespected` **NO** entra: es semántica, no alcance (ver `docs/proposals/deployed-vs-tested.md`, apéndice). Decisión en `docs/proposals/what-days-covered-should-mean.md` | `Certificate.cs:67,247-250`, `DeadmanGuardianAddOn.cs:306,315` |
| cert-2 | **CERRADO en la parte que es nuestra.** `CERT_CONFORMANCE.md` pasa la prueba de leerlo con la función apagada y además **publica la ausencia**: *"hoy no hay ancla externa, y el verificador lo dice en su propia salida"*. Nada que arreglar acá. | `CERT_CONFORMANCE.md` |
| **cert-2b** | **PENDIENTE DE VENTANA B — NO ES TERRITORIO DE ESTA VENTANA.** La pregunta original (¿`SPEC §2b` promete L2 como *alcanzado* en vez de *disponible*?) apunta al `§2b` del repo **`deadman`**, no al nuestro: acá la §2 es "The one number that matters" y no habla de anclaje. Queda anotado para que no se pierda; **esta ventana no lo toca.** | repo `deadman` |
| M4 | Suspensión de la máquina ⇒ salto de reloj hacia adelante ⇒ bloqueo. **Sin medir.** | `Guardian.cs:753` |
| M5 | Un tick sin precio con posición abierta ⇒ bloqueo. Diseñado así. | `PnlAccounting.cs:218` |
| M6 | Desconexión ⇒ bloqueo. Diseñado así; ocurrió varias veces. | `PnlAccounting.cs:198` |
| M7 | La deduplicación de fills es condicional: una ejecución sin `ExecutionId` se cuenta cada vez. Sin evidencia de que NT8 emita id nulo. | `PnlAccounting.cs:144` |
| M20 | El feed reporta no-realizado sin que el broker reporte posición ⇒ el guardián sigue ciego. Residual, sin arreglar a propósito. | `PnlAccounting.cs:210-222` |
| M21 | Primer arranque sobre un ledger viejo ⇒ rehúsa el baseline hasta que ruede el día. Una vez por instalación. | `Guardian.cs`, `LoadSameDayCheckpointGross` |
| ui-1 | El titular se deriva del **estado**, no de la **causa**. **Parcialmente cubierto**: el caso de M22 tiene titular propio (`Headline(kind, reason)` + `IsLimitNotFlattened`). Siguen sin cubrir los demás casos de `FailClosed` — discrepancia de fuentes, precio ausente, reloj, ledger — que comparten `CANNOT SEE YOUR ACCOUNT`. | `Messages.cs` |

### Anotado, sin arreglar

- **NinjaTrader de Roberto está en ESPAÑOL.** Los logs son `log.AAAAMMDD.NNNNN.es.txt`, no `.en.txt`
  — me costó un diagnóstico fallido el 26-ago — y el feed se llama `Trasmisión de datos simulados`.
  Auditoría rápida del beta-kit: `docs/install.md`, `docs/troubleshooting.md` y `docs/uninstall.md`
  citan nombres de UI en inglés (**`NinjaScript Editor`** ×7, `Control Center`, `right-click`) y
  `install.md:141` manda a leer "the Control Center Log". Ninguna herramienta parsea `.en.txt` hoy,
  así que **no hay nada roto en el código** — el problema es de documentación, y afecta a Roberto y
  a cualquier tester no anglófono.
- **La config declara `ledgerPath` y `statePath`, y el addon los ignora**: usa rutas fijas
  (`DeadmanGuardianAddOn.cs:32-33`). La clase de la casa en forma de esquema — claves que afirman
  configurar algo que no configuran. Consecuencia concreta: una sesión de prueba no se puede desviar
  a un ledger aparte (ver `docs/proposals/live-production-breach-test.md`).

### Orden definitivo después de la prueba viva (fijado 2026-08-26)

0. **LT-1** — que sólo se cancele lo que AUMENTA exposición. **Pasó al primer lugar el 26-ago**,
   por delante de cert-1: es el único defecto abierto que cuesta dinero. Rojo primero, y **contra un
   doble que modele el ciclo de vida de la orden** — con el doble actual el verde vuelve a mentir.
1. **cert-1** — acotar el certificado al día que nombra. Mecánico, con maquinaria que ya existe.
2. **Contrato de extensión del formato, con Ventana B** — ¿el verificador tolera campos desconocidos
   en un evento conocido, y está escrito? Si la respuesta es que no hay tolerancia, **eso es un
   hallazgo mayor que el campo** y reordena lo que sigue.
3. **Hash del binario en `GUARDIAN_STARTED`** — sólo después del punto 2. Nombrado por lo que
   establece: *el hash del archivo desde el que se cargó el ensamblado*, no "el código corriendo".
4. **`limitRespected`** — semántica, no alcance. Separado a propósito.

**El `.deploy-pin` quedó DESCARTADO**: con el pin puesto y sin correr el instalador —lo que pasó hoy—
no habría hecho nada. Regla que deja: **un freno cuyo arreglo habitual es "desactivalo" ya dejó de ser
un freno.**

`docs/error_espejo.md` lleva la clasificación completa (M1–M22) con el costo de cada uno. Las
pruebas `M4`–`M7` están **verdes afirmando el defecto**: pasan porque documentan lo que el código
hace hoy. Cada una debe ponerse **roja** cuando llegue su arreglo; una que siga verde después de su
propio arreglo significa que el arreglo no tocó nada. `M1b` y `M13` son contenciones y van verdes
siempre.

---
**Actualizar y commitear este archivo es el último paso del cierre de cada tanda.**
