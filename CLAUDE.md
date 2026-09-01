# deadman-guardian — mapa para sesiones futuras (actualizar al cierre de cada tanda)

Última actualización: 2026-08-31 (**la prueba de conducta de LT-1 CORRIDA y pasada; LT-4 arreglado;
el panel reconstruido; todo desplegado**). Bitácora del día: `docs/log-20260831.md`.

**ESTADO OPERATIVO AL 31-ago 19:2x CT — verificado, no recordado:**
- **NinjaTrader ABIERTO y compilado** con todo lo del día. Guardián **`DISARMED`**, sin sello.
  **NO está armado**, y armar es una decisión del operador, nunca inercia de un despliegue.
- **`config.json` es el de PRODUCCIÓN ($600)**, hash `38a15089c889ac09`, restaurado por el §11 tras
  la corrida. Respaldo del día: `config.json.produccion-20260831`.
- **Sim101 en cero**, sin posiciones. La que LT-1 dejó varada el 26-ago se limpió con un reset de la
  cuenta (08:06 del 31-ago) — NT8 lo registró: *"Cuenta de simulacion ''Sim101'' restablecida"*.
- **Desplegado `12fff6f6c76d838c`** (`GuardianCore.dll`) más los 8 `.cs`, los 8 idénticos al repo.
  **Sin divergencia pendiente.**
- Sin compuertas `.GO`. `soak.GO` está como **`soak.GO.parked-pending-assertion-decision`**: su
  aserción exige `ORDER_REJECTED_LOCKED` y una orden muerta, y desde LT-1 **no ocurre ninguna de las
  dos**. Reponerla daría un FAIL conocido disfrazado de regresión. **Decidir qué prueba antes.**
- **304 tests, 0 fallos.** Ledger: 8.023 entradas, cadena íntegra (verificada tras un corte de luz).
- **Pendiente de confirmación humana**: los tres gestos del panel nuevo — que el chevron se vea, que
  el botón siga visible colapsado, y que la franja no se abra sola. El add-on carga limpio, y eso no
  prueba nada sobre el botón.
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
3d. **UN PLAN TIENE FECHA DE ESCRITURA, Y CADA PASO HEREDA EL CONOCIMIENTO DE ESA FECHA.**
   Ejecutarlo después es ejecutar decisiones tomadas con menos información. La pregunta no es sólo
   si la secuencia es coherente — es **si cada paso sigue siendo la decisión que tomarías HOY**.
   Nació el 31-ago: el §11 del plan del sábado mandaba reponer `soak.GO`; doce horas después se
   decidió que la aserción del soak necesitaba resolverse antes, y el paso se ejecutó igual.
   **LA MECÁNICA QUE LO PREVIENE, y vale más que la regla**: *una decisión que deja sin efecto un
   paso planificado se escribe DONDE VIVE EL PASO, no sólo donde se tomó la decisión.* Ese día la
   decisión quedó en la conversación y en la cola de este archivo; el paso vivía en el §11 del plan;
   los dos lugares no se conocían y **el que ejecutaba era el segundo**. Buscar el paso y anotarlo
   en su destino cuesta treinta segundos y es mecánico. **La regla te pide acordarte; la mecánica no
   te pide nada.** Aplicada en su propio caso: `docs/lt1-behaviour-test-plan-20260829.md` §11 lleva
   el paso 12.6 tachado con el motivo, en el paso mismo.
4. **Commits separados por fix, por ruta explícita. Nunca `git add -A`.**
5. **Una sola sesión por repositorio.** Esta ventana es la de `deadman-guardian`; no toca `deadman`
   (Ventana B) ni `deadman-research` (exclusivo de Roberto, ni siquiera lectura).
   **EXCEPCIÓN DE LECTURA sobre `deadman`, concedida el 1-sep, y su motivo es lo que la hace regla y
   no permiso**: el operador se declaró **el eslabón con pérdida** — en un día introdujo tres errores
   relevando hechos entre ventanas (*"confirmado desde los dos lados"*, un cero repetido sin
   preguntar qué ventana cubría, y números de sección de un repo citados en otro).
   ⇒ **los HECHOS se leen de la fuente; las DECISIONES pasan por el operador.**
   Tres condiciones, sin excepción: **(a)** sólo lectura — nunca un commit, nunca una edición, ni
   para corregir un error obvio; **(b)** **se nombra el commit leído** — un documento citado sin su
   versión es una cita sin fecha, y ya costó caro hoy; **(c)** lo que se encuentre mal del otro lado
   **se le reporta al operador**, no se arregla y **no se le dice directo a Ventana B**: la
   coordinación sigue pasando por ahí, que es donde sí hace falta.
   La misma excepción, simétrica, la tiene Ventana B sobre este repositorio.
6. Cada reporte abre identificando la ventana.
7. No declarar "arreglado" sin salida de prueba real. Reportar los fallos tal cual.
8. **Herramientas: escribir los scripts de parche A ARCHIVO y correrlos.** La regla es una y las
   puertas por las que entra el problema son al menos dos:
   - los **heredocs** destrozan los backslashes de rutas Windows (`nt\addon` llegó como BEL,
     `nt\bots` como backspace);
   - los **backticks dentro de comillas dobles de bash** son sustitución de comandos. El 31-ago
     una fila de este mismo archivo se escribió con `python -c "..."` y cada identificador
     entre backticks —`dayKey`, `DAY_OPENED`, `limitRespected`— se ejecutó como comando y quedó
     **reemplazado por nada**. La fila salió con huecos justo donde iban sus términos técnicos.
   **Ninguna de las dos ocurre si el script vive en un archivo**, que es la regla que ya estaba
   escrita y que no seguí por tratarse de una sola línea.
9. **ANTE UN FRENO, LA PREGUNTA NO ES «¿FUNCIONA?» SINO «¿EXISTE UN INPUT ALCANZABLE QUE LO HAGA
   DECIR QUE NO?»** — y después, por separado: **¿alguna vez lo dijo?** Traída de ALAYA el 1-sep,
   donde un freno con doce criterios alimentados por constantes escritas a mano acumuló **180.688
   evaluaciones y cero rechazos** mientras el motor firmaba que había validado. **Un freno que no
   puede fallar no es un freno, es una firma.** Auditoría completa de este repo:
   `docs/auditoria-frenos-20260901.md`.
   **El refinamiento que salió de aplicarla acá, y es lo que hay que recordar:**
   > **La distancia entre un cable trampa y una firma NO es la alcanzabilidad: es si algo AFIRMA que
   > te protegió.**
   `FOREIGN_ACCOUNT_ORDER_OBSERVED` (`Guardian.cs:605`) **es inalcanzable** —el addon se suscribe
   sólo a la cuenta guardada (`DeadmanGuardianAddOn.cs:248-259`)—, cuesta cero, el propio código se
   declara cable trampa (*"if a foreign order ever reaches this method the wiring changed underneath
   us"*) y **ningún mensaje, documento ni certificado lo cita**: está bien que exista. El OCG de
   ALAYA era inalcanzable **y el motor firmaba con él**. **El daño no está en la puerta cerrada;
   está en la firma.**
   *(La regla numerada que esto corrige vive en el mapa de ALAYA, no en éste — acá no hay regla 18.)*
10. **DOS FUENTES DE CALIDAD DISTINTA NO SE SUMAN** (regla de Ventana B, adoptada el 1-sep; aplica a
   las tres ventanas por igual). **Una medición más una afirmación no dan una confirmación: el
   resultado hereda la calidad de la peor mitad, y la palabra "confirmado" hace exactamente lo
   contrario** — presenta como corroborado lo que se apoya en el eslabón que nadie midió.
   **LA SEÑAL DE ALARMA ES LA CONVENIENCIA.** Lo traicionero es que **la mitad medida es verdadera**,
   así que todo lo comprobable comprueba, y **lo que no aguanta es justo la mitad que aportaba la
   urgencia**. ⇒ **la afirmación que asciende un hallazgo a urgencia, que desbloquea, o que genera un
   paso que a uno le toca hacer, se relee con la procedencia delante.**
   Procedencia de la regla, dicha por quien la escribió: **el operador** combinó una afirmación de
   esta ventana con una medición de Ventana B y lo llamó *"confirmado desde los dos lados"*.
   **La regla sale de ese error, no del de las ventanas.** El caso completo vive en el `CLAUDE.md` del
   repo `deadman`; acá va la regla con referencia cruzada, no el caso.
   **Ejemplo propio del mismo día, por si hiciera falta uno de esta casa**: mi afirmación de que
   `session.timezone` salía `""` *"desde estado ordinario"* se combinó con la medición correcta de
   Ventana B de que `""` da `exit 1`. La medición era verdadera; **la mitad que producía la urgencia
   era mía y era falsa** — y era la que generaba el trabajo.
11. **UNA CITA DE LÍNEA ES EL HASH DE UN MOMENTO** (1-sep, encontrada por Ventana B leyendo este repo).
   **DECISIÓN: se cita por NOMBRE DE SÍMBOLO** —función, campo, constante, mensaje literal—, que
   sobrevive al movimiento. **El número de línea es acompañante opcional y NUNCA va solo**, y quien lo
   lea debe saber que **se pudre**.
   El caso: este archivo citaba `Certificate.cs:380` y `:363` para los dos `keyId`; el mismo día ya
   eran `:427` y `:410`, **las dos corridas exactamente 47 líneas**. Como el desplazamiento fue
   idéntico, **la relación «a 17 líneas» sobrevivió intacta y la cita se seguía leyendo correcta** —
   una afirmación cierta sobre el conjunto equivocado (§3), detectable sólo abriendo el archivo.
   **Y no es un caso: es una propiedad del formato**, porque este mapa está construido sobre
   `archivo:línea`. Las citas viejas **se dejan** —reescribirlas todas hoy es peor uso del tiempo que
   arreglar lo que denuncian— pero **quedan declaradas perecederas**: un número que no coincide es
   deriva, no mentira, y **la referencia buena es siempre el nombre**.

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

**Y desde el 1-sep la clase tiene su cara opuesta, que no habíamos visto**: un documento también
puede afirmar **de MENOS**. En ALAYA, un freno que no podía fallar firmaba que había protegido; acá,
un freno que **sí actuó** no aparece en el certificado (§5, ítem 4). **Los dos producen un documento
que no coincide con la realidad** — uno afirma de más, el otro de menos. **La regla se lee en las dos
direcciones: ni una palabra más, y ninguna menos.**

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

### Cuarto subtipo, y éste apunta a la VERIFICACIÓN misma (31-ago)

> **UN TEST ESCRITO PARA COMPROBAR TU PROPIA DEFINICIÓN HEREDA TU DEFINICIÓN. Los únicos que pueden
> contradecirte son los que se escribieron SIN tu idea en la cabeza.**

El caso: el acotado por día de cert-1. `Cert1_DayScopeTests` lo escribí yo, sabiendo qué buscaba —
**no podía sorprenderme, porque compartía mi error**. Los cuatro rojos salieron de
`C_CertificateEmitterTests`, que estaba ahí por hash, JSON canónico, limitaciones y render HTML,
**sin ningún interés en el alcance del día**: sus fixturas arman **lo ordinario** —una jornada que
termina donde termina el registro— y justamente por eso pudieron decirme que no.

**El corolario, que es lo accionable: después de cambiar una DEFINICIÓN, el veredicto NO está en los
tests que escribiste para ella. Está en lo que se rompe entre los que ya existían por otros motivos.**

Y si ahí no se rompe nada, hay **dos** explicaciones y no son la misma: **o la definición es
correcta, o nadie la estaba ejercitando.** Averiguar cuál es parte del trabajo — el verde de la
segunda es indistinguible del de la primera hasta que alguien mira quién tocaba ese código.

Es hermana del segundo subtipo y apunta a otro lado: **aquél dice que tu MODELO DEL MUNDO puede
estar mal; éste dice que tu VERIFICACIÓN puede compartir tu error.**

### Quinto subtipo, y éste no es sobre lo que AFIRMÁS sino sobre lo que ARREGLÁS (31-ago)

> **UN ARREGLO POR CANAL DEJA LA FAMILIA ABIERTA EN TODOS LOS CANALES QUE NO MIRASTE.**

LT-2 son los campos de referencia que un reinicio deja sin poblar. Arreglamos los **mensajes** —los
tres de config salen de Core desde el 27-ago— y dimos la familia por cerrada. **Estaba cerrada en un
canal.** El campo seguía sin poblar y el 31-ago apareció por otra salida: `session.timezone: ""` en
un certificado (`Certificate.cs:456`). **Y encontró la peor de todas, porque el certificado es el
único artefacto que va a un tercero.**

Es hermana de la regla del canal del mismo día y apunta al otro lado: **aquélla dice que un MENSAJE
puede no llegar; ésta dice que un ARREGLO puede no llegar a todas partes** — y las dos por el mismo
motivo: **nadie enumeró las salidas.**

**Pregunta de cacería, antes de dar una familia por cerrada:** *¿por cuántas salidas puede aparecer
este dato, y las miré a todas?* Un arreglo que no puede nombrar sus canales es un arreglo de uno.

### La cara sin texto de la clase madre, y es la única que GENERA TRABAJO (1-sep)

> **UN GUARDIA SOBRE UN CASO IMPOSIBLE NO ES INOFENSIVO: ES UNA AFIRMACIÓN TÁCITA DE QUE EL CASO
> EXISTE.**

Hasta acá la clase era **un TEXTO que afirma más de lo que su código comprobó**. Ésta es **el CÓDIGO
afirmando, sin una sola palabra**, y el destinatario no es el usuario: **es el próximo que lo lea.**

**Y es peor en una dimensión propia: una frase falsa cuesta confianza; un guardia falso cuesta
TRABAJO.** El `?? ""` de `session.timezone` nos hizo armar un plan a dos ventanas, le dio a Ventana B
un paso 0 que no existía, y difundió una urgencia inventada. **Ninguna de las frases que arreglamos
el 31-ago costó eso.**

**La forma accionable, y el test es la distinción entre imposible y no-cableado:**

> **Al escribir `?? default`, preguntá si la rama es ALCANZABLE.**
> - **Imposible por ESQUEMA** ⇒ el guardia es una afirmación falsa: **se borra el `??`**, no se le
>   cambia el valor. Cambiarlo a `null` conserva la mentira con mejor ortografía.
> - **Inalcanzable sólo por CABLEADO** ⇒ es **preparación legítima y el `??` se queda** —borrarlo lo
>   rompe el día que alguien conecte el llamador— **pero tiene que DECIRLO en una nota.** Sin la
>   nota, el próximo lector repite nuestro error.

Aplicado en `Certificate.cs`: la zona perdió el `??` (imposible por `RequiredKeys`); `keyId`, los dos
de `gaps` y los tres de `anchors` lo conservan **con la nota de preparación declarada**.

### La conclusión correcta con la razón falsa, que es MÁS frágil que equivocarse (1-sep)

> **UNA CONCLUSIÓN CORRECTA APOYADA EN UNA RAZÓN FALSA ES MÁS FRÁGIL QUE UNA CONCLUSIÓN EQUIVOCADA:
> el día que alguien corrija la razón, se lleva puesta la conclusión.**

Una conclusión equivocada la tumba el primero que la mira. Ésta **aguanta todas las revisiones**
—porque la conclusión es correcta— **hasta que alguien hace lo correcto con la razón**, y entonces
cae algo que estaba bien.

Caso: clasifiqué `signature.keyId` como ALCANCE *"porque habilita la verificación de la firma"*.
**Ventana B midió que su verificador no lee ese campo ni una vez**: lo que habilita la verificación
es `--pubkey`, que aporta el receptor. La conclusión —**rehusar** en vez de vaciar— **es correcta**, y
la sostiene mejor una frase que ya estaba en el mismo archivo: *"an unsigned certificate is honest, a
half-signed one is not"*. **Una firma que no nombra su clave es media firma.**

**Corolario operativo**: cuando una conclusión se apoye en una razón que no medimos nosotros,
**buscarle una razón que viva en el mismo repositorio**. La de al lado suele ser mejor y no depende de
nadie.

### Y el lugar donde la mentira entra mejor disfrazada que en ningún otro (1-sep)

> **UNA LIMITACIÓN QUE CONSUELA ES EL DEFECTO DE LA CASA VESTIDO DE HONESTIDAD.**

Una sección de limitaciones es **donde más tienta suavizar**, y por un motivo que hay que nombrar:
**uno ya está siendo honesto, y siente que se ganó el derecho a tranquilizar.** *"Esto no prueba X,
pero quedate tranquilo que…"* — la segunda mitad se cuela apoyada en la credibilidad que le dio la
primera. Es la misma mecánica que la fila no verificada dentro de la tabla verificada.

**Contención, ya cableada** (`Cert3d_the_limitation_neither_reassures_nor_promises`): una limitación
**no afirma que lo no medido no ocurrió** y **no promete el arreglo**. Prohibidas por test:
*"no tampering"*, *"was not tampered"*, *"nobody tried"*, *"will be fixed"*, *"soon"*,
*"in a future version"*, *"safe"*, *"you can be sure"*.

**Hermana medida por Ventana B el mismo día**: *un campo impreso dentro de un veredicto hereda la
autoridad del veredicto* — su verificador imprimía `VALID (keyId=key-ALPHA)` sin comprobar nunca ese
`keyId`. Inventario del lado emisor: `docs/cert-authority-borrowing-20260901.md`.

### Sexto subtipo, y es el más difícil de VER de todos (1-sep)

> **UNA COLECCIÓN VACÍA AFIRMA AUSENCIA — y el tipo lista no tiene forma de callarse salvo
> omitiéndose entero.**

Es la familia del `$0.00` en forma de colección, **y es peor que un cero**: un cero en un campo de
dinero llama la atención, alguien lo mira dos veces. **Una lista vacía se lee como «nada que
reportar» y no como «afirmo que no hay nada».**

**El alcance es real y no es futuro**: `continuity.gaps` sale `[]` en los **DOCE certificados ya
emitidos** (`Certificate.cs:433-435`) y **nadie calcula huecos** — no hay una sola asignación a
`Gaps` en `src/` ni en `nt/`. Los doce afirman *"no hay huecos"* donde el hecho es *"nadie miró"*.
Igual `anchors`. *"No hay"* y *"no sé"* no son lo mismo, y la lista vacía dice el primero.

Y el **tercer** subtipo explica por qué éste se escapa: allá **el TIPO decidía** si el campo podía
callarse —`decimal` no podía, `string` sí—. **Acá el tipo lista tampoco puede**: `[]` es un valor
legítimo con significado propio. Las únicas ausencias honestas son **omitir el contenedor entero** o
**decir en `limitations` qué significa ese vacío**, como ya se hace con `ordersRejectedWhileLocked`.

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
| **LT-2 — PARCIAL** (era `~~LT-2~~`; **des-tachado el 31-ago**: un tachado se lee de un vistazo como cerrado, y la familia está cerrada en un canal) | **capa 1 ARREGLADA 2026-08-27**. Los tres de config salen de Core (`SealedPersonalDailyLossLimit`, `SealedSessionResetLocalTime`, `SealedSessionResetTimeZone`); los dos de evento son `decimal?`/`int?` y el mensaje **suprime lo ausente y apunta al registro**. Falta la capa 2 — recuperar del ledger los dos de evento — que **viaja con cert-1**, porque exige responder qué entradas pertenecen al día en curso. Era: **la familia de M15 que no barrí.** `_personalLimit`, `_resetLocalTime` y `_zoneId` se asignan **sólo en la ruta de armado** (`:268`); un reinicio que restaura `ARMED` desde el sello los deja en su default. Consecuencia vista: el mensaje del breach publicó **"your limit is $0.00"** con límite $40, y el "until 17:00" desaparece en silencio de todos los mensajes. **SE CERRÓ EN UN CANAL, NO EN EL HECHO (31-ago)**: el mismo snapshot sin `sessionResetTimeZone` sale hoy como `session.timezone: ""` en un certificado (§5, el `?? ""` del emisor). Arreglamos los MENSAJES y dimos por arreglada la CAUSA; el campo sin poblar sigue ahí y encontró otra salida. **Un arreglo por canal deja la familia abierta en todos los canales que no miraste** — y el certificado es el canal que va a un tercero. | `DeadmanGuardianAddOn.cs:50-52,268` · `Certificate.cs:456` |
| **LT-4** | **PRIMERO EN LA COLA, POR DELANTE DE cert-1 (elevado el 31-ago).** Ya no es "el guardián deja de aplicar": es **deja de aplicar habiéndolo negado por escrito**. El 31-ago a las 09:10:30, un segundo después del `FLATTEN_VERIFIED` —o sea, en el instante exacto en que `LockoutVerified` queda en `true`— el producto le dijo a una persona real, textual, en el log de NT8: *"**Any new order will be cancelled** until 17:00 (America/Chicago). This is what you asked for."* **Esa frase es falsa**, y no por lectura: `LT4d` lo prueba. La enumeración que la sostiene es chica y por lo tanto exhaustiva — `CancelAllOrders` tiene **un** call site (`Guardian.cs:916`, en `SweepRestingOrders`), `SweepRestingOrders` **un** llamador (`EnterLockout`), y la ruta de breach de `EnterLockout` (`:747`) es **inalcanzable estando `Locked`** porque el tick retorna en `:633-638`. Es la **primera clase de defecto de la casa alcanzada a través de la segunda**, y en el peor lugar posible: el único artefacto que un humano lee, en el único momento para el que el producto existe. **Por eso el arreglo no puede esperar a que exista una forma elegante de medirlo**: mientras tanto el producto afirma algo que sabemos falso, cada vez que actúa. Era: **NUEVO 29-ago, y es la premisa del arreglo de LT-1.** Una vez que el aplanado verifica, `LockoutVerified` queda en `true` y **nada la vuelve a `false` mientras el guardián sigue `Locked`** — los dos call sites del tick están guardados por `!LockoutVerified` (`:240`, `:635`), y las tres asignaciones a `false` son `Arm` (`:478`), `CheckExpiry` (`:851`) y `EnterLockout` (`:886`). **La exposición abierta después del primer aplanado no se cierra nunca.** El comentario de LT-1 dice *"the next cycle's flatten closes it — loss BOUNDED by one cycle"*: **no hay next cycle's flatten; la cota está afirmada, no implementada.** Es **más viejo** que LT-1: lo tapaba el `CancelAllOrders` incondicional que sacamos, mal y a ciegas. *Un chequeo que existe no es un chequeo que corre — y acá el que corría era el defecto.* Confirmado por máquina: `LT4_LockoutStopsEnforcingTests.cs`, 3 verdes afirmando el defecto (convención M4-M7), `LT4c` prueba que la re-entrada **antes** de verificar sí funciona ⇒ el defecto es el pestillo, no el bucle. **El arreglo es decisión de diseño** (re-verificar cada tick, o rearmar los pasos al aparecer una posición) y no se toma en el mismo aliento que el hallazgo. **CONSECUENCIA: `SPEC.md:46` (`T7`) ES FALSA HOY** — dice *"the next cycle closes it"* y *"no position can be built past the lockout"*, y ninguna de las dos se sostiene. **NO se corrige por lectura**: se corrige con la evidencia de la predicción 8 de `docs/lt1-behaviour-test-plan-20260829.md`, en la misma tanda que el arreglo. **Y el arreglo de LT-1 NO se revierte por esto**: antes el guardián *causaba* el daño cancelando las salidas del trader; ahora *falla en prevenir* uno que el trader inicia deliberadamente después de que se le avisó. **Causar es peor que no prevenir** — el intercambio fue a favor, y LT-4 bloquea la beta igual. | `Guardian.cs:240,635,965` |
| LT-3 | Los dos mensajes del lockout no contemplan *"la promesa no se puede cumplir"*: `LockoutComplete` cuelga de `FLATTEN_VERIFIED`, así que el trader leyó *"I am about to close your positions"* y después nada, 167 vueltas. Correcto por diseño, malo en consecuencia. | `DeadmanGuardianAddOn.cs:427-434` |
| ~~cert-1 + cert-3~~ | **ARREGLADOS 2026-08-31.** El certificado se acota al día que nombra. **La definición**: el día EMPIEZA en la primera entrada que lleva su `dayKey`, y TERMINA donde empieza el día siguiente — o al final del registro si no hay siguiente. Derivable por cualquiera con una pasada, sin confiar en quien emite, y funciona sobre ledgers ya escritos. **NO es «entre `DAY_OPENED` y `DAY_CLOSED`»**: `DAY_OPENED` se escribe ÚLTIMO en el armado, así que esa ventana excluiría el `ARMED` que fija el límite, la afirmación más importante del documento. `daysCovered` se **cuenta** ahora (dayKeys distintos dentro del span) ⇒ verdad por construcción; diría 2 si el span se filtrara, donde un 1 cableado era mentira. Un día que el ledger no delimita se **rehúsa** (`CERT_DAY_NOT_IN_LEDGER`). **El primer borrador estuvo mal y lo atraparon cuatro C-tests existentes**: `[min,max]` sobre entradas con `dayKey` termina en `DAY_OPENED` en un día todavía ABIERTO — que es justo el que un trader exporta a las 16:55. La regla 3b funcionando: los rojos afirmaban algo real. **Y el caso que lo rompía era el MÁS COMÚN, no el raro**: un día abierto es el estado del ledger durante toda la sesión, no un borde. Los cuatro rojos **no** salieron de `Cert1_DayScopeTests` —ésos los escribí yo, sabiendo lo que buscaba— sino de `C_CertificateEmitterTests`, escrito para otra cosa por completo (hash, JSON canónico, limitaciones, render HTML), cuyas fixturas arman **lo ordinario** —una jornada que termina donde termina el registro— sin ninguna intención de probar el alcance del día. **Por eso pudieron contradecirme: nadie las escribió con mi definición en la cabeza.** La suite ganándose el sueldo sobre un cambio que la tocaba **de costado**. **Candidato 4 no muerde**: el certificado no calcula NINGÚN P&L — `Certificate.cs` no menciona `PNL_CHECKPOINT` ni una vez — y eso queda como el motivo escrito para NO agregar una cifra. **Candidato 6 resuelto en `limitations`**: el campo se queda (quitarlo cambiaría la forma del documento para un verificador que no es nuestro) y el certificado dice qué significa ese cero. **Hueco conocido y nombrado**: `CONFIG_LOADED` precede a `ARMED` sin `dayKey`, así que el `configHash` queda afuera; cerrarlo es un campo aditivo ⇒ contrato de extensión. **`limitRespected` NO se tocó**: es semántica y decisión de producto; meterla acá habría escondido una decisión de producto dentro de un arreglo mecánico. Tests: `Cert1_DayScopeTests`. | `Certificate.cs` |
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
  **HAY UNA PREGUNTA ANTERIOR, Y VA PRIMERO** (operador, 1-sep): hoy la ruta fija garantiza **UNA
  SOLA CADENA CONTINUA**. Si se honrara `ledgerPath`, un trader podría armar cada día con una ruta
  distinta y tener **un ledger nuevo sin historia**. **Esa propiedad la está protegiendo el defecto,
  por accidente, y nadie la declaró.** ⇒ primero: *¿queremos que el ledger sea una cadena continua
  única?* Si sí, **honrar la ruta necesita una restricción, no un arreglo**. Misma forma que
  `TargetAccount` cableado: un límite que parece descuido y resulta ser garantía.
  **Y el dato que lo vuelve urgente por el otro lado** (medido el 1-sep en el sello de producción):
  las dos claves **están DENTRO del snapshot sellado**. O sea que hoy **no hacen nada y sin embargo
  están selladas**: un beta tester que las configure y después las corrija recibe un
  **`CONFIG_TAMPERED` y un bloqueo por editar un campo inerte**. Eso no es protección accidental —
  **es un castigo por tocar algo que no sirve.**
- **`?? ""` EN EL EMISOR — la cadena vacía es el relleno de esta casa, no `"unknown"`** (verificado
  31-ago a pedido de Ventana B, **no arreglado**). `Certificate.cs` **no contiene la cadena
  `"unknown"` ni una vez** — cert-1 no la escribió y no la había antes; las dos de `Guardian.cs`
  (`:455`, `:1100`) viven **dentro de frases** y **no llegan a un campo del certificado**, porque
  `reasons` lleva nombres de evento con conteo y `Certificate.cs:216-217` fija el trigger
  posicionalmente, *"never inferred from the text of `reason`"*. Lo que sí hay son **SIETE `?? ""`**
  —**corregido el 1-sep: dije seis y son siete**, `gaps` y `anchors` aportan cinco, no cuatro— y
  **NINGUNO dispara — corregido el 1-sep, y la afirmación anterior era mía y era falsa.** Dije que
  `session.timezone` salía `""` *"desde estado ordinario del producto, la familia de LT-2"*. **No.**
  `sessionResetTimeZone` está en `GuardianConfig.RequiredKeys` (`:18-22`) ⇒ un config que no lo trae
  **no parsea y nunca llega a ser un sello**, y el emisor lee la zona del **snapshot sellado**, que es
  la misma fuente que Core usa tras un reinicio: **en este canal nunca hubo hueco de plomería**.
  Medición que lo confirma por otro camino: los **6 certificados emitidos traen `America/Chicago`**,
  ninguno vacío. **ARREGLADO IGUAL el 1-sep (cert-2), y no por el vacío sino por lo que el vacío
  tapaba**: `Issue` ahora **rehúsa** —`CERT_TIMEZONE_MISSING`, `CERT_SNAPSHOT_UNPARSEABLE`— en vez de
  emitir un documento con la zona en blanco **y `accounts: []`**, que es la sexta subclase de §3
  afirmando que el trader no guardaba ninguna cuenta. Tests: `Cert2_SealedSnapshotTests` (6, cuatro
  rojos primero). **No desplegado.** **Los otros seis `?? ""` son INALCANZABLES hoy**
  (verificado 1-sep): nada en `src/` ni en `nt/` asigna `Gaps`, `Anchors` ni `KeyId`, así que las dos
  estructuras salen siempre vacías y el bloque `signature` no se emite nunca.
  **PERO NO POR EL MISMO MOTIVO, Y LA DIFERENCIA ES EL HALLAZGO** (operador, 1-sep):
  **`session.timezone` era el único IMPOSIBLE POR ESQUEMA** — `sessionResetTimeZone` es clave
  requerida y se valida al parsear, así que **no existe un sello sin ella**. Su `?? ""` guardaba un
  caso que no puede ocurrir: **no falla nunca, y le hace creer al lector que el caso puede pasar.**
  **Eso es exactamente lo que nos hizo creer a Ventana B y a mí** — dos personas leyeron el `?? ""`
  como evidencia de que había una ruta viva, y armaron trabajo encima. **Un guardia sobre un caso
  imposible no es inofensivo: es una afirmación tácita de que el caso existe.**
  **Los otros seis NO están en esa situación**: son **inalcanzables por CABLEADO, no imposibles por
  esquema**. `Gaps`, `Anchors` y `KeyId` los provee el llamador y sus campos son libres ⇒ **el día
  que alguien cablee un firmante o calcule huecos, el nulo es posible**. No son decoración: son
  manejo prematuro y con la forma equivocada (ver la clasificación de abajo).
  **Y `keyId` tiene la contradicción escrita en el mismo archivo, a 17 líneas**: en `BuildDocument`,
  `issuer.keyId` **se omite** cuando viene vacío, y en el bloque `signature` de `Issue`
  `signature.keyId` **lo vacía**. *(Citaba `:380` y `:363`; el 1-sep estaban en `:427` y `:410` —
  las dos corridas exactamente 47 líneas, ver la regla 11. Se citan por símbolo desde ahora.)*
  El mismo campo del mismo request, tratado de dos maneras opuestas en un bloque y en el otro.
  **CLASIFICACIÓN pedida por el operador el 1-sep, con el criterio de la librería** (*se omite un
  campo que lleva un VALOR, nunca uno que lleva un ALCANCE: omitir una clave de alcance APAGA un
  chequeo en silencio*): **seis de los siete son ALCANCE y uno solo es VALOR.**
  `session.timezone` **es alcance** —declara el marco en el que `dayKey` significa algo, y es el
  vecino literal de la clave que allá midieron—; **`signature.keyId` se rehúsa, y la RAZÓN cambió el
  1-sep sin que cambiara la conclusión**: escribí *"porque habilita la verificación de la firma"* y
  **eso es falso** — Ventana B midió que su verificador **no lee ese campo ni una vez**; la
  verificación la habilita `--pubkey`, que aporta el receptor. **La razón que sí la sostiene está en
  el propio archivo**, en el `CertificateResult.Refused` del camino de firma: *"an unsigned
  certificate is honest, a half-signed one is not"* — **una firma que no nombra su clave es media
  firma**, y este archivo ya se niega a emitir media firma unas líneas antes ⇒ **rehusar**, no omitir
  ni vaciar; `gaps[].dayKey` y los tres de `anchors` **son alcance**
  ⇒ un elemento que no puede nombrar su día o lo que ancla **no se emite**. **El único VALOR es
  `gaps[].reason`**, prosa que explica un hueco: omitirlo saca una explicación, no un chequeo.
  **Y la duda del operador sobre `gaps` es correcta un nivel más arriba**: el que lleva el alcance es
  el CONTENEDOR. `gaps: []` afirma *"no hay huecos"* en los 12 certificados ya emitidos **y nadie los
  calcula** — *"no hay"* contra *"no sé"*, exactamente, y pasando hoy, no el día que alguien llene el
  arreglo (`docs/auditoria-frenos-20260901.md` §4.4). **Es el tercer subtipo de §3 con otra ropa**: un
  campo presente con un valor que no vale nada, donde el TIPO permitía callarse. **No lo cambié a
  propósito**: omitir una clave cambia la forma del documento para un verificador que no es nuestro.
  **DEPENDENCIA EXPLÍCITA: bloqueado por el punto 2 de la cola — el contrato de extensión con
  Ventana B.** No se toca antes de esa decisión, y cuando se tome, se toca en la misma tanda.
  **Y lo que dice de LT-2 (31-ago, del operador)**: `session.timezone` saliendo `""` **es LT-2
  exacto, saliendo ahora por un certificado en vez de por una ventana**. La familia no estaba
  cerrada: **estaba cerrada EN UN CANAL.** Arreglamos los mensajes y dimos por arreglado el hecho.
  **Contraejemplo dentro del mismo archivo, y es lo que convierte esto en decisión y no en tarea**:
  `triggerEvent` ya se **omite** si es nulo (`:417`). **La capacidad existe y se usó en un campo y
  no en los otros seis** ⇒ no es una limitación técnica, es una **inconsistencia**, y se decide
  **de una vez para los siete**, no campo por campo.
- **AVISO A VENTANA B — `ACCOUNT_UNKNOWN` es un valor LEGÍTIMO, no relleno** (31-ago). Es el nombre
  de evento `Ledger.cs:28`, y llega al certificado **como valor** en `triggerEvent` y como clave en
  `reasons`. Si `DECORATIVE_FILLER` se compara **sin distinguir mayúsculas o por subcadena**, el
  primer certificado real con un episodio de desconexión **falla la verificación** — y las
  desconexiones son **M6**, la única condición que esta máquina sí produce, varias veces por semana.
  La comparación tiene que ser **exacta, sensible a mayúsculas y sobre valores**.
- **CANDIDATO 11 — `NinjaTrader.Adapter.AccountLockoutNotifications`** (anotado 31-ago, **no
  perseguido**). Tipo **público** de `NinjaTrader.Core.dll`, con evento `Push` y método `Raise`.
  Apareció en el barrido de sonido fuera de proceso. Un tipo de NT8 con ese nombre exacto está
  demasiado cerca de lo que hace este producto para mirarlo con un sello corriendo. En el mismo
  barrido apareció `Globals.RmsOptions` (Risk Management System); mismo criterio.
- **REGLA DE FRONTERA — ¿esto necesita la PLATAFORMA, o sólo Windows / el disco?** (31-ago, tras
  fallar tres veces el mismo día). Dije que hacía falta esperar a NT8 para: verificar
  `ShowInTaskbar` con `ToolWindow` (**era WPF puro**: dos ventanas de sonda lo contestaron en tres
  segundos) y correr el escaneo de tipos (**`STEP3_FINDINGS §4` ya lo había hecho fuera de
  proceso**, por reflexión sobre el disco). Lo que de verdad exige estar dentro de NT8 es sólo la
  conducta en runtime: si un evento **dispara**, si NT8 **suprime** algo, valores vivos, y si un
  sonido **se oye**. Todo *"¿qué existe y con qué firma?"* se contesta desde el disco.
  **La pregunta se hace ANTES de decir que algo espera**, no después.
- **CANDIDATO — EL LEDGER AUDITA LOS HECHOS DEL GUARDIÁN, NO SUS PALABRAS** (31-ago; formulación de
  Roberto). Los dos mensajes del lockout del 31-ago existen **completos y textuales** — pero en
  `log.20260831.00001.es.txt`, el log de NinjaTrader. Buscados en el **ledger**: 0 coincidencias. En
  `adapter.log`: 0. Están registrados **exactamente en el único lugar que no es la evidencia**: texto
  plano, rotado por NT8, sin cadena, sin firma, borrable, y fuera del alcance del certificado.
  > *Nuestras dos clases centrales son "un texto que afirma más de lo que su código comprobó" y "el
  > guardián nunca actúa con una premisa que no pudo verificar". **El ledger ve la segunda.** La
  > primera —la que más veces cazamos, la que produjo LT-2, `limitRespected` y ahora LT-4— es
  > **estructuralmente invisible para nuestro propio rastro de auditoría**. Cada defecto de palabras
  > que arreglamos es inverificable después del hecho desde el artefacto que existe para verificar.*

  Consecuencia concreta: un auditor con el certificado y el ledger **no puede reconstruir qué leyó el
  trader**, que es la mitad del propósito del producto. Emparentado con LT-3.
- **CANDIDATO — la serie de checkpoints de un día que termina en lockout cierra en `0.00`**
  (observado 31-ago). Estando `Locked`, el tick retorna antes del checkpoint
  (`Guardian.cs:633-638`), así que **no se escribe ninguno más ese día**. Verificado en la corrida:
  el último `PNL_CHECKPOINT` del 31-ago es el de las 14:06:21 con `dayLoss 0.00` y
  `gross {"Sim101":"0.00"}`; el día realizó **−$40,00** cuatro minutos después. Lo mismo vale para
  `FailClosed`, que también retorna antes.
  **El ledger NO miente**: `LIMIT_BREACHED` lleva la cifra real (`dayLoss 40.00`). Lo que engaña es
  que **la serie que *parece* el registro del día** termina en cero.
  **Alcance real, y hay que decirlo porque acota el candidato**: quien la consume es
  `LoadSameDayCheckpointGross`, y no se encontró daño alcanzable hoy — estando `Locked` no hay
  adopción de baseline, y al día siguiente `c == null` porque falta el `DAY_OPENED` del dayKey nuevo.
  En el caso que sí la leería (reinicio durante `FailClosed`), un `c` viejo de `0.00` contra un `p`
  real diverge más que la tolerancia y **se rehúsa**, que es el desenlace seguro.
  **Dónde sí importa: cert-1.** Al acotar el certificado al día, cualquier cómputo del P&L del día
  sobre la serie de checkpoints leería `0.00` para un día que perdió $40. Se revisa junto con cert-1.
- **CANDIDATO — un guardián que no puede escribir su estado reporta CERO pérdida, no "no sé"**
  (observado 31-ago en la corrida de LT-1; planteado por Roberto). El guardián **sandbox** de BOT A
  entró en fail-closed por contención de archivo sobre su propio temporal de escritura atómica:
  > `FAIL_CLOSED_CLEARED` … `previousReason: "state is not writable: El proceso no puede obtener`
  > `acceso al archivo '…\runs\botA-20260831-140706\state.json.tmp' porque está siendo utilizado en`
  > `otro proceso."`

  Mientras tanto, el log de BOT A imprimió `sandbox dayLoss=0.00` en las vueltas 5, 10, 15, 20, 25,
  30, 35 y **40** — ocho lecturas seguidas. A las 14:10:22 el fail-closed se levantó y la vuelta 45
  imprimió **`37.50`**. El **primer** `PNL_CHECKPOINT` del sandbox es el de las 14:10:22
  (`trigger: "transition"`, `dayLoss 37.50`): durante todo el fail-closed **no escribió ninguno**.
  > *Un guardián que no puede escribir su estado reporta cero pérdida — no "no sé", **cero**.*

  **Misma familia que [la rama "trivially established"] y que LT-2**: el elemento neutro ocupando el
  lugar de una ausencia. **Lo que NO está verificado y hay que establecer primero**: el `0.00` sale
  del log de BOT A, no del ledger del guardián, así que falta saber **cuál de los dos componentes lo
  produce** — el accesor del bot devolviendo su default, o el snapshot del guardián devolviendo cero
  estando fail-closed. Son defectos distintos y no se diseña encima hasta saberlo.
  **Tampoco está atribuida la contención**: quién tenía tomado el `.tmp` no se estableció. El mismo
  patrón de escritura atómica se observó en producción el 31-ago a las 11:20, con el `.tmp` visible a
  mitad de camino.
  **No afecta nada de lo verificado el 31-ago**: producción usa sus propios archivos y su ledger
  registra los checkpoints completos.
- **CANDIDATO, no defecto confirmado — la rama "trivially established" del baseline** (anotado
  31-ago, planteado por Roberto). En `TryAdoptBaseline`:
  `if (c == null && p == 0m) { adopted = 0m; }   // nothing happened; trivially established`
  > *Es una adopción de una sola fuente vestida de sin-riesgo. El comentario dice trivial; lo que hace
  > es adoptar una línea base con corroboración CERO porque el valor casualmente es el elemento neutro.
  > Nuestra propia doctrina dice que un default plausible miente y una ausencia dice la verdad
  > callándose — y **cero es el default más plausible que existe**.*

  La rama de al lado, con `c == null` y `p != 0`, **rehúsa** y nombra el motivo: *"fills while this
  guardian was not running and a platform session reset are indistinguishable"*. **Ese mismo
  argumento no se aplica cuando `p == 0`**, y nadie escribió por qué no.
  **Lo que acota el candidato y hay que decirlo**: `p == 0m` no puede ser "no sé". Tres líneas antes,
  `if (!platform.GrossRealized.HasValue) return;` deja pendiente la adopción cuando la plataforma no
  reporta. Así que `p == 0` es un **cero afirmado por la plataforma**, no un hueco. El candidato se
  reduce a: *¿es un 0 afirmado corroboración suficiente por sí solo, cuando un no-cero afirmado no lo
  es?* — que sigue siendo una pregunta legítima y sin responder.
  **Nadie lo interrogó**, y la corrida del 31-ago **lo esquiva a propósito** (se espera el primer
  `PNL_CHECKPOINT` antes del F5 para que la adopción sea corroborada), así que no va a traer evidencia.
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

### Orden definitivo — REORDENADO 2026-08-31 por la conclusión del día

> **LA AUDIBILIDAD ES INOBSERVABLE DESDE ADENTRO.** El guardián puede leer su configuración de
> sonido, elegir el mejor camino y probar por dos vías. Lo que **no puede, por construcción y no
> por falta de ingeniería, es saber si alguien oyó.** Ni volumen maestro, ni silencio en el
> mezclador, ni parlantes conectados, ni si hay alguien en la habitación.
>
> **Por lo tanto: EL ACUSE DE RECIBO NO ES UNA MEJORA DEL CANAL, ES EL ÚNICO MECANISMO QUE CIERRA
> LA BRECHA.** Sonido, respaldo, chequeo de salud y panel que grita son todos **mejores intentos**;
> ninguno convierte la promesa en verificable. Sólo el acuse lo hace, porque **es el único que
> produce una señal DESDE la persona en vez de HACIA ella.**

Eso mueve el contrato de extensión del sexto lugar al camino crítico del defecto número uno.

0. ~~**LT-1**~~ **ARREGLADO 26-ago**, y ~~**LT-4**~~ **ARREGLADO 31-ago** (opción A + C).
1. **EL CANAL** — sonido (`Globals.PlaySound(Announcement)`, cadencia inmediata + 5 min, plano) y
   **chequeo de salud** con su contención de vocabulario. **No bloqueado por nada: se puede ya.**
   Diseño cerrado en `docs/proposals/the-channel-20260831.md`.
2. **CONTRATO DE EXTENSIÓN con Ventana B — AHORA EN EL CAMINO CRÍTICO.** Sin él no hay acuse, y sin
   acuse el canal sigue sin ruta de falla. **Y lo que se lleva no es un pedido, es la razón entera**:
   no es *"necesito un campo"*, es que **el formato tiene que decidir si admite HECHOS SOBRE LA
   INTERACCIÓN CON EL HUMANO además de hechos sobre la cuenta.** Esa decisión se toma **una vez** y
   condiciona todo lo que venga: el acuse, el hash en `GUARDIAN_STARTED`, y lo que pida el acotado
   del certificado.
   **Y se le sumó uno el 31-ago que NO es aditivo: la ausencia honesta en el emisor** (los siete
   campos del `?? ""`, §5). Los otros piden **agregar** algo al formato; éste pide **poder QUITAR**
   una clave. Si el contrato se decide sólo por la mitad aditiva, ese ítem **queda bloqueado igual**
   — así que la pregunta se hace completa: *¿el formato admite hechos sobre el humano, **y admite
   que una clave falte**?*
3. **ACUSE DE RECIBO** — bloqueado por el 2.
4. **EL CERTIFICADO BORRA EL DÍA QUE MÁS LO JUSTIFICA — PRIMERO DE LA COLA DEL CERTIFICADO, POR
   DELANTE DE ~~cert-1~~** (que ya está hecho, 31-ago). Hallado el 1-sep por una pregunta lateral de
   la auditoría de frenos (`docs/auditoria-frenos-20260901.md` §4.3). `EnterLockout` tiene **tres**
   llamadores y **uno solo** escribe `LIMIT_BREACHED` (`Guardian.cs:767`); el certificado lee **seis**
   tipos de evento y **`CONFIG_TAMPERED` y `SEAL_MISMATCH` no están** (`Certificate.cs:192-206`).
   ⇒ **un día en que el trader editó su config para aflojarse el límite y el guardián lo bloqueó por
   eso sale con `lockoutsTriggered: 0`, `changeAttemptsWhileSealed: 0` y `limitRespected: true`.**
   **Ese es EL día que un trader querría poder mostrar** —la prueba de que alguien intentó aflojarse
   el freno y no pudo— y es el único que el documento no sabe contar. **cert-1 arregló lo que el
   certificado decía de MÁS; esto arregla lo que no sabe decir.**
   **MITIGADO EN PARTE el 1-sep, y dicho con precisión para que nadie lo lea como cerrado**: el hueco
   **sigue abierto** —hacerlo reportable es decidir qué debe decir el documento, y eso es del dueño
   del campo y del verificador, no de una sesión—, pero **el silencio dejó de ser silencioso**: el
   certificado ahora **declara el punto ciego** en `limitations`, que es el mismo movimiento que hizo
   el candidato 6 y el motivo por el que esa lista existe. Tests: **`Cert3_TamperLockoutTests`** — dos
   **verdes sobre el defecto** (convención M4-M7) que fijan `lockoutsTriggered: 0`,
   `changeAttemptsWhileSealed: 0` y `limitRespected: true` en un día bloqueado por manipulación, **y
   que se pondrán ROJOS el día que llegue el arreglo real**, obligando a volver acá; más el espejo
   —un día con brecha real **sí** se cuenta, así que el punto ciego es específico y no general— y la
   contención del texto (una limitación no tranquiliza ni promete el arreglo). **No desplegado.**
4b. **`changeAttemptsWhileSealed` cuenta otra cosa que la que su nombre promete** (anotado aparte del
   4 a propósito, son dos defectos y no uno). Su única fuente es `CONFIG_CHANGE_REJECTED`
   (`Certificate.cs:193`), que sólo emite `TryChangeConfig` — **método público sin ningún llamador en
   el producto**. Se alcanza únicamente de rebote, vía `Arm` (`Guardian.cs:457`), o sea **apretando
   dos veces «Arm for today»** antes del refresco de 1 s. El nombre dice *intentos de aflojar el
   límite estando sellado*; el conjunto contado es *clics de más en un botón*.
5. **Hash del binario en `GUARDIAN_STARTED`** — sólo después del 2. Nombrado por lo que establece:
   *el hash del archivo desde el que se cargó el ensamblado*, no "el código corriendo".
   **`GUARDIAN_STARTED` TIENE DOS SITIOS DE EMISIÓN Y DOS FORMAS** (encontrado por Ventana B el 1-sep,
   verificado acá): el arranque en frío emite `{state:"DISARMED", fresh:true}` y el arranque con
   estado previo emite `{state}` solo — o sea que **las dos descripciones que circulaban eran ciertas
   sobre sitios distintos**. ⇒ **el `buildHash` va en LOS DOS, no en uno.** Medido en el ledger de
   producción: de **70** `GUARDIAN_STARTED`, **exactamente 1** lleva `fresh` — la forma rara es de las
   de *"una vez por instalación"* (familia de M21), así que **esta máquina ya gastó la suya** y no la
   volverá a producir.
6. **`limitRespected`** — semántica, no alcance. Separado a propósito.

**ENTRADA DE OTRA CLASE PARA ESE INVENTARIO, y hay que escribirla ahí** (31-ago):
**LA MÁQUINA PUEDE CORRER TESTS PERO NO PUEDE APRETAR BOTONES CON EXPECTATIVAS.** Los dos defectos
del primer arranque real vivían en código con **303 pruebas verdes**, y los encontró una persona en
sesenta segundos — no porque las pruebas fueran malas, sino **porque ninguna traía una creencia
previa sobre lo que ese botón iba a hacer.** Roberto apretó un `-` esperando minimizar; la regla
pura que las pruebas ejercitaban (`RemembersCollapsed`) era correcta todo el tiempo y el defecto
estaba en **cómo la ventana la aplicaba**, más un glifo que **prometía la única función que el
producto se niega a tener**. Es una condición no producible de otra familia que las de arriba: no
falta un estado del entorno, falta **un humano con una expectativa**.

**CONSTRUIDO 31-ago — `docs/conditions-this-machine-cannot-produce.md`**:
**INVENTARIO DE LAS CONDICIONES QUE ESTA MÁQUINA NO PUEDE PRODUCIR.** Cada entrada: la condición,
por qué esta máquina no la produce, y si hay test que la simule. Es la regla del doble de prueba
vista desde otra altura — *el doble difiere del mundo; la MÁQUINA difiere del campo, y lo que no
puede producir es invisible* — y a diferencia de la primera tiene corolario accionable.
**Es además el registro de riesgos de la beta**: el conjunto exacto de cosas que un desconocido va
a encontrar en su primera semana y que Roberto no encontró en tres.
**Su trabajo real, verificado el 31-ago al comprobar la lista contra la tabla**: separar
*"abierto porque nadie lo vio"* de *"abierto porque se decidió"*, que en una tabla de defectos se
ven idénticos y son riesgos distintos. La marca está en la propia fila — **M4** (*"sin medir"*),
**M7** (*"sin evidencia"*) y **M21** (*"una vez por instalación"*) son del primer tipo; **M5**,
**M20** y sobre todo **M6** (*"diseñado así; **ocurrió varias veces**"*) son del segundo, y M6 es el
contraejemplo que impide leer el patrón como universal.
**Y un TERCER estado, el más engañoso de los tres (31-ago)**: **SIMULADA PERO NO PRODUCIDA** — la
fila con test y sin ocurrencia. Los otros dos **se ven como riesgo**; éste **se ve como una
tranquilidad**, porque dice *"sí"* en la columna que el lector usa para relajarse. **Un test no es
la condición: es lo que CREEMOS que la condición hace** — `G13a`-`G13d` no prueban que el guardián
sobreviva a una suspensión, prueban que sobrevive **al salto de reloj que escribimos nosotros**. Es
el problema del doble de prueba con forma de tranquilidad, y peor que él, porque **el doble se
declara doble**. Consecuencia operativa: una fila *"nadie lo vio"* con test **sigue siendo** *"nadie
lo vio"*; el test abarata el arreglo cuando la condición llegue, **no baja la probabilidad de que
llegue por un camino que no imaginamos**.

**Nota para quien implemente el chequeo de salud**: en la máquina de Roberto el canal está **sano**
(31-ago: `SoundVolumeSerialize` 50, `Announcement.wav` presente, los 13 archivos existen). Eso
significa que va a funcionar para él desde el primer día — **y que la máquina de desarrollo no va a
producir sola el caso roto.** La contención necesita un test que **simule** el canal degradado:
volumen cero, ruta vacía, y ruta no vacía apuntando a un archivo inexistente (que es el peor,
porque parece configurado).

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
