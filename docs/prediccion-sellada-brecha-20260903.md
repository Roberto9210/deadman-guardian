# Predicción sellada — prueba de brecha deliberada en `Sim101`

**Escrita el 2026-09-03, ANTES de que se toque nada.** Documento datado: se anota, no se reescribe.
La corrida se juzga contra este texto tal como está en su commit, no contra una versión posterior.

---

## 0. Sobre QUÉ código es esta predicción — leerlo primero

**No es sobre el repositorio. Es sobre el binario que está cargado en NinjaTrader ahora mismo, que
es el código del 1-sep.** Medido, no inferido:

| | |
|---|---|
| `bin/Custom/GuardianCore.dll` | sha256 `6529a92ae9b47240b32a1b12489904993eca61860236a3cbf7fce6d76127c420`, construido **2026-09-02 18:09:57**, byte-idéntico a `src/GuardianCore/bin/Release/net48/GuardianCore.dll` |
| `bin/Custom/NinjaTrader.Custom.dll` (el addon) | sha256 `134f6f82ce6ab47da36e34c37dfd4cc97f2dc7d2d3b725d5da1d23611de7ef63`, construido **2026-09-02 18:14:51** |
| último commit de `src/` **anterior** al build | **`ea60de9`**, 2026-09-01 13:32:39 |
| último commit de `nt/addon/` anterior al build | **`485787f`**, 2026-09-01 09:51:38 |

**Los dos commits de fuente del 2-sep son POSTERIORES al build y no están corriendo:**
`05d20bb` (18:40:19, `exhausted`→`needsHuman`) y `21018bb` (19:51:02, `MaxFlattenAttempts`→
`FlattenAttemptsBeforeHuman` + comentarios de `Messages.cs`).

Verificado **dentro del binario**, en las dos codificaciones que usa un ensamblado .NET (nombres en
UTF-8, literales en UTF-16): `exhausted` está como **literal UTF-16**, `needsHuman` **no**;
`MaxFlattenAttempts` está como nombre, `FlattenAttemptsBeforeHuman` **no existe**.

**Consecuencia buena para la prueba:** el diff de `Messages.cs` en `21018bb` es **sólo comentarios**
(verificado quitando las líneas `//`: queda vacío), y el de `05d20bb` también. **Ningún texto visible
difiere** entre el binario y el repo. Lo único que difiere en conducta observable es la clave del
payload — que es la predicción 1.

**Estado de producción al sellar:** `state.json` → `DISARMED`, `sealHash` vacío,
`dayKey 2026-09-01`, `flattenAttempts 0`. Cabeza del ledger: **`seq 8106`**, 2026-09-02T23:14:52Z.
Config vigente: `Sim101`, personal **600.00**, firma 1000.00, tolerancia 5.00, reset 17:00
`America/Chicago`.

---

## 1. LAS TRES PREDICCIONES QUE MÁS ENSEÑAN

### P-A · El ledger va a escribir `"exhausted"`, no `"needsHuman"`

En cada `LOCKOUT_INCOMPLETE` el payload será
`{"accounts":["Sim101"],"attempts":N,"exhausted":false|true}`.

**Falsador:** si aparece `"needsHuman"`, **nos equivocamos al identificar el binario** — el DLL
cargado no es el que medimos, y todo lo demás de esta predicción queda en duda hasta re-identificarlo.
Es el falsador más barato y el más importante: **es la única afirmación de esta página que verifica
que lo que corre es lo que decimos que corre.**

*Dónde se observa:* `ledger.jsonl`, cualquier fila `LOCKOUT_INCOMPLETE` a partir de `seq 8107`.

### P-B · Si el aplanado verifica, **NO SUENA NADA**

El sonido no está atado a la brecha: está atado a `NeedsHuman`. En el addon desplegado,
`AlertIfNeeded` retorna en la primera rama si `!v.NeedsHuman` (`:620-621`), y
`Guardian.LockoutNeedsHuman` es `FlattenAttempts >= MaxFlattenAttempts`, con la constante en **3**.

En el único lockout que verificó en producción (31-ago) los `LOCKOUT_INCOMPLETE` llegaron a
**attempts 2** y el `FLATTEN_VERIFIED` fue con attempts 3 ⇒ `NeedsHuman` **nunca fue true** ⇒ **no
sonó**. Predecimos lo mismo.

> **En ese caso la única superficie de aviso es un panel WPF que puede estar tapado por otra ventana,
> minimizado, o en un monitor que Roberto no está mirando.** No hay toast, no hay ventana emergente,
> no hay correo. Hay además una línea en el Log de NinjaTrader (`LogLevel.Alert`), en una pestaña que
> Roberto no mira — que es la premisa de esta prueba.

**El sonido existe para el FRACASO, no para el EVENTO.** Si esta predicción se cumple, queda medido
que el producto guarda su único canal audible para el caso que ocurrió una vez de dos.

*Dónde se observa:* los oídos de Roberto, y su anotación de la hora. **El ledger es ciego a esto.**

### P-C · Qué va a ver Roberto, y en cuánto tiempo

**Predecimos que puede no darse cuenta de que algo pasó.**

Lo escribimos así, sin suavizarlo, porque **es el resultado más importante que esta prueba puede
dar**. La cadena entera es: el panel cambia de verde `ARMED` a rojo oscuro (`#B71C1C`), el titular
pasa a decir `LOCKED` en 22 px, el ancho de la ventana **no cambia** (330 px en los dos estados) y
**no suena nada**. Si el panel no está a la vista, **la señal es de cero bits**.

Lo que sí va a notar, tarde o temprano y por otra vía: **su posición desapareció** y NinjaTrader
apagó cualquier estrategia que tuviera corriendo en esa cuenta. Es decir, **el canal real del
producto es la consecuencia, no el aviso.**

Predicción cuantitativa, para que se pueda fallar:

| si el panel está visible | **≤ 5 s** — el cambio de color y de titular es inmediato al `LIMIT_BREACHED` |
| si el panel está tapado o minimizado | **> 60 s**, y probablemente *"me di cuenta cuando fui a mirar"* |

**Nos equivocamos si Roberto anota que se dio cuenta en menos de 60 segundos sin haber estado mirando
el panel.** Eso significaría que hay un canal que no encontramos leyendo el código, y sería una buena
noticia.

---

## 2. El resto de la predicción, paso por paso

### 2.1 Al armar

Ledger, en este orden, `seq` contiguo desde **8107**:

1. `CONFIG_LOADED {configHash: <64 hex>}` — si el config cambió respecto del cargado al arrancar
2. `ARMED {"accounts":["Sim101"],"dayKey":"<D>","firmLimit":"1000.00","personalLimit":"<el chico>"}`
3. `SEAL_CREATED {expiresAtUtc, ledgerHeadHash, sealDurationMs, sealHash}`
4. `DAY_OPENED {"dayKey":"<D>"}`

Los pasos 2-4 dentro del **mismo milisegundo o los 2 ms siguientes** (26-ago: `.109/.110/.111`).

**Regla del `dayKey`, y hay que respetarla al elegir la hora:** si arma **antes** de las 17:00 CT,
`dayKey` = hoy y el sello vence hoy a las 17:00. Si arma **después**, `dayKey` = mañana y **queda
bloqueado hasta mañana a las 17:00 CT** — eso es lo que pasó el 26-ago (armado 18:40 CT ⇒
`dayKey 2026-08-27`). `expiresAtUtc` será **`...T22:00:00.000Z`** (17:00 CDT).

*Panel:* titular `ARMED`, detalle `Watching Sim101. Entries allowed.`, botón Arm oculto.

### 2.2 Al cruzar el límite

Ledger, plantilla del 31-ago (`seq 7995`-`8002`), toda la secuencia en **~1 segundo**:

```
LIMIT_BREACHED    {"dayLoss":"<= o > limite>","limit":"<limite>","perAccount":{"Sim101":"-<x>"}}
ORDERS_CANCELLED  {"account":"Sim101","count":0,"orderIds":[]}      <- ver el control de 2.5
FLATTEN_REQUESTED {"account":"Sim101","instruments":["MES ..."]}
LOCKOUT_INCOMPLETE{"accounts":["Sim101"],"attempts":1,"exhausted":false}
FLATTEN_REQUESTED {...}
LOCKOUT_INCOMPLETE{"accounts":["Sim101"],"attempts":2,"exhausted":false}
FLATTEN_REQUESTED {"account":"Sim101","instruments":[]}
FLATTEN_VERIFIED  {"accounts":["Sim101"],"attempts":3}
```

**Predicción explícita sobre el 1-contra-169: predecimos `FLATTEN_VERIFIED`, con `attempts` entre 1
y 3.**

El motivo, y corrige una cifra que veníamos citando: **«1 verificado contra 169 incompletos» cuenta
CICLOS, no EPISODIOS.** Los 167 incompletos son **un solo episodio**, el del 26-ago, y su causa está
arreglada — el guardián se cancelaba sus propios `FLATTEN_REQUESTED`, lo que retiró `a916bba` el
27-ago. Los otros 2 son los dos primeros intentos del episodio del 31-ago, que **verificó al
tercero**. En episodios: **2 en total, 1 bajo el código actual, y ése verificó.**

**Nos equivocamos si aparecen `LOCKOUT_INCOMPLETE` con `attempts >= 3`.** En ese caso `NeedsHuman`
se enciende y P-B se invierte: **sonaría**, el panel pasaría a rojo vivo `#D50000`, titular
`THE GUARDIAN NEEDS YOU` en 30 px y ancho 430 px. **Ese contraste es intencional en el diseño y es
la mejor observación que la prueba puede dar si el aplanado falla.**

*Panel al verificar:* titular `LOCKED`, detalle exactamente
`Daily limit reached on Sim101. This does not block new orders - nothing here can - but no position will stay open until 17:00.`

*Log de NinjaTrader* (`LogLevel.Alert`), dos líneas y en este orden:
1. al `LIMIT_BREACHED`: `DAILY LOSS LIMIT REACHED. The guardian is closing your day on Sim101. You are down $X and your limit is $Y. I am about to cancel your working orders and close your positions. NinjaTrader will switch off any strategy running on this account as a result - that is NinjaTrader reacting to the positions being closed, not an error, and nothing is broken.`
2. al `FLATTEN_VERIFIED`: una línea que empieza con `LOCKED.` y termina `This is what you asked for.`

### 2.3 G8 en vivo — el falsador que más queremos

Después del bloqueo, Roberto manda **una orden nueva** en `Sim101`.

**Predicción, en dos partes que hay que juzgar por separado:**

**(a) La orden llega al broker, puede llenarse, y NO queda NINGÚN evento en el ledger que la
nombre.** Cero `ORDER_REJECTED_LOCKED`, cero `ORDERS_CANCELLED` que la mencione, cero filas nuevas
atribuibles a esa orden. `OnOrderObserved` retorna sin actuar desde `a916bba`.

**(b) PERO si se llena, el ledger SÍ se mueve** — porque vuelve a haber exposición y el arreglo de
LT-4 la cierra en el ciclo siguiente: aparecerá un `FLATTEN_REQUESTED` nuevo y después
`FLATTEN_VERIFIED` (o `LOCKOUT_INCOMPLETE`). **El registro reacciona a la POSICIÓN, nunca a la
ORDEN.** Esa es la diferencia entre efecto e intención, hecha visible en un archivo.

**Si aparece `ORDER_REJECTED_LOCKED`, G8 no es lo que creemos y A12 está mal.** Es el falsador más
importante del día.

### 2.4 El sello

- Vigente hasta **`expiresAtUtc`** = 17:00 CT del `dayKey`. **No hay forma de aflojarlo desde la
  interfaz**: el botón Arm está oculto en `LOCKED`.
- **Editar `config.json` con el sello vigente ⇒ `CONFIG_TAMPERED`** y el cambio no toma efecto. El
  literal está en el binario. *No lo hagan como parte de esta prueba salvo que quieran medirlo a
  propósito.*
- **Reiniciar NinjaTrader ⇒ vuelve bloqueado**: `GUARDIAN_STARTED {"state":"LOCKED"}` →
  `STATE_RESTORED {"state":"LOCKED","sealHash":<el mismo>}` → `SEAL_VERIFIED`.
- Al vencer, cuatro filas en **~350 ms** (31-ago: `.120 .331 .335 .336`):
  `SEAL_EXPIRED {"basis":"wallclock",...}` → `LOCKOUT_CLEARED` → `DAY_CLOSED` → `DISARMED`.

### 2.5 Control opcional que contestaría una pregunta abierta

**Las 168 peticiones `ORDERS_CANCELLED` de toda la vida del producto llevan `count: 0`, las 168, y
no se determina si es porque no había nada resting o porque el barrido no las vio.**

**Si Roberto deja una orden límite viva, lejos del precio, ANTES de cruzar el límite**, el
`ORDERS_CANCELLED` del bloqueo dirá `count: 1` (y `orderIds` con su id) o dirá `count: 0`.
**Las dos respuestas cierran la pregunta.** Es el control más barato de la jornada.

---

## 3. PROCEDIMIENTO PARA ROBERTO

> **AGREGADO 2026-09-03, DESPUÉS DE SELLAR — no toca ninguna predicción, sólo el procedimiento.**
> Se anota como agregado en vez de editarlo en silencio: cambia **cómo se corre la prueba**, no **qué
> predecimos**. Las predicciones P-A, P-B y P-C quedan tal como se sellaron.

> ## ⚠ LO MÁS IMPORTANTE DEL PROCEDIMIENTO
>
> **DESPUÉS DE CRUZAR EL LÍMITE, ROBERTO TIENE QUE IRSE A HACER OTRA COSA DE VERDAD.**
>
> Otra ventana, otra tarea, algo que lo absorba: contestar un mail, mirar otro gráfico, atender el
> teléfono, lo que sea — **algo real, no una pausa de treinta segundos fingiendo que no mira.**
>
> **Si se queda mirando el panel, P-C no se puede medir**, y P-C —*¿se entera, y en cuánto?*— **es la
> predicción más valiosa de esta prueba.** Es la única pregunta del día que ningún test puede
> contestar y ningún archivo puede registrar. Si la desperdiciamos mirando, no vuelve: la próxima vez
> ya vamos a saber qué esperar.
>
> **Y NO MIRAR EL RELOJ ESPERANDO.** Esperar el momento es medir otra cosa. **Cuando te des cuenta de
> que algo pasó —sea cuando sea, en diez segundos o en cuarenta minutos— ahí mirás el reloj y anotás
> la hora.** No importa si es mucho: **si tardaste media hora, ese es el resultado**, y es el
> resultado que más nos enseña.

**Todo lo de interfaz gráfica lo hace él. Yo no toco NinjaTrader.**
**Aviso sobre los nombres de menú: el NinjaTrader de Roberto está en español y NO verifiqué las
traducciones exactas de la interfaz. Doy el nombre en inglés y dónde está; el rótulo en español
puede diferir.**

### Paso 0 — antes de empezar

- Que sea **antes de las 17:00 CT**, para que el sello venza el mismo día. Si arma después, queda
  bloqueado hasta mañana a las 17:00 CT.
- `Sim101` conectado y con datos de mercado de `MES` (el instrumento de los dos episodios previos).
- **No hace falta reiniciar NinjaTrader ni recompilar. NO apretar F5**: la prueba es sobre el binario
  cargado hoy, y un F5 lo cambiaría.

### Paso 1 — poner un límite chico, **con el guardián DESARMADO**

**Verificado hoy: no hay sello vigente, así que es seguro.** Con sello vigente esto escribe
`CONFIG_TAMPERED` y no toma efecto — **nunca editar `config.json` con el guardián armado.**

Abrir con el Bloc de notas:

```
C:\Users\home\Documents\NinjaTrader 8\deadman-guardian\config.json
```

Cambiar **una sola línea**:

```
  "personalDailyLossLimit": "600.00",     ->     "personalDailyLossLimit": "40.00",
```

Guardar y cerrar. **No tocar nada más del archivo.**

### Paso 2 — armar

En el panel del guardián, apretar **Arm**. El panel debe quedar verde, titular `ARMED`, detalle
`Watching Sim101. Entries allowed.`

*(Opcional, control de 2.5: antes de seguir, dejar una orden límite de compra viva muy por debajo del
precio, para que nunca se llene.)*

### Paso 3 — generar la pérdida hasta cruzar el límite, en `Sim101`

Con **4 contratos de MES**: cada tick vale $1.25 por contrato ⇒ **$5.00 por tick**, y **8 ticks
(2 puntos) en contra dan exactamente $40.00** — el mismo número exacto de los dos episodios previos,
que es lo que hace que esto pruebe el `>=` y no el `>`.

1. Comprar (o vender) **4 MES a mercado** en `Sim101`.
2. **Esperar a que el mercado se mueva 2 puntos en contra.** No hace falta cerrar nada: la pérdida
   que dispara es **no realizada**.
3. Si se mueve a favor, cerrar y volver a entrar en la otra dirección. No hay apuro.

*(Variante opcional y valiosa, si quiere quedarse un rato: hacer entradas y salidas seguidas a
mercado para acumular pérdida **REALIZADA**. Es el único camino que pone `grossRealizedPerAccount`
distinto de `0.00` y por lo tanto **el único que le da algo que comparar al cotejo contra la
plataforma** — hoy ese chequeo nunca tuvo nada que chequear. Ver el defecto G3-INSUMO.)*

### Paso 4 — **IRSE A HACER OTRA COSA** ← el paso que hace medible a P-C

**Apenas la posición esté puesta y la pérdida corriendo, irse.** Otra ventana, otra tarea, algo que
absorba de verdad. **No quedarse mirando el panel. No mirar el reloj esperando.**

Cuando te des cuenta de que algo pasó —**sea cuando sea**— ahí mirás el reloj y anotás la hora. Si
tardaste media hora, **ese es el resultado**, y es el mejor que esta prueba puede dar.

### Paso 5 — la orden posterior al bloqueo (el falsador de G8)

**Cuando ya te diste cuenta y anotaste la hora**, y con el panel diciendo `LOCKED`, mandar **una orden
nueva a mercado en `Sim101`** — 1 MES alcanza, en cualquier dirección, desde el DOM o desde el
gráfico.

**Se espera que entre y se llene.** No es un error: es exactamente lo que estamos midiendo. El
guardián la va a cerrar en el ciclo siguiente sin decir nada sobre la orden.

### Paso 6 — queda bloqueado hasta las 17:00 CT

No hay forma de desarmarlo antes, y **en `Sim101` eso no cuesta nada**: es una cuenta simulada, sin
dinero. Si quiere seguir operando de verdad hoy, usar otra cuenta — el guardián sólo vigila `Sim101`.

**Después de las 17:00 CT vuelve solo a `NOT ARMED`.** Y acordarse de devolver
`personalDailyLossLimit` a `"600.00"` cuando el sello ya no esté vigente.

---

## 4. LO QUE SÓLO ROBERTO PUEDE REGISTRAR

**Anotarlo EN EL MOMENTO, con la hora del reloj, no de memoria después.** El ledger es ciego a todo
esto y si no se anota **se pierde para siempre**. Ocho renglones:

1. **¿Sonó?** sí / no.
2. **¿Lo escuchaste?** sí / no / no estaba en la sala. *(Es otra pregunta que la anterior.)*
3. **Hora exacta en que te diste cuenta de que algo había pasado** — reloj, no "como cinco minutos".
4. **Cuánto tardaste desde la brecha hasta darte cuenta** — la resta contra la hora del
   `LIMIT_BREACHED`, que yo puedo darte del ledger.
5. **¿Qué decía el panel, textualmente?** copiarlo tal cual, con las mayúsculas que tenga.
6. **¿El panel tapaba algo?** ¿Estaba encima de un gráfico, de la ventana de órdenes, del DOM?
7. **¿Entendiste qué te estaba pidiendo?** sí / no / a medias — y qué creíste que había que hacer.
8. **¿Por dónde te enteraste?** el panel, el sonido, la posición que desapareció, la estrategia que
   NinjaTrader apagó, o fuiste a mirar por otra cosa.

---

## 5. Pendiente anotado, NO hecho

**`buildHash` en `GUARDIAN_STARTED`.** Hoy el ledger **no puede decir qué binario escribió ninguna de
sus 8.106 filas**: `GUARDIAN_STARTED` lleva sólo `{"state":...}`, y un `grep` de
`buildHash|addonBuild|coreBuild` sobre el ledger entero da **0**. `adapter.log` tampoco lo registra.
La única superficie que lo lleva es el **certificado** (`"buildHash":"a0709714bffc62b5"` en el del
31-ago), que se emite a pedido.

**Forma del arreglo:** un campo `buildHash` en el payload de `GUARDIAN_STARTED`, escrito en cada
arranque, encadenado como todo lo demás — **así nadie tiene que acordarse de mirar**. Es un campo
aditivo ⇒ contrato de extensión, la misma discusión que `configHash`.

**Va DESPUÉS de esta prueba, en el mismo F5 que lleve los dos commits del 2-sep** (`05d20bb` y
`21018bb`). A partir de ese F5 el ledger escribirá `needsHuman` en vez de `exhausted`, y **la
predicción P-A deja de aplicar** — por eso esta prueba se corre antes.
