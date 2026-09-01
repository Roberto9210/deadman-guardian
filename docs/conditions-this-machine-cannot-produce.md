# Condiciones que esta máquina no produce

**Lista viva, no principio.** Se actualiza cuando aparece una condición nueva o cuando una que
estaba acá empieza a ocurrir de verdad.

---

## Por qué existe

Es la regla del doble de prueba vista desde otra altura:

> **el doble** difiere del mundo, y esa diferencia puede ser donde vive el defecto
> **la MÁQUINA** difiere del campo, y **lo que no puede producir es invisible**

La segunda tiene un corolario que la primera no tiene: **se puede escribir la lista.** La primera
enseña a desconfiar; ésta se convierte en un documento.

## Y su trabajo real no es «qué falta probar»

Es **separar dos cosas que en una tabla de defectos se ven idénticas y son riesgos opuestos**:

| | |
|---|---|
| **abierto porque NADIE LO VIO** | una sorpresa esperando a alguien |
| **abierto porque SE DECIDIÓ** | el producto funcionando como se eligió |

La marca ya estaba escrita en cada fila de la serie M desde antes, sin que nadie la usara así:
*"sin medir"* y *"sin evidencia"* contra *"diseñado así"*.

**`M6` es el contraejemplo que impide leer el patrón como universal**: esta máquina lo produce
seguido —desconexiones del feed, varias por semana— y sigue abierto igual, porque es una decisión.

## Y hay un TERCERO, que es el más engañoso de los tres

| | |
|---|---|
| **SIMULADA PERO NO PRODUCIDA** | verde contra nuestro **modelo** de la condición |

Los dos de arriba se separan por una decisión, y los dos **se ven como riesgo**. Éste no: **se ve
como una tranquilidad.** Dice *"sí"* en la columna que el lector usa para relajarse.

> ### Un test no es la condición. Es lo que CREEMOS que la condición hace.

`G13a`-`G13d` no prueban que el guardián sobreviva a una suspensión: prueban que sobrevive **al salto
de reloj que escribimos nosotros**. Si una suspensión real trae además el feed volviendo con
timestamps viejos, un `Account` re-creado, o dos relojes que discrepan al despertar, nada de eso está
en el test **porque no está en nuestra cabeza**. Una fila con test y sin ocurrencia está verificada
contra la condición que imaginamos, no contra la que va a llegar.

Es **el problema del doble de prueba con la forma de una tranquilidad**, y por eso es peor que el
doble: **el doble se declara doble.** Un `sí` en la columna de test se lee como cobertura y es una
hipótesis en verde.

**La consecuencia operativa, que es lo único que hay que recordar**: una fila *"nadie lo vio"* con
test **sigue siendo** *"nadie lo vio"*. El test abarata el arreglo cuando la condición llegue; **no
baja la probabilidad de que llegue por un camino que no imaginamos.** En el registro de riesgos de la
beta, una fila con test **no vale menos** que una sin test.

## Y un CUARTO estado, que sube de prioridad en vez de bajar (1-sep)

| | |
|---|---|
| **NO PRODUCIBLE ACÁ, UNIVERSAL AFUERA** | esta máquina ya pasó por ahí; **todo usuario nuevo pasa por ahí primero** |

**Una forma que ocurre UNA VEZ POR INSTALACIÓN se lee RARA desde adentro de una instalación y
UNIVERSAL desde la población.** Es la trampa de perspectiva de esta lista entera: medimos desde
adentro de la única instalación que tenemos.

**El caso que lo encontró**: `GUARDIAN_STARTED` tiene dos formas, y la del arranque en frío
—`{state:"DISARMED", fresh:true}`— aparece **1 vez en 70** en el ledger de producción. Leerlo como
*"rara, esta máquina ya gastó la suya"* es correcto **y es la mitad equivocada de la frase**: es el
**primer evento que va a escribir todo usuario nuevo, sin excepción**, y la única manera de volver a
observarla es **una instalación nueva** — que es exactamente lo que hace el primer beta que no sea
Roberto.

**Los dos miembros de este estado hoy, y comparten vecino a propósito:**

| condición | por qué esta máquina no la produce | por qué es universal afuera |
|---|---|---|
| **`GUARDIAN_STARTED` en frío** (`fresh:true`) | ya ocurrió, el 2026-08-21, y no vuelve | el primer arranque de cada instalación |
| **NinjaTrader en inglés** | el de Roberto está en español | cualquier tester anglófono, desde el minuto uno |
| **Episodios fail-closed por cuenta desconectada** (1-sep) | se activó **connect on startup** el 2026-08-22 14:57:28Z (`Config.xml.before-connectonstartup-20260822-095728`); **cero commits el 22, 23 y 24** ⇒ no fue el guardián. Ninguno en 10 días | **NT8 no se conecta al arrancar por defecto** ⇒ toda instalación nueva empieza ahí. **Y es lo primero que se le va a romper a un usuario nuevo**: un certificado emitido dentro de un episodio lee `limitRespected: false` sobre un día impecable |

⇒ **no baja de prioridad por inalcanzable: sube.** Los dos le pegan al **primer beta que no sea
Roberto**, y ninguno de los dos se puede ensayar acá sin instalar de cero.

## ⇒ Esta lista ES el registro de riesgos de la beta

**El conjunto exacto de cosas que un desconocido va a encontrar en su primera semana y que Roberto
no encontró en tres.** Antes de dejar entrar a alguien, esto decide qué se prueba.

---

## El inventario

Tres columnas que importan: por qué esta máquina no la produce, si hay test que la **simule**, y por
qué está abierta. **Un *"sí"* en la del medio es una hipótesis en verde, no cobertura** — ver el
tercer estado, arriba.

### Entorno y plataforma

| condición | por qué esta máquina no la produce | ¿test que la simule? | ¿por qué abierta? |
|---|---|---|---|
| **Salto de reloj por suspensión** (M4) | la máquina no se suspende con NT8 abierto y el guardián armado | **sí** — `G13a`-`G13d`, incluido `G13d_a_sleep_like_divergence` | **nadie lo vio** (*"sin medir"*) |
| **Feed desconectado** (M6) | **la produce** — reconexiones del servidor histórico varias veces por semana | sí | **se decidió** (*"diseñado así"*) — **el contraejemplo** |
| **Tick sin precio con posición abierta** (M5) | el feed simulado siempre tiene precio | sí (`NoPriceForOpenPosition`) | **se decidió** |
| **Ejecución sin `ExecutionId`** (M7) | NT8 nunca emitió uno acá | parcial | **nadie lo vio** (*"sin evidencia"*) |
| **No realizado sin posición reportada** (M20) | no ocurrió | — | **se decidió** (residual) |
| **Primer arranque sobre un ledger viejo** (M21) | esta instalación ya gastó su única vez | — | **nadie lo vio** (*"una vez por instalación"*) |
| **Disco lento o lleno durante una escritura** | disco sano, archivos chicos | sí, por fakes que lanzan en escritura | **nadie lo vio** |

### Cuenta y configuración

| condición | por qué esta máquina no la produce | ¿test que la simule? | ¿por qué abierta? |
|---|---|---|---|
| **Cuenta fondeada CONECTADA** | **a propósito**: sólo se conecta el feed simulado. `2127534` está presente y desconectada, siempre | **sí** — `Bots_AccountRuleTests` la construye conectada y verifica que el riel niega | no es un defecto: es el riel |
| **Config con más de una cuenta** | `config.json` tiene una sola | **sí** — `M16_a_config_with_two_accounts_is_refused_at_arm` | **se decidió** (M16 rehúsa) |
| **`ledgerPath`/`statePath` apuntando a otro lado** | **la condición no puede surgir**: el addon usa rutas fijas y **ignora esas claves** | — | **defecto abierto** — claves que afirman configurar algo que no configuran |
| **Canal de sonido degradado** — volumen 0, ruta vacía, o **ruta que apunta a un archivo inexistente** | el canal de Roberto está **sano**: volumen 50, `Announcement.wav` presente, los 13 archivos existen | **NO — pendiente** | el chequeo todavía no existe |

### Entorno humano

| condición | por qué esta máquina no la produce | ¿test que la simule? | ¿por qué abierta? |
|---|---|---|---|
| **NinjaTrader en inglés** | el de Roberto está en **español** (`log.*.es.txt`, *"Trasmisión de datos simulados"*) | — | **defecto de documentación**: `docs/` cita nombres de UI en inglés. El espejo exacto del mismo problema |
| **Un humano que aprieta un botón con una EXPECTATIVA** | ver abajo — es de otra clase | **imposible** | ver abajo |

### Evidencia

| condición | por qué esta máquina no la produce | ¿test que la simule? | ¿por qué abierta? |
|---|---|---|---|
| **Un ledger con la cadena rota de verdad** | nunca se rompió — sobrevivió incluso a un corte de luz durante una escritura posiblemente en curso (31-ago, 8.005 entradas, cero eslabones rotos) | **sí** — `G17`/`G18` la fabrican, incluido el caso sofisticado que re-calcula el hash propio | no es un defecto |

---

## La entrada de otra clase, ganada el 31-ago

> ### LA MÁQUINA PUEDE CORRER TESTS PERO NO PUEDE APRETAR BOTONES CON EXPECTATIVAS

Los tres defectos de UI del 31-ago vivían en código con **303 pruebas verdes** y los encontró una
persona en **sesenta segundos**:

1. el `collapsed` se persistía donde el estado no lo recuerda
2. expandirse solo era una condición permanente en vez de una transición — colapsar se deshacía al
   segundo, y desde afuera eso es un botón que no responde
3. el chevron quedaba **fuera de pantalla** por una altura fija de 40px

**No porque las pruebas fueran malas.** La regla pura que ejercitaban —`RemembersCollapsed`— era
correcta todo el tiempo; el defecto vivía en cómo la ventana la aplicaba, y en un glifo (`-`) que
**prometía la única función que el producto se niega a tener**.

**Ninguna prueba traía una creencia previa sobre lo que ese botón iba a hacer.**

Lo que falta acá no es un estado del entorno: **falta un humano con una expectativa.** No hay
mecanismo que lo simule, y por eso esta entrada no tiene columna de test — tiene una consecuencia:
**cada cambio de superficie necesita que alguien la toque antes de darla por buena.** El add-on
cargando limpio no prueba nada sobre el botón.

---

## Cómo se usa

1. **Antes de dejar entrar a un beta**: todo lo marcado *"nadie lo vio"* es una sorpresa esperando.
   Lo marcado *"se decidió"* necesita estar en la documentación del usuario, no en un arreglo.
2. **Cuando una condición empieza a ocurrir**: se mueve de columna y se anota la fecha. `M6` ya está
   así.
3. **Cuando se agrega una superficie nueva**: alguien la toca antes de darla por buena.
