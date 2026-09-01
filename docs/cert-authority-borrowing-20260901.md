# Campos que piden prestada credibilidad — certificado, lado emisor

> **Las citas `archivo:linea` de este documento se midieron contra el commit `5d5eb27`.**
> Una cita de linea es el hash de un momento (regla 11 del metodo): si un numero no coincide,
> es **deriva**, no mentira — compara commits y usa el **nombre del simbolo**, que es la
> referencia buena. Esta linea caduca sola: en cuanto el archivo se mueve, el lector lo ve.

**2026-09-01. Sólo la lista, ningún arreglo** (pedido del operador).
Regla que la origina, medida en el verificador por Ventana B:

> ### UN CAMPO IMPRESO DENTRO DE UN VEREDICTO HEREDA LA AUTORIDAD DEL VEREDICTO.

Su caso: el verificador imprime `VALID (keyId=key-ALPHA)` cuando el documento **reclama** `key-ALPHA`
y firmó otra clave. El campo nunca se verifica; está ahí sólo para interpolarse en la línea del
veredicto, y el lector entiende *"lo firmó key-ALPHA y lo comprobé"*.

**La pregunta aplicada acá**: ¿qué campos NO verificados se renderizan junto a afirmaciones que sí lo
están? El HTML los pone **a todos en una sola tabla**, sin marca de procedencia, así que la
convivencia es total: cada fila hereda la credibilidad de las demás.

---

## 0 · La keystone, y es exactamente la forma de su hallazgo

> ### `ledgerVerified` NO ES UNA VERIFICACIÓN. Es un parámetro.

`Certificate.Issue(entries, state, request, **bool chainVerified**, signer)` — el emisor **no verifica
ninguna cadena**. Copia lo que le pasan a `claims.ledgerVerified`, y lo imprime **como última fila de
la tabla de números contados**.

- **Es la única fila cuyo nombre suena a comprobación**, y es la que el emisor no hizo.
- **`limitRespected` se calcula a partir de ella** (`Certificate.cs:56`), así que el veredicto más
  fuerte del documento hereda esa confianza sin comprobarla.
- **Hoy el valor es correcto** —el addon pasa `verify.Ok` de `VerifyLedger()` (`:413`)—, y por eso es
  peligroso: **la mitad medida es verdadera**, y lo que no aguanta es la estructura. Un llamador
  distinto, o el mismo con un bug, imprime `true` sin que nada lo note.

---

## 1 · Los cinco del SELLO — impresos sin comprobar el sello

`Issue` **nunca llama `SnapshotMatchesHash()`**. La única llamada en todo el repo está en
`Guardian.cs:223`, en el arranque. Un certificado emitido a las 16:55 **no re-verifica** que el
snapshot sellado siga correspondiendo a su hash.

| fila del HTML | de dónde sale |
|---|---|
| `timezone` | snapshot sellado |
| `armed at (UTC)` | `seal.ArmedAtUtc` |
| `seal expiry (UTC)` | `seal.ExpiresAtUtc` |
| **`personal daily loss limit`** | snapshot sellado |
| **`firm daily loss limit`** | snapshot sellado |

**Las dos últimas son la promesa entera del documento** — el número contra el que se juzga todo lo
demás — y se imprimen junto a `lockoutsTriggered` y `ledgerVerified` sin que el emisor haya
comprobado el sello del que salieron.

**Matiz que corresponde, y evita exagerar**: el guardián **sí** verifica el sello al arrancar y
**bloquea** si no coincide (`SEAL_MISMATCH` ⇒ lockout), así que un sello adulterado no llega lejos en
un proceso vivo. Lo que no existe es la comprobación **en el momento de emitir**, que es cuando el
documento se firma con esos números.

También sale de ahí, en el JSON y no en el HTML: `subject.accounts[]`, los hashes de las cuentas
guardadas.

## 2 · Los que el llamador provee y nadie contrasta

| campo | quién lo pone | comprobación |
|---|---|---|
| **`alias`** | el trader, en `alias.txt` | **ninguna** — sólo se rehúsa si falta (`C7`) |
| **`trust level`** | derivado de si el llamador pasó `anchors` | **ninguna**; hoy siempre `L1` porque nada llena `anchors` |
| `issuer.version` / `issuer.buildHash` | el llamador (JSON, no HTML) | el hash es una medición real **del archivo**, y está nombrado así a propósito |

`alias` encabeza la tabla. Es el nombre bajo el cual se lee todo lo demás, y es texto libre.

## 3 · Los que el emisor SÍ calcula — con la deuda de la keystone

`lockoutsTriggered`, `changeAttemptsWhileSealed`, `ordersRejectedWhileLocked`, `clockAnomalies`,
`ledgerRange`, `failClosedEpisodes`, `daysCovered`, `certHash`.

Son cómputos genuinos sobre las entradas recibidas — **pero sobre entradas cuya cadena el emisor no
verificó** (§0). Su aritmética es correcta; su autoridad se apoya en la fila que no comprobó nada.

## 4 · El contraejemplo, que es lo que hace creíble a esta lista

**`day` SÍ se verifica contra el ledger.** Desde cert-1, un día que el ledger no delimita se **rehúsa**
(`CERT_DAY_NOT_IN_LEDGER`). No todo lo que se imprime es palabra del llamador, y por eso la mezcla es
más engañosa: **hay filas comprobadas de verdad en la misma tabla.**

## 5 · Fuera de la tabla, y es de los dos lados

El HTML cierra con:

```
Verify this yourself, without asking us anything:
    pip install deadman-kit
    python -m deadman.verify_certificate certificate.json ledger.jsonl
```

Cadenas fijas (`Certificate.cs:542-545`). **No es una afirmación del emisor sobre los datos**, pero sí
una invitación a confiar en la herramienta del otro lado — **la misma cuya línea de veredicto acaba de
medirse imprimiendo un `keyId` que no comprueba**. Se anota como ítem de coordinación, no como defecto
de este repo.

---

## 5b · `limitRespected` es un booleano y los hechos son TRES (1-sep, tarde)

Hoy: `BreachLockouts == 0 && ningún episodio fail-closed abierto && ChainVerified`.

**No son tres condiciones del mismo hecho: son tres hechos sobre sujetos distintos**, y sólo el
primero habla del trader.

| término | de quién habla | qué significa `false` |
|---|---|---|
| `BreachLockouts == 0` | **del trader** | incumplió su límite |
| ningún episodio abierto | **del guardián** | **no pudo saberlo** |
| `ChainVerified` | **de la evidencia** | el registro no se puede verificar |

**Los tres salen impresos idénticos.** Un `false` acusa al trader de algo que en dos de los tres casos
no hizo — la clase de la casa en el campo del que cuelga la promesa entera.

### Alcanzabilidad, medida sobre el ledger

| | |
|---|---|
| episodios fail-closed | **13 — y los 13 CERRARON.** Ninguno quedó abierto jamás |
| cuándo | **todos el 2026-08-21 y 22.** Ninguno en los últimos 10 días |
| duración típica | 5–20 segundos |
| **el más largo** | **1 h 01 m 45 s** (22-ago, 19:58:16 → 21:00:01Z); tres pasaron los 30 min |

⇒ **nunca se capturó un episodio abierto en un certificado, y sin embargo es alcanzable de sobra**: un
certificado emitido dentro de aquella hora habría impreso `limitRespected: false` **sobre un día sin
ninguna brecha**. Y las 16:55 —el trader exportando su jornada— es justo el tipo de momento.
**Matiz que corresponde**: los 2.946 `ACCOUNT_UNKNOWN` **no** son 2.946 episodios; un episodio cubre
muchos ticks. La condición común es la desconexión; el episodio es raro y **largo**.

### Mi lectura de las tres salidas

**Contra la tercera (que `Issue` rehúse), y coincido con el operador — con un motivo más.** Las dos
negativas que sí escribí hoy —zona ausente, sello que no coincide— son de otra clase: ahí **el sujeto
del documento era ilegible**. Acá **un campo queda indeterminado y el resto del día está perfecto**, y
rehusar tiraría también la brecha real, los episodios y el rango. **La negativa tiene que ser
proporcional a lo que no se sabe.**

**Contra la segunda (dos booleanos), y el motivo es un peligro concreto.** Si `limitRespected` pasa a
ser sólo `BreachLockouts == 0`, entonces un día con la cadena rota y sin brecha imprime **`true`**, y
un consumidor viejo que lea sólo ese campo ve *"limpio"* sobre un registro que no se puede verificar.
**Eso mueve la mentira al lado que parece más seguro, que es peor que donde está hoy.** Sólo funciona
si leer `determinable` fuera obligatorio, y eso no se puede imponer.

**A favor de la primera (tres valores: `respected` / `breached` / `undetermined`)**, y el argumento que
decide no es de tipos sino **de quién lee**:

> **El certificado también lo lee un humano, en el HTML.** Una omisión **no se ve** en una tabla —
> nadie nota una fila que no está. **`undetermined` se imprime**, ocupa su renglón, y alguien puede
> preguntar por él.

Ésa es la diferencia con la forma de omitir que esta casa usa en otros campos (`issuer.keyId`,
`toSeq`, `triggerEvent`): **ahí el lector es una máquina; acá es una persona con una tabla delante.**

**Costo, dicho sin adornos**: es un **cambio de tipo** (`bool` → cadena), el más duro para un
verificador, y por eso va entero a la pila del contrato. **Y la cadena rota debe mapear a
`undetermined`, no a `true`** — si no, se reintroduce el peligro de la opción 2 por la puerta de atrás.

### FALLO DEL OPERADOR (1-sep): **opción 1, tres valores**, con dos condiciones

**Condición 1 — la cadena rota mapea a `undetermined`, NUNCA a `true`.** Escrita acá para que no se
pierda en la implementación.

**Condición 2 — tres valores SIGUE SIENDO UN COLAPSO, sólo que uno mejor.** `undetermined` no dice
**cuál** de los dos sujetos quedó indeterminado, y para un lector no da igual: *"el guardián estuvo
ciego 40 minutos"* es un problema de la herramienta, y *"el registro no es verificable"* es una alarma
sobre la integridad de la evidencia — **la segunda asusta mucho más, con razón**.

**Y NO se agrega un campo para eso, porque el motivo YA ESTÁ IMPRESO**: `failClosedEpisodes` y
`ledgerVerified` están los dos en el documento. **Lo que falta es de RENDERIZADO**: que el HTML ponga
`undetermined` **al lado de lo que lo causó**, en vez de en una tabla de números sueltos. Es
exactamente el defecto de préstamo de autoridad de §0-§3 de este mismo documento —una tabla sin marca
de procedencia donde cada fila hereda la credibilidad de las demás— **y acá el arreglo es barato y no
toca el esquema**.

### Por qué la condición que lo vuelve alcanzable DEJÓ de ocurrir — **HAY REGISTRO, y no es del guardián**

Los 13 episodios son del 21 y 22 de agosto y no hubo ninguno en 10 días. Medido:

| | |
|---|---|
| **commits el 22, 23 y 24 de agosto** | **CERO.** No hubo cambio de código |
| lo que sí cambió | **`Config.xml.before-connectonstartup-20260822-095728`** — un respaldo de la config de NinjaTrader con la fecha en el nombre: **se activó "connect on startup" el 2026-08-22 a las 14:57:28Z** |
| el episodio en curso | entró 14:24:26Z y **cerró 14:58:11Z — 43 segundos después del cambio** |
| después | **un episodio más** esa tarde (19:58→21:00Z), y **ninguno desde entonces** |

> **"Dejó de pasar" NO es "está arreglado".** Paró por un **ajuste de plataforma en ESTA máquina**, no
> por nada del guardián. Vuelve solo en cuanto ese ajuste no esté — **y el valor de fábrica de NT8 es
> no conectarse al arrancar**, así que **toda instalación nueva empieza en la condición que produce
> estos episodios.**

⇒ **va al CUARTO estado del inventario: *no producible acá, universal afuera*.** Y confirma la
indicación: **se construye en un test**, como el canal de sonido roto, en vez de esperar a que vuelva.

## 6 · Lo que esta lista NO dice

1. **No dice que ningún valor sea falso hoy.** Dice **cuáles no fueron comprobados por quien los
   imprime**, que es una propiedad de la estructura y no del día.
2. **No propone arreglos**: ni marcar procedencia en el render, ni verificar el sello al emitir, ni
   cambiar la firma de `Issue`. Cualquiera de los tres cambia lo que un verificador ve.
3. **No cubre el verificador**, que es de Ventana B.
