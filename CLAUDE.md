# deadman-guardian — mapa para sesiones futuras (actualizar al cierre de cada tanda)

Última actualización: 2026-08-29 (LT-4 encontrado; plan de la prueba de conducta de LT-1 escrito).

**ESTADO OPERATIVO AL 29-ago 11:20 CT — verificado, no recordado:**
- **NinjaTrader está ABIERTO** (PID 39112, desde el jue 27-ago 19:55). Guardián vivo y `DISARMED`:
  el sello de $600 expiró el 28-ago 17:00 CT (`SEAL_EXPIRED` seq 7963 → `DAY_CLOSED` → `DISARMED`).
- **`config.json` es el de PRODUCCIÓN ($600)** y **no hay sello vigente**, así que hoy sí se puede
  editar. Respaldo previo: `config.json.produccion-20260826`.
- **Hay una POSICIÓN ABIERTA en Sim101** — `positionsAdopted: 1`, +$18.430 no realizados al último
  checkpoint del viernes. **Es la que LT-1 dejó varada el 26-ago** (32 `FLATTEN_REQUESTED`, 32
  `LOCKOUT_INCOMPLETE`, cero `FLATTEN_VERIFIED`). Cerrarla antes de cualquier prueba: el no realizado
  entra crudo en `DayPnl`, y con +$18k **ningún límite es alcanzable**.
- **Divergencia pendiente**: desplegado `fc8fda6e514bb921` (LT-1), `bin\Release` en
  `104dbca67f19f709` (LT-2). Ver §4, "cuándo la divergencia importa".
- Sin compuertas `.GO`; `soak.GO` sigue aparcado como `soak.GO.parked-for-livetest`.
- 268 tests, 0 fallos. Plan de hoy: `docs/lt1-behaviour-test-plan-20260829.md`.
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
3b. **Antes de reescribir un test que se puso rojo, preguntar si lo que afirma está PROMETIDO en algún
   lado.** Lo normal es reescribir el test y seguir. El 26-ago dos tests rojos resultaron ser una
   **especificación**: `SPEC §9.5` decía por nombre *detect-and-cancel-immediately* y la fila `T7` del
   modelo de amenazas lo publicaba. Reescribirlos en silencio habría dejado el código contradiciendo una
   spec publicada. Un test rojo puede ser un test viejo — o el único lugar donde una promesa sigue viva.
3c. **Un doble de prueba se corrige contra PRODUCCIÓN, no contra la intuición.** El
   `OrderLifecycleBroker` apilaba una orden por aplanado hasta que el log de producción lo desmintió
   (167 `FLATTEN_REQUESTED` contra 6 órdenes `Cerrar`). Cada punto en que un doble difiere de lo real es
   un lugar donde puede esconderse un defecto: o está respaldado por evidencia, o queda escrito como
   simplificación conocida.
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

### Tercer subtipo, y el que decide el tipo del campo

> **UN DEFAULT PLAUSIBLE MIENTE, UNA AUSENCIA DICE LA VERDAD CALLÁNDOSE — y el TIPO del campo decide
> cuál de las dos puede.**

Seis campos del adaptador sólo existían si el proceso estaba presente en un instante dado. Los tres de
referencia pudieron **callarse** (`Messages.Until(null,…)` devuelve `null` y la cláusula desaparece
limpia). Los de valor **no tuvieron opción**: `decimal` → `0.00` impreso como plata, `int` → `0`
impreso como conteo. Un trader leyó *"your limit is $0.00"* con el límite en $40 (LT-2, 26-ago).

**No fue descuido de quien escribió los campos: es una propiedad del lenguaje que nadie miró.**

Y el agravante: **el default plausible es PEOR que uno absurdo.** `-999999` se habría visto el primer
día; `$0.00` es la cifra más creíble que existe, y por eso sobrevivió hasta que un humano la leyó en
el peor momento posible.

**Cero no es ausencia.** `ORDERS_CANCELLED` llevó un conteo real de `0` el 26-ago — no había órdenes en
reposo. *"No se canceló ninguna"* y *"no sé cuántas"* son hechos distintos y tienen que leerse distinto.

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
- **CUÁNDO LA DIVERGENCIA `bin\Release` ≠ desplegado IMPORTA — y cuándo callarse.** Que el build local
  vaya adelantado es **el estado normal** entre un arreglo y su despliegue, no una anomalía. Se
  reporta **sólo** cuando existe *verificación pendiente sobre un binario específico*: hay una
  afirmación de conducta que queremos sostener sobre el binario **X**, y el árbol ya no produce X.
  Ahí la divergencia puede hacer que se verifique lo que no era, y hay que nombrar el estado con esas
  palabras — *"verificación pendiente sobre `fc8fda6e514bb921`"*, no *"bin\Release difiere"*. **El
  resto del tiempo no se menciona.** *Flaguear siempre gasta la atención que hace falta para el caso
  que sí importa*, y un aviso que aparece en cada reporte deja de leerse justo cuando importa.
- El guardián nunca coloca órdenes de entrada. Los bots de prueba (`DeadmanBotA`/`B`) sí envían
  órdenes fillables, sólo en Sim101 verificada por `Provider == Simulator`, y sólo con su archivo
  `.GO` presente.

## 5. Defectos abiertos (verificados, con archivo:línea)
| # | qué | dónde |
|---|---|---|
| ~~LT-1~~ | **ARREGLADO 2026-08-26** (A11). `OnOrderObserved` ya no cancela; el barrido vive en `EnterLockout` y corre una vez. Cuatro tests con un doble que modela el ciclo de vida de la orden. Queda la **opción completa** como tanda propia: interfaz opcional de cancelación selectiva consultada con `as`, y sin ella el comportamiento de hoy, que es permanente. Era: **el guardián cancelaba sus propias órdenes de aplanado y las salidas del trader.** `CancelAllOrders(order.Account)` es incondicional: no mira lado, ni origen, ni si la orden reduce exposición. Probado en vivo el 26-ago: 167 intentos, `FLATTEN_VERIFIED` cero, y `Sell`/`BuyToCover` cancelados. **PRIMERO EN EL MAPA**, por delante de todo: es el único defecto abierto que cuesta dinero y atrapa al trader en una posición. Informe: `docs/live-test-findings-20260826.md` | `Guardian.cs:576` |
| ~~LT-2~~ | **ARREGLADO 2026-08-27** (capa 1). Los tres de config salen de Core (`SealedPersonalDailyLossLimit`, `SealedSessionResetLocalTime`, `SealedSessionResetTimeZone`); los dos de evento son `decimal?`/`int?` y el mensaje **suprime lo ausente y apunta al registro**. Falta la capa 2 — recuperar del ledger los dos de evento — que **viaja con cert-1**, porque exige responder qué entradas pertenecen al día en curso. Era: **la familia de M15 que no barrí.** `_personalLimit`, `_resetLocalTime` y `_zoneId` se asignan **sólo en la ruta de armado** (`:268`); un reinicio que restaura `ARMED` desde el sello los deja en su default. Consecuencia vista: el mensaje del breach publicó **"your limit is $0.00"** con límite $40, y el "until 17:00" desaparece en silencio de todos los mensajes. | `DeadmanGuardianAddOn.cs:50-52,268` |
| **LT-4** | **NUEVO 29-ago, y es la premisa del arreglo de LT-1.** Una vez que el aplanado verifica, `LockoutVerified` queda en `true` y **nada la vuelve a `false` mientras el guardián sigue `Locked`** — los dos call sites del tick están guardados por `!LockoutVerified` (`:240`, `:635`), y las tres asignaciones a `false` son `Arm` (`:478`), `CheckExpiry` (`:851`) y `EnterLockout` (`:886`). **La exposición abierta después del primer aplanado no se cierra nunca.** El comentario de LT-1 dice *"the next cycle's flatten closes it — loss BOUNDED by one cycle"*: **no hay next cycle's flatten; la cota está afirmada, no implementada.** Es **más viejo** que LT-1: lo tapaba el `CancelAllOrders` incondicional que sacamos, mal y a ciegas. *Un chequeo que existe no es un chequeo que corre — y acá el que corría era el defecto.* Confirmado por máquina: `LT4_LockoutStopsEnforcingTests.cs`, 3 verdes afirmando el defecto (convención M4-M7), `LT4c` prueba que la re-entrada **antes** de verificar sí funciona ⇒ el defecto es el pestillo, no el bucle. **El arreglo es decisión de diseño** (re-verificar cada tick, o rearmar los pasos al aparecer una posición) y no se toma en el mismo aliento que el hallazgo. **CONSECUENCIA: `SPEC.md:46` (`T7`) ES FALSA HOY** — dice *"the next cycle closes it"* y *"no position can be built past the lockout"*, y ninguna de las dos se sostiene. **NO se corrige por lectura**: se corrige con la evidencia de la predicción 8 de `docs/lt1-behaviour-test-plan-20260829.md`, en la misma tanda que el arreglo. **Y el arreglo de LT-1 NO se revierte por esto**: antes el guardián *causaba* el daño cancelando las salidas del trader; ahora *falla en prevenir* uno que el trader inicia deliberadamente después de que se le avisó. **Causar es peor que no prevenir** — el intercambio fue a favor, y LT-4 bloquea la beta igual. | `Guardian.cs:240,635,965` |
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
- **La cuenta de los bots está cableada, y es la misma familia** (verificado 29-ago). La REGLA es
  genérica —`BotAccountRule.Decide(accounts, target)` toma el objetivo por parámetro y no nombra
  ninguna cuenta— pero **los cinco llamadores son constantes de compilación**:
  `BotGuardrails.cs:67` (`TargetAccount`, objetivo de BOT A/B), `BotGuardrails.cs:407` (la cuenta que
  vigila el **guardián sandbox** del bot), `SoakSandbox.cs:152`, `DeadmanGuardianSoak.cs:38`,
  `DeadmanGuardianLatencyProbe.cs:35`. **El guardián NO tiene este problema**: `GuardedAccountRule`
  lee sólo el config sellado y rehúsa explícitamente tener un fallback.
  **Por qué importa más que una molestia**: sin una segunda cuenta de simulación, **cada prueba quema
  producción** — el costo que se pagó toda la semana. *No se paga una vez, se paga siempre.*
  **Por qué NO se cambia de apuro**: `TargetAccount` siendo `const` es parte de por qué el riel que
  mantiene a los bots lejos de una cuenta fundeada se puede creer. Volverlo variable **es modificar
  un riel de seguridad** y necesita tanda propia, roja primero, con los casos de negación cubiertos.
  Dato que la hace viable: **cambiar esas constantes no toca `GuardianCore.dll`** — los bots son
  NinjaScript y compilan en `NinjaTrader.Custom.dll`, así que el binario bajo prueba no cambia.

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
