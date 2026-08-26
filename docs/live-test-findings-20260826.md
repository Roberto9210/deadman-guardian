# El guardián cancela sus propias órdenes de aplanado

**Informe de la prueba viva del 2026-08-26. Sesión `dayKey 2026-08-27`, límite $40, Sim101.**
Nada arreglado: este documento es el hallazgo, no la solución.

El feed fue **`Trasmisión de datos simulados` = Simulated Data Feed, precios sintéticos**. La prueba
valida el **mecanismo**, no comportamiento contra mercado real. Ningún resultado de acá puede usarse
para ablandar lo que dice `SPEC §17`.

---

## 1. La traza

### Lo que funcionó

```
seq 7141  ARMED                  23:40:40.109  dayKey=2026-08-27
seq 7148  PNL_BASELINE_ADOPTED   23:40:56.099  adopted=0.00  platform=0.00  coreCheckpoint=none
seq 7150  LIMIT_BREACHED         23:44:27.314  dayLoss=40.00  limit=40.00
```

El breach disparó **exactamente en el límite** — la semántica `>=` de `SPEC §8` sobre fills reales, no
sintéticos. Option A corrió por segunda vez en el día, esta vez dentro de la sesión de prueba, con una
ventana ciega de **73 ms** (predicha en 75 ms a partir de la medición de la mañana).

### Lo que no

**`FLATTEN_VERIFIED`: cero, en toda la sesión.**

La orden de aplanado del propio guardián, en el log de NinjaTrader — se llama `Cerrar` porque NT8 está
en español:

```
18:44:27.407  Cerrar ee21b310…  Sometido          (Vender 1 MES SEP26, Mercado)
18:44:27.408  ← el guardián escribe ORDER_REJECTED_LOCKED seq 7152, action=Sell
18:44:27.516  Cerrar ee21b310…  Aceptado
18:44:27.517  Cerrar ee21b310…  Pedido Cancelado    ← 1 ms después de ser aceptada
18:44:27.522  Cerrar ee21b310…  Funcionando
18:44:27.524  Cerrar ee21b310…  Pedido Cancelado
18:44:27.644  Cerrar ee21b310…  CANCELADO           ← nunca se llenó
```

**110 ms desde el envío hasta el pedido de cancelación. 237 ms hasta la cancelación efectiva.**

### El bucle

```
seq 7151-7154   ORDERS_CANCELLED count=0 → FLATTEN_REQUESTED → LOCKOUT_INCOMPLETE attempts=1
seq 7157-7159                             …                    attempts=2
seq 7163-7165                             …                    attempts=3   exhausted=true
…
seq 7661-7663                             …                    attempts=167 exhausted=true
```

**514 eventos en 2 min 47 s** (seq 7150–7663), a ~1 ciclo por segundo, hasta que NinjaTrader se cerró.
El ledger creció a 2,5 MB. Sin cierre, habría seguido hasta la expiración del sello de mañana.

`ORDERS_CANCELLED count=0` en cada vuelta porque al enumerar órdenes vivas todavía no había ninguna:
la orden `Cerrar` se emite **después** de esa enumeración, y muere antes de la siguiente.

### Los `ORDER_REJECTED_LOCKED`, y lo que son

Doce, y **todos son intentos de reducir exposición o de salir**:

```
seq 7152, 7155, 7156   Sell        MES SEP26
seq 7160, 7161, 7162   Sell        MES SEP26
seq 7169, 7170, 7171   SellShort   MES SEP26
seq 7181, 7182, 7183   BuyToCover  MES SEP26
```

`Sell` cierra un largo. `BuyToCover` cierra un corto. **El guardián los canceló todos.**

---

## 2. El mecanismo, confirmado en el código

`Guardian.cs:576`:

```csharp
_broker.CancelAllOrders(order.Account);
```

**Incondicional.** No pregunta el lado de la orden, ni quién la mandó, ni si reduce exposición.

Veinte líneas más arriba, en el comentario del arreglo de M1 del 22-ago:

> *"what a cancel destroys is a protective stop, on an account that may hold real money"*

**Identificamos el daño, arreglamos a QUIÉN le pasa, y dejamos intacto QUÉ ES sobre la cuenta
vigilada.** Es la mejor descripción del defecto que vamos a tener, y estaba escrita en el archivo.

El bucle completo:

1. `LOCKED` + hay posición ⇒ `Flatten()` ⇒ NT8 emite la orden `Cerrar`
2. `OnOrderUpdate` observa `Cerrar` ⇒ `OnOrderObserved` ⇒ estado `Locked` ⇒ `CancelAllOrders`
3. `Cerrar` muere sin llenarse
4. La posición sigue abierta ⇒ siguiente tick ⇒ vuelve a 1

---

## 3. Por qué ninguna prueba lo vio — clase nueva

`FakeBroker.Flatten` (`tests/GuardianCore.Tests/Fakes.cs`):

```csharp
public void Flatten(string account)
{
    Calls.Add("flatten:" + account);
    …
    if (_positions.TryGetValue(account, out var list)) list.Clear();
}
```

**Borra de una lista.** No hay orden, no hay envío, no hay aceptación, no hay cancelación posible —
**no hay ciclo de vida.** El doble hizo el aplanado **atómico**, y el diseño asumió esa atomicidad sin
que nadie escribiera esa suposición en ningún lado.

No es un bug de código: **es un error de modelo, escondido por el instrumento que debía revelarlo.**
Las 16 corridas del soak y los 256 tests son verdes y **siguen siendo verdes**: prueban un mundo donde
aplanar es instantáneo. Ese mundo no existe.

> **UN DOBLE DE PRUEBA QUE SIMPLIFICA LA REALIDAD NO PRUEBA MENOS — PRUEBA OTRA COSA, Y EL VERDE DICE
> QUE PROBASTE LA QUE NO ERA.**

Es hermana de la clase que ya teníamos —*una afirmación cierta sobre el conjunto equivocado*— pero
peor de cazar: acá **el conjunto equivocado es un universo entero**, construido a propósito, y su
diferencia con el real es exactamente el defecto.

---

## 4. El costo, sin suavizar

**El guardián atrapa al trader en una posición y le cancela sus propias salidas.**

No es una metáfora: los doce `ORDER_REJECTED_LOCKED` de arriba son `Sell` y `BuyToCover`, órdenes que
**reducen** exposición, canceladas por el guardián. Un humano que intentara cerrar a mano en esa
ventana habría recibido el mismo trato: `OnOrderObserved` cancela todo lo que se observe en la cuenta
vigilada mientras esté `LOCKED`.

Es el **error espejo en su forma más cara** — el guardián causando la pérdida que existe para evitar —
y es lo contrario del principio que gobierna al repo hermano: *entradas fail-closed, salidas que
reducen exposición fail-open; ante duda, dejar salir.*

Fue Sim101 con precios sintéticos, así que no costó nada. En una cuenta fondeada, con el mercado
moviéndose, cuesta exactamente lo que el producto promete impedir.

---

## 5. Dos defectos más, en el texto que el usuario leyó

### `"your limit is $0.00"` — y es M15 otra vez, en otro campo

El mensaje que Roberto vio decía:

> *"You are down $40.00 and your limit is **$0.00**."*

El límite era $40. `_personalLimit` se asigna **sólo dentro de la ruta de armado**
(`DeadmanGuardianAddOn.cs:268`). El F5 **posterior al armado** reconstruyó el addon; el guardián
restauró `ARMED` **desde el sello, sin volver a armar**; el campo quedó en su default `0`.

**Es exactamente la forma de M15** —un campo poblado sólo al armar, y un reinicio que restaura `ARMED`
sin rearmar— que arreglé el 22-ago para `_guardedAccount` **sin barrer la familia**. Fallo mío: el
arreglo de M15 debió haber disparado una búsqueda de todos los campos con ese ciclo de vida.

Y son **tres**, los tres en el mismo bloque `if (parsed.Ok)`:

| campo | consecuencia tras un reinicio |
|---|---|
| `_personalLimit` | el mensaje del breach publica un límite de `$0.00` |
| `_resetLocalTime` | `Messages.Until(null, …)` devuelve `null` |
| `_zoneId` | ídem |

`Until` devolviendo `null` es correcto por diseño —*un tiempo que no tenemos se omite, jamás se
inventa*— así que **el "until 17:00 (America/Chicago)" desaparece de todos los mensajes en silencio.**
La protección funcionó; lo que falló es que el dato nunca llegó.

### El segundo mensaje nunca llegó

`LockoutComplete` se emite en `Ev.FlattenVerified`, que no ocurrió. El trader leyó *"I am about to
cancel your working orders and close your positions"* y después **nada**, mientras el guardián giraba
167 veces en silencio.

Eso es **correcto por diseño** —el mensaje en pasado no debe existir si el aplanado no se verificó— y
la consecuencia igual es mala: la promesa quedó sin cierre. El diseño de los dos mensajes nunca
contempló *"la promesa no se puede cumplir y el estado no avanza"*.

---

## 6. Dirección del arreglo — a evaluar mañana, nada decidido

**`CancelAllOrders` es la primitiva equivocada.** Debería cancelarse **sólo lo que AUMENTA
exposición**. Un solo cambio arregla las dos mitades: deja de matar sus propias órdenes de aplanado, y
deja de atrapar al trader que quiere salir a mano.

**Lo que NO alcanza:** recordar los ids de las órdenes propias. Arregla la auto-cancelación y **deja
al trader atrapado igual**, que es la mitad que cuesta dinero.

**La lección ya existe en el repo hermano.** ALAYA cableó en agosto la asimetría *entradas
fail-closed / salidas que reducen exposición pasan todos los frenos*, y aprendió que la exit-ness
**no se determina por `side`** sino por un `intent_kind` explícito. Nunca cruzó a este repo.

**Pero no transfiere tal cual, y hay que decir por qué:** ALAYA decide sobre intents que **ella misma
construye**, así que puede exigir `intent_kind`. El guardián observa **órdenes ajenas** —del DOM, de
un gráfico, de una estrategia, de un bot— que no traen intención declarada. Acá "reduce exposición"
hay que **calcularlo**: acción de la orden contra la posición actual del instrumento.

La traza de esta noche da el caso de prueba exacto: con posición larga, `Sell` y `BuyToCover`
**reducen** y deben pasar; `SellShort` **abre** y debe cancelarse. Los tres aparecieron en el mismo
minuto y el código los trató igual.

**Rojo primero cuando llegue el arreglo. Y la prueba tiene que correr contra un doble que MODELE EL
CICLO DE VIDA DE LA ORDEN** — envío, aceptación, cancelación, llenado. Si el doble sigue borrando de
una lista, el verde volverá a mentir, y esta vez sabremos que mentía.
