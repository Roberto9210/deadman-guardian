# Propuesta — cobertura de continuidad del sello

**Estado: propuesta. Nada de esto está implementado.** Cruza dos repositorios y la mitad que importa
no es de este lado.

## El problema, dicho sin adornos

SPEC §17.2 declara el hueco y lo declara bien: *"Across a restart: **not defended**"*. Con continuidad
monotónica el sello se mide en un contador que el trader no puede mover; sin ella —o sea, después de
cualquier reinicio del proceso— `IsSealExpired()` cae al reloj de pared contra `expiresAtUtc`
(`Guardian.cs:489-495`), y el detector de salto hacia adelante de `CheckClock` está condicionado a
`continuity` (`Guardian.cs:467`), que es falsa por construcción tras un reinicio.

Cerrar el hueco necesita una fuente de tiempo fuera de la máquina, y v1 no abre sockets (§13). Sigue
siendo una pregunta de v2.

**Pero el rastro ya existe y el certificado no lo publica.** `SEAL_EXPIRED` lleva
`basis: monotonic | wallclock` desde v0.3, y los pares `GUARDIAN_STOPPED` / `GUARDIAN_STARTED` están
en el vocabulario desde v0.1. Un día que terminó por reloj de pared es hoy indistinguible, en el
documento publicado, de uno que terminó por contador monotónico. Esa distinción es información real
que ya está en el ledger y que nadie ve.

## Dos principios que fijan la forma

**1. Declara cobertura, no culpa.** Un reinicio con expiración por reloj de pared lo produce igual una
actualización de Windows a las 3 de la mañana que alguien haciendo trampa. El nombre y el texto no
pueden insinuar lo segundo. `sealExpiryBasis` describe qué evidencia decidió; `clockTampered` acusaría
a la mayoría de los inocentes. Un campo que acusa de más se vuelve ruido que la gente aprende a
ignorar, y entonces no protege a nadie.

**2. Lo computa el VERIFICADOR, no el emisor.** Si lo calcula el verificador es una **cantidad
verificada**; si lo publica el emisor es una **cantidad afirmada**. Es la misma diferencia que entre
un ancla externa y una cadena de hashes, y el proyecto ya eligió de qué lado está. El emisor no gana
un campo nuevo: gana la obligación de no perder los eventos de los que se deriva.

## Qué computa el verificador, sólo del ledger

Para el período armado de un día — de `ARMED` (o `DAY_OPENED`) hasta `DAY_CLOSED`, o hasta el último
evento si el día sigue abierto:

| cantidad | derivación |
|---|---|
| `sealExpiryBasis` | el campo `basis` del `SEAL_EXPIRED` que cerró el día: `monotonic`, `wallclock`, o ausente si el día no terminó por expiración |
| `processRestarts` | pares `GUARDIAN_STOPPED` → `GUARDIAN_STARTED` dentro del período armado |
| `continuityCoverage` | fracción del período armado cubierta por continuidad monotónica |
| `unmonitoredMs` | suma de los intervalos `GUARDIAN_STOPPED` → `GUARDIAN_STARTED`: tiempo en que no había guardián corriendo |
| `longestGapMs` | el **más largo** de esos intervalos, por separado |

`continuityCoverage` no es "el período menos los huecos", y la diferencia importa.
`HasMonotonicContinuity` compara `seal.runId == runId` del proceso actual (`Guardian.cs:107`), así que
**un solo reinicio la anula para todo el resto del día**, aunque el proceso vuelva en dos segundos. La
continuidad sólo se recupera creando un sello nuevo. Entonces:

```
continuityCoverage = Σ |[SEAL_CREATED_i , siguiente GUARDIAN_STOPPED_i] ∩ período_armado|
                     ─────────────────────────────────────────────────────────────────────
                                          |período_armado|
```

Con un día sin reinicios da 1.0. Con un reinicio a mitad de sesión y sin re-armar, da ~0.5 aunque el
guardián haya estado corriendo el 99,9% del tiempo — que es exactamente lo que se quiere decir: estuvo
mirando, pero desde ese momento su medida del tiempo dependía del reloj de la máquina.

`unmonitoredMs` es la otra pregunta, y por eso va aparte: cuánto tiempo no hubo nadie mirando.

Y `longestGapMs` va aparte de `unmonitoredMs` porque **el total esconde la forma**. Cuatro horas en
un solo hueco y veinte huecos de dos segundos dan cifras parecidas y significan cosas opuestas. Un
reinicio legítimo — una actualización, un cierre y reapertura, un cuelgue — se mide en segundos o
en un par de minutos. Un hueco de horas en mitad de una sesión armada es la **forma exacta** que el
ataque de §17.2 necesita: cerrar, mover el reloj, abrir.

El campo sigue sin acusar: no dice cuál de las dos cosas pasó, dice cuánto duró el hueco más largo.
Es lo que vuelve ruidoso el movimiento sin señalar a nadie — y a diferencia de un conteo de
reinicios, un valor alto acá es raro entre los inocentes, que es lo que hace que valga la pena
mirarlo.

## Qué muestra

Una línea en el render, junto a las demás afirmaciones:

> **Continuidad del sello:** 100% del período armado · 0 reinicios de proceso · el día cerró por
> contador monotónico

o, en el caso incómodo:

> **Continuidad del sello:** 41% del período armado · 2 reinicios de proceso · el día cerró por reloj
> de pared · 4 h 12 min sin guardián corriendo, **el hueco más largo de 4 h 09 min**

Y el texto fijo que la acompaña, que es la mitad del trabajo:

> Un reinicio del proceso ocurre por actualizaciones, cierres y reinicios normales. Esta línea declara
> qué evidencia decidió el final del día, no que alguien haya hecho algo. Con continuidad al 100% el
> final del día se midió en un contador que nadie puede ajustar; por debajo de eso, se midió contra el
> reloj del sistema (SPEC §17.2). La duración del hueco más largo se informa porque un reinicio
> ordinario dura segundos; nada más se afirma sobre él.

## Lo que esta cantidad NO prueba — va en el certificado, no en una nota al pie

`continuityCoverage` se deriva de `tsUtc` del ledger, y esos timestamps salen del mismo reloj que un
atacante habría movido. **No prueba nada por sí sola.** Lo que hace es hacer *visible* en el documento
publicado la condición bajo la cual el hueco de §17.2 es explotable, en vez de dejarla enterrada en un
`.jsonl` que nadie abre.

Lo que sí es difícil de fabricar: un reloj movido y devuelto deja una secuencia de `tsUtc` **no
monotónica dentro de un archivo encadenado por hashes**, que no se puede reparar en silencio. Si el
verificador ya recorre la cadena, detectar `tsUtc` que retrocede entre `seq` consecutivos es gratis y
es una señal independiente.

**Aprobado (Roberto, 21-ago-2026) y asignado a Ventana B.** El blanco es el viaje de vuelta: mover el
reloj hacia adelante no deja un `tsUtc` que retroceda, pero **devolverlo sí** — y el atacante tiene
que devolverlo si quiere seguir operando con datos de mercado coherentes. Es el tramo que no puede
evitar.

## Reparto del trabajo

| lado | qué le toca |
|---|---|
| **`deadman` — Ventana B** | todo el cálculo y la presentación: derivar las cinco cantidades en `verify_certificate`, imprimirlas, agregar el texto fijo, y el chequeo de `tsUtc` no monotónico **ya aprobado**. Es el verificador el que convierte esto en verificado |
| **`deadman-guardian` — Ventana A** | ninguna implementación en el emisor, que es el punto. Sólo dos obligaciones defensivas: que `GUARDIAN_STOPPED`/`GUARDIAN_STARTED` y el `basis` de `SEAL_EXPIRED` **nunca** se omitan de un segmento rotado, y una nota en SPEC §11/§12 diciendo que estos eventos son la fuente de derivación y por eso no son opcionales |

No hay cambio de esquema, no hay campo nuevo en el JSON del certificado, y no hay nada que el emisor
tenga que afirmar. Si Ventana B lo implementa, funciona sobre todos los certificados ya emitidos.
