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

## Qué haría, sin implementarlo todavía

El guardián ya tiene una ventana de estado propia, que es la única superficie donde puede hablar en su
propia voz. La corrección no es técnica: es **decir en voz alta, en el momento exacto, lo que acaba de
hacer y por qué**. Algo del orden de:

> **BLOQUEADO — límite diario alcanzado.** Se cancelaron N órdenes, se aplanó la cuenta y NinjaTrader
> deshabilitó M estrategias como consecuencia. Esto es el guardián haciendo su trabajo, no un fallo de
> la plataforma. No se puede volver a operar hasta las 17:00.

Tres cosas hacen falta y ninguna existe hoy: que la ventana anuncie el bloqueo cuando ocurre (no sólo
que muestre el estado), que **nombre las estrategias deshabilitadas** —el guardián puede enumerarlas
antes de aplanar—, y que diga explícitamente que el apagado lo hizo NinjaTrader por consecuencia y no
es un error.

Sin eso, el evento más importante que el producto produce en toda su vida —el único momento en que
realmente salva a alguien— se le presenta al usuario como si el software se hubiera roto.

## Fuentes

- [Strategy disables — NinjaTrader Support Forum](https://forum.ninjatrader.com/forum/ninjatrader-8/platform-technical-support-aa/93519-strategy-disables)
- [What happens when "Flatten Everything" is pressed?](https://forum.ninjatrader.com/forum/ninjatrader-8/strategy-development/1204870-what-happens-when-flatten-everything-is-pressed)
- [How can I prevent a strategy from auto disabling after position is closed?](https://forum.ninjatrader.com/forum/ninjatrader-8/strategy-development/1107907-how-can-i-prevent-a-strategy-from-auto-disabling-after-position-is-closed)
