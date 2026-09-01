# Correcciones para el sitio — texto final para pegar

**2026-09-01.** El sitio publica hoy una afirmación **medida como falsa**: que estando bloqueado toda orden
nueva *"is cancelled on sight"*. **Ni se bloquea, ni se cancela, ni se registra.**

> **Este archivo es TEXTO FINAL, no instrucciones.** Se pega tal cual en el editor web de GitHub.
> El sitio no vive en este repositorio y esta ventana **no lo toca**.

**Medición que obliga la corrección** (contra `deadman-guardian` @ `2036020`):

- `CancelAllOrders` tiene **un solo sitio de llamada**, dentro de `SweepRestingOrders`, cuyo único llamador
  es `EnterLockout` ⇒ **el barrido ocurre UNA vez, al entrar**.
- `OnOrderObserved` estando `Locked` **retorna sin actuar**.
- **`Ev.OrderRejectedLocked` no tiene ningún escritor** en `src/`. Los 12 del ledger son del **2026-08-26,
  entre 23:44:27.408Z y 23:44:33.826Z**, y no puede haber más.
- La decisión está firmada: **`AMENDMENTS.md` A11, 2026-08-26**.

**Regla que la destraba**: *bajar una afirmación nunca necesita evidencia; sólo subirla la necesita.*

**Orden**: primero se bajan estas siete. **Nada nuevo se agrega al sitio antes** — una página publicada
dentro de una superficie hereda la credibilidad de la superficie.

---

## 1 · `how-it-works.html` — el párrafo del lockout

**Buscar**: `<p><strong>A single flatten is not a lockout.</strong> While locked, every new order`

**Reemplazar el párrafo entero por:**

```html
<p><strong>A single flatten is not a lockout — and nothing is cancelled while locked. This is the
sentence to read twice.</strong> Entering the lockout sweeps the resting orders <em>once</em>. After
that, a new order — from the DOM, a chart, a running strategy — <strong>reaches your broker and can
fill</strong>: the guard neither cancels it nor records it. What the guard does is close the position
that order opens, on the next cycle.</p>

<p>Until 2026-08-26 it did cancel on sight, and a live test on a simulated account showed what that
costs: it cancelled <strong>its own flatten orders</strong> and the trader's own exits — twelve of
them inside six seconds, all sells and buy-to-covers — and the position never closed. Cancelling
wrongly can trap someone in a sinking position, which is unbounded and caused by us; not cancelling
costs one order's worth of exposure for one cycle. The trade was taken deliberately and it is written
down, with its restoration condition, in <a
href="https://github.com/Roberto9210/deadman-guardian/blob/main/AMENDMENTS.md">AMENDMENTS.md, A11</a>.
The event <code>ORDER_REJECTED_LOCKED</code> is not written any more, and the certificate's
<code>ordersRejectedWhileLocked</code> is <code>0</code> for that reason — truthfully, because nothing
is rejected.</p>
```

## 2 · `how-it-works.html` — el párrafo de *detect-and-cancel*

**Buscar**: `<p>Enforcement is <em>detect-and-cancel</em>, not prevention`

**Reemplazar el párrafo entero por:**

```html
<p>Nothing here can stop an order from reaching the venue, and that is a platform fact rather than a
design choice: 2,912 types were scanned inside the running NinjaTrader process and <strong>no
pre-submit hook exists</strong> — there is no event that can veto an order before submission. This
page used to quote a 315.9 ms cycle, of which 14.4 ms were ours. That measurement was real, and it
measured the cancel-on-sight mechanism that was removed on 2026-08-26, so it is not a claim about the
current build and it has been taken down rather than restated.</p>
```

## 3 · `how-it-works.html` — la lista de mediciones de plataforma

**Buscar**: `<li><strong>Detect-and-cancel takes 14.4 ms</strong>`

**Reemplazar ese `<li>` entero por:**

```html
  <li><strong>The one that follows from it</strong> — with no pre-submit hook, an order sent while
  locked reaches the venue and can fill, and what bounds it is the flatten on the next cycle rather
  than a cancellation. See "a single flatten is not a lockout", above.</li>
```

## 4 · `how-it-works.html` — el bloque de estado

**Buscar**: `26 of 26 named guarantees implemented, 137 collected test cases`

**Reemplazar esa frase por:**

```html
  <p>26 of 26 named guarantees implemented, all passing, 0 skipped — that is the conformance
  statement, rather than "it works". A test count used to sit here and was removed rather than
  updated: the number climbs on its own, says nothing about coverage, and is wrong again the following
  week. The soak run quoted below is from <strong>2026-08-21</strong> and <strong>predates</strong>
  three findings that followed it, the last of which showed the guard stopped enforcing after its
  first verified flatten. It has not been re-run, and today it cannot be: one of its assertions
  describes behaviour that was removed on 2026-08-26.</p>
```

## 5 · `index.html` — el bullet del lockout

**Buscar**: `<li><strong>A single flatten is not a lockout.</strong> While locked, every new order`

**Reemplazar ese `<li>` entero por:**

```html
  <li><strong>A single flatten is not a lockout — and nothing is cancelled while locked.</strong>
  Entering the lockout sweeps the resting orders once; after that a new order reaches your broker and
  can fill, and what closes the position it opens is the flatten on the next cycle. It used to cancel
  on sight, until a live test showed it cancelling its own flatten orders and the trader's own exits.
  <a href="how-it-works.html">The trade, and why it was taken →</a></li>
```

## 6 · `index.html` — "It does not bound your loss"

**Buscar**: `Measured inside the running platform, the full detect-and-cancel`

**Reemplazar desde ahí hasta `belonged to NinjaTrader and the venue.` por:**

```html
Between reaching your limit and the position actually closing, the market keeps moving — and the
closing is done by a flatten that has to be <em>observed</em> to succeed, not assumed. A gap or a fast
market goes straight through that.
```

## 7 · `index.html` y `compatibility.html` — los números que quedan

**En `index.html`, buscar** `named guarantees under 137 tests` → **reemplazar por** `named guarantees`.

**En `index.html`, buscar** `and <strong>137 tests</strong>, and the conformance statement` →
**reemplazar por** `, and the conformance statement`.

**En `compatibility.html`, buscar** `Measured inside the running platform, the whole detect-and-cancel` →
**reemplazar desde ahí hasta** `platform and the venue.` **por:**

```html
The closing is done by a flatten at market, and it has to be observed to succeed rather than assumed.
A gap or a fast market moves through that.
```

---

## Lo que NO se toca, y por qué

- **`no pre-submit hook exists` y los 2.912 tipos**: sigue siendo cierto y sigue estando medido dentro del
  proceso. Es la mitad de la frase vieja que sobrevive.
- **`26 of 26`**: estable y comprobable. Es la afirmación que se queda cuando se va el conteo.
- **El soak del 21-ago**: se queda **con su fecha y su limitación**, no se borra. Ocurrió.
