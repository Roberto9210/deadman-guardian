# Propuesta — los bots como `Strategy` en vez de `AddOn`

**Estado: propuesta aprobada en principio, nada implementado.** Fuera del camino crítico: los bots
AddOn no están rotos, están invisibles, y eso es documentación, no función.

## Por qué cambiar

Tres razones, ninguna técnica, todas de producto:

1. **El botón de pánico.** Hoy detener BOT A es borrar un archivo y reiniciar NinjaTrader. Un bot que
   pierde a propósito, en manos de alguien que no programa, tiene que apagarse con un clic.
2. **Es donde un desconocido busca.** El diálogo de Strategies del gráfico es el lugar correcto para
   buscar algo que opera; que no esté ahí es un defecto de producto aunque el código funcione.
3. **`SetStopLoss` manda un stop real al venue**, que es exactamente la historia que BOT B hoy escribe
   a mano con `StopMarket`. El framework la cuenta mejor que nosotros.

## Las tres piezas que hay que resolver — y son la misma pieza

Un `AddOn` arranca una vez por proceso de NinjaTrader y se apaga con él. Una `Strategy` la habilita
alguien, puede habilitarse dos veces, y **vuelve habilitada** al reabrir un workspace guardado. Los tres
problemas que eso crea tienen la misma raíz: *el estado del bot dejaba de existir con el proceso, y
ahora no.*

### 1. Presupuesto de órdenes — derivado, no persistido aparte

**El riesgo, dicho exacto:** `SessionBudget` vive en memoria. Una Strategy que revive tras un reinicio
vuelve con el presupuesto en cero usadas. BOT A podría reiniciarse solo y volver a tener 200 órdenes.
Bajo AddOn + compuerta quemada eso era imposible.

**Lo que NO hay que hacer:** un archivo `budget.json` aparte. Sería un archivo que se borra para
conseguir presupuesto nuevo, que es precisamente el modo de fallo que el proyecto rechaza en todo lo
demás.

**Lo que sí cierra — y lo que no.** Acá hay que separar dos cosas que el planteo original junta:

| lo que se necesita | de dónde sale | ¿cierra? |
|---|---|---|
| el **límite del día** correcto (rueda 17:00 CT, no medianoche) | `Status.DayKey` del guardián, que viene de `SessionCalendar` | **sí** |
| el **presupuesto de pérdida** | `dayLoss` de los `PNL_CHECKPOINT` del ledger del guardián | **sí** |
| el **conteo de órdenes enviadas** | — | **NO** |

**Esto es lo que no cierra, y hay que decirlo:** el ledger del guardián **no puede dar un conteo de
órdenes**. `CERT_CONFORMANCE.md` ya lo declara para el certificado: *"`tradesObserved` no es
recalculable: ningún evento del vocabulario registra un conteo de fills"*. El guardián sólo escribe
`ORDER_REJECTED_LOCKED`, y sólo cuando está bloqueado. Derivar el presupuesto de órdenes del ledger del
guardián es imposible sin agregarle un evento al emisor — y agregarle un evento al emisor para que un
bot de prueba lleve su cuenta es exactamente la clase de contaminación que hay que evitar.

**La salida, que usa lo que ya existe:** el bot **ya tiene un ledger encadenado propio** — el de su
guardián sandbox. Hoy su directorio se nombra por timestamp (`botA-20260821-140233`), así que un
reinicio abre uno nuevo y el conteo se pierde. **Nombrarlo por `dayKey` en vez de por timestamp** lo
arregla entero:

- un reinicio reabre **el mismo** ledger y el bot cuenta sus propios envíos previos leyéndolo;
- rueda a las 17:00 CT solo, porque el `dayKey` es el del guardián;
- no hay archivo que borrar para conseguir presupuesto: borrarlo **rompe la cadena de hashes**, que es
  visible, y es peor que quedarse sin presupuesto.

El presupuesto pasa a ser una **derivación de un registro encadenado**, no un contador en memoria ni un
archivo de conveniencia. Es la misma forma que todo lo demás en el proyecto.

**La trampa del día, señalada antes de caer en ella:** un presupuesto "por día calendario" y un guardián
cuyo día rueda a las 17:00 CT son **dos días distintos en el mismo sistema**. Entre las 17:00 y la
medianoche conviven un `dayKey` nuevo y una fecha vieja; un bot que use `DateTime.Today` tendría, esas
siete horas, un presupuesto que no coincide con el día del guardián — y a la medianoche se resetearía
solo en mitad de una sesión armada. **Nunca fecha calendario. Siempre `Status.DayKey`.**

### 2. Armado — chequeo antes de CADA envío, no puerta de arranque

**El riesgo:** una Strategy habilitada a las 16:50 sigue habilitada a las 17:05. Si el guardián desarma
en el borde de sesión, un bot que sólo miró al arrancar sigue mandando órdenes sin nadie vigilando.

**La forma:** `Submit()` empieza consultando `_sandbox.Status.Kind` **en vivo**, nunca un booleano
cacheado del arranque. Si no es `Armed`, no manda y lo anota. Es una línea, cuesta nada, y convierte una
dependencia temporal frágil (los 45 segundos del AddOn) en una precondición explícita por operación.

Mismo chequeo cubre gratis el caso que hoy no está cubierto: si el guardián entra en `FailClosed` por
una anomalía de reloj a mitad de corrida, el bot se detiene en el envío siguiente.

### 3. Exclusión mutua — reclamo en proceso, no lock en disco

**El riesgo:** bajo AddOn hay exactamente una instancia por proceso de NinjaTrader. Bajo Strategy
cualquiera habilita A y B en dos gráficos, o **la misma dos veces**, y aparecen dos guardianes sandbox
escribiendo en directorios distintos mientras actúan sobre la misma cuenta.

**Lo que NO hay que hacer:** un lock en disco. Un cuelgue lo deja tomado y hay que inventar una regla de
caducidad — un lock rancio es un modo de fallo nuevo a cambio de resolver uno viejo.

**La forma:** un reclamo estático en memoria, en el mismo ensamblado (`NinjaTrader.Custom` es uno por
proceso de NinjaTrader):

```
BotClaim.TryClaim("A")  ->  false si "A" o "B" ya está reclamado en este proceso
BotClaim.Release("A")   ->  en State.Terminated
```

Sin disco, sin caducidad, sin lock rancio: **el registro muere con el proceso, que es exactamente lo
correcto**, porque un proceso muerto no tiene bots corriendo. Y cubre el caso que el archivo-compuerta
nunca cubrió: la misma Strategy habilitada dos veces.

El único caso que no cubre son dos procesos de NinjaTrader contra la misma cuenta — que ya es territorio
de `CONCURRENT_WRITER_DETECTED` y lo detecta el sello del guardián, no esto.

## Qué sobrevive del código actual

| pieza | qué le pasa |
|---|---|
| `BotSandboxGuardian` | **intacto.** Es GuardianCore más los puertos NT; no le importa quién lo hospede. Sólo cambia el nombre del directorio: `dayKey` en vez de timestamp |
| `BotSafety.VerifyAccount` | **sobrevive cambiando de significado, para mejor.** Hoy elegimos la cuenta; bajo Strategy la elige quien clickea, y la función pasa de *"elegimos Sim101"* a **"vetamos cualquier cuenta que no sea Sim101"**. Defiende contra un misclick, que la compuerta nunca cubrió. Corre en `State.Realtime`, cuando `Account` ya existe |
| `BotSafety.ResolveInstrument` | **innecesario.** La Strategy recibe su instrumento de la serie de datos del gráfico |
| `SessionBudget` | **la clase sobrevive, la fuente cambia**: se inicializa leyendo el ledger del día en vez de arrancar en cero |
| `BotGate` | **desaparece.** "Habilitar" es la compuerta y "Deshabilitar" es el botón de pánico. Su otra función —impedir la repetición por reinicio— la absorbe el presupuesto derivado |
| `BotLog` y los reportes | **intactos** |
| plomería de órdenes | **decisión pendiente**: seguir con `Account.CreateOrder/Submit` (funciona, pero entonces la Strategy es sólo una cáscara y sólo ganamos la UI) o pasar a órdenes gestionadas (`EnterLong`, `SetStopLoss`). Lo segundo es trabajo real y mejora BOT B; no está decidido |

## Lo que el usuario ve cuando el guardián bloquea — ver `strategy-disabled-message.md`

Cambia bajo Strategy y es un asunto de producto, no de mecanismo. Va en su propio documento.
