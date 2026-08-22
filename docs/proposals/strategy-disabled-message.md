# El mensaje que ve el usuario cuando el guardián bloquea una `Strategy`

**Hallazgo de producto, no de mecanismo.** Verificado en la documentación y en el foro de soporte de
NinjaTrader. Aplica hoy a cualquier trader que corra una Strategy propia con el guardián armado —
no hace falta esperar a que los bots se conviertan.

## Qué pasa, exactamente

El guardián, al bloquear, llama `Account.Flatten` y `Account.CancelAllOrders`, ambas de cuenta entera
(`nt/addon/GuardianPorts.cs`). Y NinjaTrader trata un cierre externo de posición así:

> Cerrar una posición con el botón *Close* o con *Flatten Everything* **deshabilita automáticamente
> todas las estrategias que corran sobre esa cuenta e instrumento.** Es deliberado: evita que la
> posición interna de la estrategia quede desincronizada de la posición real de la cuenta.

Y *Flatten Everything* específicamente **cierra todas las posiciones, cancela todas las órdenes
activas y deshabilita todas las estrategias NinjaScript habilitadas.**

**Para el mecanismo, eso nos conviene**: el lockout no sólo aplana, además apaga la estrategia que
estaba generando exposición. Es más fuerte de lo que el diseño pedía.

## Lo que el usuario ve — que es lo que importa

En el Log del Control Center, una línea con:

```
Category = "Default"      Message = "Disabling NinjaScript strategy"
```

Y en la pestaña Strategies, su estrategia pasa de **Enabled** a **Disabled**, sola.

Eso es todo. Sin diálogo, sin error rojo, sin una sola palabra sobre el guardián.

## Por qué eso es un problema del producto

El trader ve **su estrategia apagándose sola** y un renglón de log genérico que podría venir de
cualquier cosa. Nada conecta el apagado con el guardián ni con haber alcanzado su límite diario. Las
conclusiones disponibles para alguien que no programa son, en orden de probabilidad:

1. "el bot se colgó"
2. "NinjaTrader tiene un bug"
3. "el guardián me rompió la estrategia"

Ninguna es *"el guardián hizo exactamente lo que le pedí"*. Un producto cuya única acción visible se
lee como una falla propia va a ser desinstalado por la gente a la que protege — que es la definición
de un fallo de producto, aunque el mecanismo sea perfecto.

Y hay un matiz peor: el mensaje **no es un error**. Es informativo, categoría `Default`. O sea que
tampoco va a estar destacado entre los errores rojos que un usuario mira cuando algo sale mal.

## El orden importa más que el texto — y el tiempo verbal más que el orden

**El Log se lee de arriba hacia abajo.** Si `Disabling NinjaScript strategy` aparece primero, el usuario
ya sacó su conclusión antes de llegar a la explicación; una aclaración que llega después no corrige
nada, porque nadie sigue leyendo después de entender.

Pero adelantar el mensaje crea un problema peor que el que resuelve. Un mensaje escrito **antes** de
cancelar y aplanar no puede decir *"cancelé 3 órdenes y cerré tus posiciones"*: en ese instante no
canceló nada. Si la cancelación falla parcialmente, o si el flatten necesita tres intentos y no lo
consigue, el registro queda **afirmando algo falso en un archivo que vendemos como evidencia**.

No se resuelve redactando con cuidado. Se resuelve con **dos mensajes**, cada uno verdadero en el
instante en que se escribe — la misma disciplina del ledger aplicada a la prosa.

| | cuándo | tiempo verbal | qué puede afirmar |
|---|---|---|---|
| **1. el aviso** | al escribirse `LIMIT_BREACHED` | **futuro** | la brecha, los números, lo que está por hacer |
| **2. el resultado** | después de `FLATTEN_VERIFIED` (o de `LOCKOUT_INCOMPLETE`) | **pasado** | qué pasó realmente, con las cifras reales |

`LIMIT_BREACHED` es más temprano que la primera llamada al broker y precede al mensaje de NinjaTrader
con margen de sobra. Y en ese punto la brecha ya se conoce y todavía no se tocó nada, que es
exactamente la condición que hace honesto el futuro.

## El enganche que falta, y lo que cuesta

Hoy no hay forma de que el adaptador actúe **en** `LIMIT_BREACHED`: ese evento lo escribe GuardianCore,
que por G22 no puede referenciar NinjaTrader, y sus únicos seams son los cuatro puertos inyectados. Lo
más temprano que el adaptador controla es la primera línea de `NtBrokerActions.CancelAllOrders`, que
llega microsegundos después.

**Lo que propongo, y es un cambio a la superficie de Core:** un observador opcional en el `Ledger`,
`Action<LedgerEntry>`, invocado después de cada append exitoso. El adaptador se suscribe y reacciona a
`LIMIT_BREACHED` y a `FLATTEN_VERIFIED` / `LOCKOUT_INCOMPLETE`. **Un solo seam para los dos mensajes**,
en vez de un "anunciador" a medida, y el patrón ya existe en la librería hermana — el `publisher` de
anclas de `deadman` tiene esa forma.

Las condiciones, porque un seam nuevo es superficie nueva:

- el callback recibe un `LedgerEntry`, tipos de Core solamente: **G22 se mantiene**;
- se invoca **envuelto en try/catch**, y una excepción suya no puede romper el append ni frenar el
  lockout. Es *best-effort* y nunca portante — escribirlo en el contrato, no confiarlo al llamador;
- §14 de la SPEC (la lista de seams) pasa a nombrarlo.

### Best-effort no puede significar invisible

Tragarse la excepción y seguir es **la misma forma otra vez**: un camino que falla sin dejar rastro. Y
ahora hay una afirmación de producto encima — *"el guardián te explica lo que pasó"* — sostenida por un
callback que puede no haber corrido nunca sin que nadie se entere. **Cero fallas de notificación tiene
que ser un hecho comprobable, no una suposición.**

Y hay una trampa propia: si el manejador de la falla appendeara al ledger para dejar constancia,
estaría appendeando **desde adentro del append**, con recursión en la ruta crítica del lockout. La
solución no puede vivir en el mismo lugar que el problema. Entonces:

1. **La falla se cuenta, no se registra en el momento.** El `Ledger` incrementa un contador y no hace
   nada más dentro de la notificación. Sin I/O, sin recursión, sin demora.
2. **Guarda de reentrada de todos modos**, no sólo por este camino: un flag por hilo que salte la
   notificación si ya estamos dentro de una. Un observador que appendea es un bug del observador, pero
   el ledger no puede recursar por culpa de un bug ajeno.
3. **El `Tick` siguiente publica el conteo**, fuera de la ruta del append: si el contador es mayor que
   cero, se escribe un evento `NOTIFY_FAILED` con `count` y se resetea. Es una línea más del ledger, con
   su hash, encadenada como todo lo demás.
4. **El reporte del día lo muestra** aunque sea cero, porque un cero que sólo aparece cuando hay algo
   que contar no es un cero verificado.
5. **El certificado no gana ningún campo nuevo.** `NOTIFY_FAILED` es un evento del ledger, así que el
   verificador lo cuenta como cuenta todo lo demás — misma regla que la cobertura de continuidad: si lo
   computa el verificador es una cantidad verificada; si lo publica el emisor es una cantidad afirmada.
   Lo único que cambia es §12, el catálogo de eventos, que gana una fila.

Nada de esto vuelve portante al observador. Un lockout con las tres notificaciones fallidas sigue
cancelando, aplanando y quedando `LOCKED`: lo único que se pierde es la explicación — y esa pérdida
queda escrita, fechada y encadenada, en vez de desaparecer.

**Alternativa sin tocar Core**, por si el costo no se acepta: el mensaje 1 en la primera línea de
`CancelAllOrders`, en futuro. Es igual de honesto — ahí tampoco se canceló nada todavía — y precede al
mensaje de NinjaTrader, sólo que con menos margen. Lo que **no** funciona es el mensaje 2 desde ahí: el
resultado del flatten no se conoce hasta después.

## Dónde escribirlo para que no se pierda

`NinjaScript.Log(string, LogLevel)` y `Cbi.Log.Process(Type, string, object[], LogLevel, LogCategories)`
existen (verificado por reflexión). Los valores disponibles:

- `LogLevel`: **Alert**, Information, Warning, Error
- `LogCategories`: Ati, Connection, **Default**, Execution, NinjaScript, Order, Position, Strategy,
  **Account**, LicenseManagement, DB, System, User

El mensaje de NinjaTrader sale informativo y con `Category = "Default"`. Para no quedar enterrado al
lado, el del guardián va con **`LogLevel.Alert`** — el nivel más alto y el más raro, o sea el que
sobrevive a cualquier filtro que alguien ponga cuando algo sale mal — y **`LogCategories.Account`**, que
es lo que la acción realmente es: algo que le pasó a la cuenta entera, no a una estrategia.

*No verificado*: cómo se renderiza exactamente esa combinación en la pestaña Log. La API existe; su
apariencia hay que mirarla en pantalla antes de darla por buena.

## Mensaje 1 — el aviso, en futuro

> **LÍMITE DIARIO ALCANZADO. El guardián está cerrando tu día.**
> Perdiste $612.40 hoy y tu límite era $600.
> Ahora voy a cancelar tus órdenes activas y cerrar tus posiciones en esta cuenta.
> Tenés estrategias corriendo acá (MiEstrategia en MES 09-26). NinjaTrader apaga sola cualquier
> estrategia cuya posición se cierre desde afuera, así que es probable que veas alguna pasar a
> **Disabled** con el mensaje `Disabling NinjaScript strategy`. **No es un error, no se rompió nada, y
> no es un problema de la plataforma.**
> Esto es lo que pediste que pasara.

Sobre las estrategias: se **nombra lo que está corriendo** (`Account.Strategies`, que existe) y se
**describe la regla**, sin prometer cuáles van a caer. NinjaTrader deshabilita las de esa cuenta e
instrumento cuya posición se cerró desde afuera; nombrar una que después no se apague sería
sobreafirmar otra vez, en el mismo mensaje que existe para no sobreafirmar.

## Mensaje 2 — el resultado, en pasado

Cuando el flatten se verificó:

> **Día cerrado.** Cancelé 3 órdenes y cerré 1 posición. Quedaste plano.
> Hasta las 17:00 (America/Chicago) voy a cancelar cualquier orden nueva en esta cuenta. No puedo
> impedir que la mandes: la detecto y la cancelo, y si alguna llega a ejecutarse, cierro la posición.

Cuando **no** se verificó — `LOCKOUT_INCOMPLETE`, que existe en el catálogo y hoy no se le dice a nadie:

> **No pude cerrar todo.** Cancelé 3 órdenes, pero quedó 1 posición abierta en MES 09-26 después de 3
> intentos. **Cerrala vos.** Hasta las 17:00 (America/Chicago) voy a seguir cancelando cualquier orden
> nueva en esta cuenta.

Dos correcciones que este par incorpora y que la versión anterior de este documento hacía mal:

**La promesa que §17 desmiente.** *"No vas a poder volver a operar hasta las 17:00"* afirma una
prevención que el producto no tiene. El guardián **no impide** colocar una orden: la detecta y la
cancela, y una orden a mercado puede llenarse antes de que el cancel llegue — por eso existe el
flatten. Escribir una garantía de prevención en el único mensaje que el usuario va a leer de verdad,
cuando el modelo de amenazas la desmiente dos documentos más allá, es exactamente la clase de
sobreafirmación que este proyecto no comete en el código y no puede cometer en la prosa.

**La hora sin zona.** *"hasta las 17:00"* no significa nada para alguien fuera de ese huso, y el
guardián **conoce** la zona: está en el snapshot sellado (`sessionResetTimeZone`). Se imprime.

## La ventana de estado: la misma pareja, no una tercera redacción

El guardián tiene una ventana propia, que es la otra superficie donde puede hablar con su voz. Hoy
**muestra un estado**; lo que falta es que **anuncie el cambio** cuando ocurre.

Debe llevar exactamente los dos mensajes de arriba, con el mismo texto y el mismo reparto de tiempos
verbales — mensaje 1 al escribirse `LIMIT_BREACHED`, mensaje 2 al conocerse el resultado. **Una tercera
redacción propia de la ventana sería una tercera cosa que puede contradecir a las otras dos**, y la
versión anterior de este documento ya cometió ese error: proponía para la ventana un texto que prometía
*"no se puede volver a operar hasta las 17:00"*, la misma sobreafirmación corregida arriba, sobreviviendo
en el párrafo siguiente porque nadie la volvió a leer. Un solo par de cadenas, consumido por las dos
superficies.

Lo que falta, entonces, es sólo mecánica: que la ventana se suscriba al mismo observador del ledger que
el Log, y que el bloqueo sea visible sin tener que ir a buscarlo.

Sin eso, el evento más importante que el producto produce en toda su vida —el único momento en que
realmente salva a alguien— se le presenta al usuario como si el software se hubiera roto.

## `LOCKOUT_INCOMPLETE` no es un fracaso, y la primera corrida real lo demostró

**Corrección urgente a este documento.** El mensaje 2 estaba redactado para disparar en
`LOCKOUT_INCOMPLETE` con un texto de fracaso — *"No pude cerrar todo. Cerrala vos."*. La primera
corrida de BOT A contra fills reales, el 2026-08-22, probó que eso habría sido **falso en el caso
normal**:

```
19:20:42.637  LIMIT_BREACHED        dayLoss=50.00
19:20:42.706  FLATTEN_REQUESTED
19:20:42.706  LOCKOUT_INCOMPLETE    <- el mensaje habria salido ACA
19:20:43.203  ORDERS_CANCELLED      (reintento sobre el tick, A7)
19:20:43.208  FLATTEN_VERIFIED      <- 502 ms despues, todo cerrado
```

El flatten es una orden market real y tarda en llenarse, así que en la primera evaluación la posición
todavía no estaba plana. **`LOCKOUT_INCOMPLETE` es un estado intermedio TRANSITORIO de un lockout que
está saliendo bien.** Con el diseño de ayer, ese texto habría aparecido en pantalla **medio segundo
antes de que el guardián cerrara todo correctamente, en CADA lockout normal**, mandando al usuario a
cerrar a mano una posición que estaba por cerrarse sola. Peor que no decir nada: un aviso que produce
una acción innecesaria en el peor momento del día de alguien.

**La regla, entonces:** el mensaje sólo puede dispararse en un `LOCKOUT_INCOMPLETE` **TERMINAL**. El
evento ya trae con qué distinguirlo — `Guardian.cs:615-618` escribe `attempts` y
`exhausted = FlattenAttempts >= MaxFlattenAttempts`. **Sólo `exhausted: true` es un resultado.**
Cualquier otro es ruido de reintento y no debe llegar a un ser humano.

(Ojo también: hay tres sitios que emiten `LOCKOUT_INCOMPLETE`. Los de `Guardian.cs:572` y `:590` son
excepciones por paso — `step: cancel` / `step: flatten` — y **no llevan `exhausted`**. Ausencia de
`exhausted` no es `exhausted: false`: es otro evento. El consumidor tiene que exigir el campo, no
inferirlo.)

**Llegamos a esto por dos caminos independientes.** Ventana B lo predijo leyendo el código media hora
antes de esta corrida — *"sólo el último es el resultado, y la spec tiene que decirlo o dos
implementaciones van a discrepar"* — y la corrida lo produjo en vivo sin saber de esa predicción. Una
lectura estática y una ejecución real coincidiendo es lo que separa un hecho de una opinión, y es la
razón por la que esto se escribe como regla y no como anécdota.

## El hermano: el titular de la ventana dice lo mismo para dos estados opuestos

**Mismo patrón, otra superficie, y este ya le pasó a un humano real.** El 22-ago Roberto vio
`NOT PROTECTED`, buscó el botón Arm, no lo encontró, y nada en pantalla le dijo por qué. El guardián
estaba haciendo exactamente lo correcto.

Verificado en `nt/addon/DeadmanGuardianAddOn.cs:512-549`, el mapeo actual:

| estado | color | titular | detalle | botón Arm |
|---|---|---|---|---|
| `Armed` | verde | `ARMED` | Watching {cuenta}. Entries allowed. | oculto |
| `Locked` | rojo | `LOCKED` | Daily limit reached. No new entries. | oculto |
| `FailClosed` | naranja | **`NOT PROTECTED`** | Blocked, state unknown: {motivo} | **oculto** |
| `Disarmed` | gris | **`NOT PROTECTED`** | Disarmed. Nothing is being watched. | visible |

**El mismo titular para dos situaciones que no se parecen en nada:**

- **fail-closed**: el sello está **vivo**, el guardián está armado, está bloqueando entradas, y lo
  único que le falta es poder ver la cuenta. Está protegiendo todo lo que puede proteger.
- **disarmed**: no hay nada armado, no hay sello, nadie está mirando nada.

El color los distingue y el detalle también, pero **el titular es lo que se lee**, y dice lo mismo.

### Titulares propuestos

| estado | titular propuesto |
|---|---|
| `FailClosed` | **`NO PUEDO VER TU CUENTA`** / *`CANNOT SEE YOUR ACCOUNT`* |
| `Disarmed` | **`SIN ARMAR`** / *`NOT ARMED`* |

`NOT PROTECTED` desaparece de los dos, porque en fail-closed es directamente engañoso hacia el lado
peligroso: sugiere que no hay nada operando cuando sí lo hay.

### El detalle tiene que decir QUÉ HACER

*"Blocked, state unknown: AccountUnknown on Sim101: account is Disconnected"* es exacto y es inútil:
describe el estado interno y no da un paso siguiente. Lo accionable:

> **NO PUEDO VER TU CUENTA**
> NinjaTrader no tiene ninguna conexión abierta, así que no puedo leer tu P&L y no puedo garantizar
> tu límite. Mientras tanto no dejo abrir posiciones nuevas.
> **Conectá el feed en Connections y el guardián vuelve solo** — no hay que reiniciar nada.
> Seguís armado: tu límite de $600 vale hasta las 17:00 (America/Chicago).

### Y el botón que falta hay que explicarlo, no sólo omitirlo

**Un botón ausente sin motivo se lee como un bug.** En fail-closed está oculto por una razón buena: si
el sello sigue vigente, **el usuario ya está armado** y no hay nada que armar; lo que falta es
reconectar.

Cuidado con el tiempo verbal, que es la misma trampa de este documento: `FailClosed` **no siempre**
tiene sello — `StartCorrupt` entra en fail-closed sin ninguno. Así que el texto no puede afirmar
"seguís armado" sin mirar. Dos variantes, cada una verdadera cuando se escribe:

| condición | última línea |
|---|---|
| hay sello vigente | *Seguís armado: tu límite vale hasta las {hora} ({zona}). Por eso no aparece el botón Arm — no hay nada que armar, hay que reconectar.* |
| no hay sello | *No hay nada armado todavía. Cuando pueda ver la cuenta va a aparecer el botón Arm.* |

La hora **con su zona**, siempre: "hasta las 17:00" no significa nada para alguien en otro huso, y el
guardián conoce la zona configurada.

### Esto obedece A10

El titular y las dos variantes del detalle son **cadenas únicas**, consumidas por la ventana y por
cualquier otra superficie que las muestre. No una versión para la ventana y otra para el Log:
[`AMENDMENTS.md` A10](../../AMENDMENTS.md) existe porque ya encontramos una sobreafirmación viviendo
un párrafo debajo de su propia corrección.

## Fuentes

- [Strategy disables — NinjaTrader Support Forum](https://forum.ninjatrader.com/forum/ninjatrader-8/platform-technical-support-aa/93519-strategy-disables)
- [What happens when "Flatten Everything" is pressed?](https://forum.ninjatrader.com/forum/ninjatrader-8/strategy-development/1204870-what-happens-when-flatten-everything-is-pressed)
- [How can I prevent a strategy from auto disabling after position is closed?](https://forum.ninjatrader.com/forum/ninjatrader-8/strategy-development/1107907-how-can-i-prevent-a-strategy-from-auto-disabling-after-position-is-closed)
