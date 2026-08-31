# El canal — diseño

**2026-08-31.** Diseño, sin código. Nace del hallazgo del día: el guardián pidió ayuda 165 veces
el 26-ago y la persona a la que se lo pedía contestó, cinco días después, **"no me di cuenta"**.

---

## 0. La regla de frontera, que hoy falló tres veces

Antes de decir que algo espera a que la plataforma esté libre:

> **¿Esto necesita de verdad la plataforma, o sólo necesita Windows / el disco?**

Tres veces hoy puse la frontera más adentro de lo que corresponde:

| # | dije | era |
|---|---|---|
| 1 | *"el certificado no se puede verificar sin desplegar"* | el certificado sí, pero el hash del binario se lee del archivo bloqueado |
| 2 | *"`ShowInTaskbar` con `ToolWindow` no se resuelve sin correrlo"* | **WPF puro**: dos ventanas de sonda contestaron en tres segundos, sin NT8 |
| 3 | *"el escaneo de tipos necesita producción desarmada"* | **`STEP3_FINDINGS §4` ya lo había hecho fuera de proceso.** Reflexión sobre el disco |

**Lo que de verdad exige estar dentro de NT8** es sólo la conducta en runtime: si un evento
*dispara*, si NT8 *suprime* algo, valores vivos como `Account.All`, y si un sonido **se oye**.
Todo lo que sea *"¿qué existe y con qué firma?"* se contesta desde el disco, ahora.

**Corroboración de que el método es el mismo**: el escaneo de disco de hoy cuenta **2.912 tipos**
en `NinjaTrader.Core.dll` — el número exacto que publica `STEP3_FINDINGS`.

---

## 1. Sonido: lo que existe, verificado por reflexión

### Llamable desde un AddOn — `public static`

```
NinjaTrader.Core.Globals.PlaySound(SoundType soundType, Account account)
NinjaTrader.Core.Globals.PlaySound(String file)
System.Media.SystemSounds.{Asterisk|Beep|Exclamation|Hand|Question}.Play()
System.Media.SoundPlayer.{Play|PlayLooping|PlaySync|Stop}
```

### NO llamable, y casi lo doy por bueno

`NinjaTrader.NinjaScript.NinjaScript.PlaySound(String)` **existe y es público, pero es de
instancia** (`static=False`), mientras su hermano `Log` es estático. Pertenece a una instancia de
NinjaScript —indicador o estrategia—; **un AddOn no es una.**

Lo di por disponible en una primera pasada porque mis flags de reflexión incluían `Instance`.
Apareció al pedir la firma exacta. **Es la clase de la casa: una lectura cierta sobre el conjunto
equivocado**, esta vez el conjunto de flags.

### El sonido a usar: `SoundType.Announcement`

Los 13 valores de `SoundType` son semánticos de NT8 —`OrderFilled`, `StopFilled`,
`ConnectionLost`…—. **`Announcement` es el único que no miente.** Usar `OrderFilled` para nuestra
alerta sería una mentira en el canal de audio, y hoy ya sacamos una de un canal.

---

## 2. Cadencia: inmediato, después cada cinco minutos, plano

**La cadencia es lo que decide si el acuse conserva su significado.** Un sonido continuo o cada
minuto se manotea sin pensar — es una alarma, y el botón que la calla se aprieta por reflejo. A
cinco minutos cada repetición llega habiendo vuelto a la línea de base: se procesa como
información nueva, y apretar el acuse es un acto deliberado.

| | |
|---|---|
| primero | **inmediato**. La condición acaba de nacer; la primera alerta no espera |
| después | cada **5 minutos**, indefinidamente **mientras no haya acuse** |
| escalado | **ninguno.** Plano |
| techo natural | el corte de sesión: la condición se resuelve o el sello expira |

**Por qué indefinido y no acotado**: el trabajo real del sonido es alcanzarlo **cuando no está**.
Si se levantó tres horas, lo agarra al volver. Un sonido que se apaga solo pierde exactamente el
caso que vinimos a resolver.

**Por qué plano y no escalado**: *"no escalar" es el techo más simple que existe.* Un escalado sin
límite superior reconstruye la tormenta de las 165 con altavoz. Y si el canal está muerto, subir
el volumen no arregla nada — lo que sirve es **decirlo** (§3).

---

## 3. Salud del canal — pieza propia, y es lo mejor de todo esto

Es gratis, no necesita acuse, no necesita ledger y **no necesita el contrato de extensión**.

`Globals.GeneralOptions` es `public static` (verificado), y expone:

```
SoundVolume            : Double   (public)
SoundAnnouncement      : String   (public)   <- el archivo que suena
SoundPlayConsecutively : Boolean  (public)
```

**El guardián puede leer si su propio canal está vivo antes de confiar en él**: volumen en cero,
o archivo ausente, significan que `PlaySound` no va a devolver ningún error y no se va a oír nada.
Falla en silencio — y ahora es detectable.

**Y el producto puede DECIRLO en el panel:**

> *Intenté avisarte con un sonido y tu volumen está en cero.*

**Es el guardián informando sobre la salud de su propio canal**, que es exactamente lo contrario
de lo que hacía esta mañana: creer que había avisado. Convierte una parte de lo inobservable en
observable **sin pedirle nada al humano**.

---

## 4. Acuse de recibo

### El ataque, y la resolución

Si el acuse **calla el sonido**, su significado se degrada de *"lo estoy viendo"* a *"hacé que se
calle"*, lo aprieta el reflejo, y es el botón de esconder con nombre noble.

Si el acuse **no calla el sonido**, el trader que ya vino y ya está cerrando su posición sigue
escuchándolo mientras trabaja: puro costo, ninguna atención adicional, y enemistad con el
producto.

**La salida no era desacoplar: era la cadencia.** A cinco minutos no hay reflejo que manotear, así
que el acuse **sí** para el sonido sin perder su significado. Un gesto, no dos.

### Las tres restricciones, en el diseño desde el principio

1. **El acuse no oculta nada.** Registra que alguien vio; el panel sigue igual hasta que la
   condición se resuelva. Si ocultara, sería el candidato 9 otra vez.
2. **El acuse no cambia nada de la aplicación.** Es una observación, no un permiso: no desbloquea,
   no pausa, no ablanda.
3. **Va al ledger**, y está permitido porque *"un humano vio esto a las HH:MM"* es un **hecho**, no
   un evento que signifique *"esto no cuenta"*. La regla que prohíbe lo segundo queda intacta.

### La evidencia es ASIMÉTRICA, y hay que escribirlo para que nadie la lea al revés

> **La ausencia prueba mucho. La presencia prueba un clic.**

*"Pidió a las 18:44 y nadie acusó en cinco días"* es evidencia fuerte y es exactamente el hueco de
hoy. *"Alguien acusó"* **no prueba que alguien actuó** — prueba que alguien apretó un botón. Nadie
debe leer un acuse como prueba de acción.

Y la ausencia tampoco distingue *"no había nadie"* de *"estaba y decidió no actuar"*.

### Lo que esto le hace al ledger, y es más grande que el acuse

Sería **el primer evento cuyo sujeto es la atención de una persona, no la cuenta.** El ledger pasa
de *"qué hizo el guardián"* a incluir *"qué supo el humano"*.

No es un desvío del **candidato 5** —*el ledger audita los hechos del guardián y no sus palabras*—
**es el candidato 5 aplicándose.**

**Consecuencia para Ventana B: lo que hay que negociar no es un campo, es una CATEGORÍA.** Un
formato que sólo contempla hechos sobre la cuenta va a tener que decidir si admite hechos sobre la
interacción, y **esa decisión se toma una vez**.

---

## 5. Orden de trabajo

| # | qué | bloqueado por |
|---|---|---|
| 1 | **salud del canal** + decirlo en el panel | **nada** — se puede ya |
| 2 | **sonido**, cadencia inmediata + 5 min, plano | nada del formato |
| 3 | **contrato de extensión** con Ventana B — categoría, no campo | Ventana B |
| 4 | **acuse de recibo** | el punto 3 |

---

## 6. Anotado y no perseguido

- **Candidato 11**: `NinjaTrader.Adapter.AccountLockoutNotifications` — público, con evento `Push`
  y método `Raise`. Un tipo de NT8 con ese nombre exacto es demasiado cercano a lo que hace este
  producto para mirarlo con el sello corriendo.
- `Globals.RmsOptions` (Risk Management System) apareció en el mismo barrido. Mismo criterio.
