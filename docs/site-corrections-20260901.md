# Correcciones para el sitio — texto final para pegar

**2026-09-01. Segunda redacción.** La primera cambiaba una afirmación **falsa** por una **no ganada**:
decía que *"el aplanado del ciclo siguiente cierra la posición"*, que es exactamente la frase que
después medimos con **n=1**. Se reescribió antes de pegar nada.

> **Este archivo es TEXTO FINAL, no instrucciones.** Se pega tal cual en el editor web de GitHub.
> El sitio no vive en este repositorio y esta ventana **no lo toca**.

## Lo que el sitio afirma hoy y está medido como falso

*"While locked, every new order — from the DOM, a chart, a running strategy — is cancelled on sight
and recorded as `ORDER_REJECTED_LOCKED`."* **Ni se cancela, ni se registra, ni se impide que salga.**

## Lo único publicable, y por qué es esto y no más

| medido | fuente |
|---|---|
| `CancelAllOrders` tiene **un solo sitio de llamada**, en `SweepRestingOrders`, cuyo único llamador es `EnterLockout` ⇒ **el barrido ocurre una vez, al entrar** | `deadman-guardian` @ `72e75af` |
| `OnOrderObserved` estando `Locked` **retorna sin actuar** | ídem |
| **`ORDER_REJECTED_LOCKED` no tiene escritor** en `src/` | ídem |
| **Un (1) `FLATTEN_VERIFIED`** en toda la vida del producto | ledger, 8.034 entradas |
| **Un episodio de 167 intentos sin cerrar la posición** | ledger, 2026-08-26 |
| **`MaxFlattenAttempts = 3` NO gobierna ningún bucle**: sólo prende el flag `exhausted` y `LockoutNeedsHuman`. El flag prendió en el intento **3** y el guardián siguió **164 veces más** — 165 de los 169 llevan `exhausted: true` | `Guardian.cs`, `Constants` + ledger |
| El `LOCKOUT_INCOMPLETE` que disparó **no lleva causa**: `{accounts, attempts, exhausted}` | ledger, 169 de 169 |

**Bajar una afirmación no necesita evidencia; afirmar una cota sí, y no la tenemos.** Por eso el texto
de abajo dice *"intenta aplanar"* y **nunca** *"lo cierra en el ciclo siguiente"*.

---

## 1 · `how-it-works.html` — el párrafo del lockout

**Buscar**: `<p><strong>A single flatten is not a lockout.</strong> While locked, every new order`

**Reemplazar el párrafo entero por:**

```html
<p><strong>A single flatten is not a lockout — and nothing is cancelled while locked. This is the
sentence to read twice.</strong> Entering the lockout sweeps the resting orders <em>once</em>. After
that, a new order — from the DOM, a chart, a running strategy — <strong>reaches your broker and can
fill</strong>: the guard neither cancels it nor records it.</p>

<p>What the guard does instead is <strong>keep trying to flatten, and verify rather than assume</strong>:
every cycle it asks the platform to flatten, then reads the positions back, and if they are not flat it
writes <code>LOCKOUT_INCOMPLETE</code> and tries again. <strong>Here is what that has actually done, in
full.</strong> In the product's entire recorded life there is <strong>one</strong> verified flatten. There
is also one episode — 2026-08-26, on a simulated account — where it tried <strong>167 times and the
position never closed</strong>. <strong>It did not give up, because it has no limit to give up at:</strong>
from the third attempt onwards it marks the failure and tells you it needs you, and then it keeps
trying — that day, 164 more times. The episode had a cause we found and fixed the same day, and it is
also the only large sample we have. <strong>So "the position gets closed" is not something this page can
promise you: it is something the guard attempts, reports honestly when it fails, and never stops
attempting.</strong></p>

<p>The failure is also, today, <strong>undiagnosable from the record</strong>: the event carries a
counter and no reason, so a broker that did not confirm and a position that reopened look identical in
the log. That is an open defect, and it is named here rather than left for you to discover.</p>

<p><strong>Which means this does not replace watching your platform, and it is not built to.</strong>
If the guard says it needs you — that is the panel turning red and writing
<code>LOCKOUT_INCOMPLETE</code> — <strong>go and check the flatten yourself in NinjaTrader</strong>.
When the last brake does not verify, a person is what closes the gap, and that is the one instruction
this page can honestly give you today.</p>

<p>Until 2026-08-26 the guard did cancel on sight, and a live test showed what that costs: it cancelled
<strong>its own flatten orders</strong> and the trader's own exits — twelve of them inside six seconds,
all sells and buy-to-covers. Cancelling wrongly can trap someone in a sinking position, which is
unbounded and caused by us; not cancelling costs one order's worth of exposure. The trade was taken
deliberately and it is written down, with its restoration condition, in <a
href="https://github.com/Roberto9210/deadman-guardian/blob/main/AMENDMENTS.md">AMENDMENTS.md, A11</a>.
<code>ORDER_REJECTED_LOCKED</code> is not written any more, and the certificate's
<code>ordersRejectedWhileLocked</code> reads <code>0</code> for that reason — truthfully, because
nothing is rejected.</p>
```

## 2 · `how-it-works.html` — el párrafo de *detect-and-cancel*

**Buscar**: `<p>Enforcement is <em>detect-and-cancel</em>, not prevention`

**Reemplazar el párrafo entero por:**

```html
<p>Nothing here can stop an order from reaching the venue, and that is a platform fact rather than a
design choice: 2,912 types were scanned inside the running NinjaTrader process and <strong>no
pre-submit hook exists</strong> — there is no event that can veto an order before submission. This page
used to quote a 315.9 ms cycle, of which 14.4 ms were ours. That measurement was real, and it measured
the cancel-on-sight mechanism that was removed on 2026-08-26, so it is not a claim about the current
build and it has been taken down rather than restated.</p>
```

## 3 · `how-it-works.html` — la lista de mediciones de plataforma

**Buscar**: `<li><strong>Detect-and-cancel takes 14.4 ms</strong>`

**Reemplazar ese `<li>` entero por:**

```html
  <li><strong>What follows from having no hook</strong> — an order sent while locked reaches the venue
  and can fill. What the guard does after that is attempt a flatten and verify it; how often that has
  actually verified is stated above, in full.</li>
```

## 4 · `how-it-works.html` — el bloque de estado

**Buscar**: `26 of 26 named guarantees implemented, 137 collected test cases`

**Reemplazar esa frase por:**

```html
  <p>26 of 26 named guarantees implemented, all passing, 0 skipped — that is the conformance
  statement, rather than "it works". A test count used to sit here and was removed rather than updated:
  the number climbs on its own, says nothing about coverage, and is wrong again the following week. The
  soak run quoted below is from <strong>2026-08-21</strong> and <strong>predates</strong> three findings
  that followed it, the last of which showed the guard stopped enforcing after its first verified
  flatten. It has not been re-run, and today it cannot be: one of its assertions describes behaviour
  that was removed on 2026-08-26.</p>
```

## 5 · `index.html` — el bullet del lockout

**Buscar**: `<li><strong>A single flatten is not a lockout.</strong> While locked, every new order`

**Reemplazar ese `<li>` entero por:**

```html
  <li><strong>A single flatten is not a lockout — and nothing is cancelled while locked.</strong>
  Entering the lockout sweeps the resting orders once; after that a new order reaches your broker and
  can fill. The guard then keeps trying to flatten and verifies rather than assumes — and the honest
  record is one verified flatten in the product's life, plus one episode where it tried 167 times
  without closing the position. <strong>So it does not replace watching your platform: if it says it
  needs you, check the flatten yourself.</strong> <a href="how-it-works.html">What that means, in
  full →</a></li>
```

## 6 · `index.html` — "It does not bound your loss"

**Buscar**: `Measured inside the running platform, the full detect-and-cancel`

**Reemplazar desde ahí hasta `belonged to NinjaTrader and the venue.` por:**

```html
Between reaching your limit and the position actually closing, the market keeps moving — and the
closing is an <em>attempt</em> that has to be observed to succeed, not a step that is assumed to work.
A gap or a fast market goes straight through that, and so does a flatten that does not verify.
```

## 7 · `index.html` y `compatibility.html` — los números que quedan

**En `index.html`, buscar** `named guarantees under 137 tests` → **reemplazar por** `named guarantees`.

**En `index.html`, buscar** `and <strong>137 tests</strong>, and the conformance statement` →
**reemplazar por** `, and the conformance statement`.

**En `compatibility.html`, buscar** `Measured inside the running platform, the whole detect-and-cancel` →
**reemplazar desde ahí hasta** `platform and the venue.` **por:**

```html
The closing is a flatten at market, and it is an attempt that has to be observed to succeed rather than
a step assumed to work. A gap or a fast market moves through that, and so does a flatten that does not
verify.
```

---

## Lo que NO se toca, y por qué

- **`no pre-submit hook exists` y los 2.912 tipos**: sigue medido dentro del proceso y sigue siendo
  cierto. Es la mitad de la frase vieja que sobrevive.
- **`26 of 26`**: estable y comprobable. Es la afirmación que queda cuando se va el conteo.
- **El soak del 21-ago**: se queda **con su fecha y su limitación**. Ocurrió.

## Y lo que este texto NO dice, a propósito

- **No dice que la posición se cierre.** Dice que se intenta y que se verifica, y publica el resultado
  real de esos intentos. La cota *"bounded by one cycle"* existe en `AMENDMENTS.md` A11 y tiene **n=1**;
  no se repite acá.
- **No promete que el defecto del registro se va a arreglar.** Lo nombra.
- **No usa la palabra `exhausted`.** El flag existe en el payload y **anuncia un tope que no existe**:
  `MaxFlattenAttempts = 3` no gobierna ningún bucle. Decir *"167 attempts were exhausted"* en la página
  afirmaría que había un límite y se alcanzó — **la misma clase que la página existe para corregir**.
- **No agrega NADA nuevo sobre el certificado.** La única frase que lo menciona
  —`ordersRejectedWhileLocked` en `0`— es verdadera y ya estaba. **Pero el certificado tiene hoy un campo
  que sabemos mal** (`lockoutsTriggered` no cuenta `CONFIG_TAMPERED` ni `SEAL_MISMATCH`, y
  `limitRespected` cuelga de él), y una página pública que avala un campo **le presta credibilidad a la
  tabla entera**. Nada más sobre el certificado hasta que el contador esté arreglado.
