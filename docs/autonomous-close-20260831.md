# Reporte de cierre — tanda autónoma del 2026-08-31 (noche)

**Trabajo hecho sin operador presente**, bajo reglas duras: nada irreversible, nada que necesitara
una mano humana, ninguna decisión ajena tomada sola. **Se respetaron todas.**

> **No se armó. No se desplegó. No se corrió `install.ps1`. No se tocó `config.json`. No se tocó
> NT8.** Ninguna compuerta se repuso.

---

## 1 · Estado de la máquina al cerrar

| | |
|---|---|
| producción | **`DISARMED`**, sin sello, **sin armar** |
| `config.json` | `38a15089c889ac09`, **$600** — el de producción, intocado |
| ledger | **8.027 entradas**, cadena íntegra |
| NT8 | abierto, con el binario del despliegue de la tarde |
| desplegado | **`12fff6f6c76d838c`** |
| repo | `2155153`, **320 tests, 0 fallos** |

### ⚠ Divergencia, y por qué esta vez SÍ importa

El repo tiene **cert-1 y el canal de sonido**; lo desplegado **no**. Y `bin\Release` está atrasado
respecto al fuente (build 19:09, `Messages.cs` 19:41), así que **`install.ps1` daría `exit 5`** —
la guarda de build viejo. **Lo dejé así a propósito**: fuerza recompilar antes de desplegar.

La regla dice que la divergencia sólo se reporta cuando hay *verificación pendiente sobre un binario
específico*. **La hay**: los tres gestos del panel están pendientes de confirmación sobre
**`12fff6f6c76d838c`**. Hasta que se confirmen, ese binario es el sujeto de una afirmación abierta.

---

## 2 · Qué se hizo

### Cierre de la tanda del día
`CLAUDE.md` al día y bitácora completa en **`docs/log-20260831.md`** — la corrida, el apagón, los
tres defectos de UI, el despliegue en dos vueltas y las seis reglas que dejó el día.

### El inventario de condiciones que esta máquina no produce
**`docs/conditions-this-machine-cannot-produce.md`**. Construido verificando cada fila, no
transcribiendo. Dos cosas salieron de verificar:

- **Varias condiciones SÍ están simuladas aunque la máquina no las produzca** — el salto de reloj de
  M4 tiene `G13a`-`G13d`, y la cadena rota tiene `G17`/`G18` incluido el sabotaje sofisticado que
  recalcula su propio hash. Es un tercer estado que vale la pena tener: **simulada pero no
  producida**.
- **`ledgerPath`/`statePath` ni siquiera pueden surgir**, porque el addon ignora esas claves — lo
  que las convierte en defecto, no en hueco.

Y `M6` quedó como el contraejemplo que impide leer el patrón como universal.

### cert-1 — el certificado acotado al día que nombra
**Definición**: el día empieza en la primera entrada con su `dayKey` y termina donde empieza el
siguiente, o al final del registro si no hay siguiente. Derivable por cualquiera, sin confiar en
quien emite, y funciona sobre ledgers ya escritos.

**Mi primer borrador estuvo mal y lo atraparon cuatro C-tests existentes**: `[min,max]` sobre
entradas con `dayKey` termina en `DAY_OPENED` en un día todavía **abierto** — que es justo el que un
trader exporta a las 16:55. La regla 3b funcionando: los rojos afirmaban algo real.

**Las dos trampas, adentro del arreglo y no descubiertas después:**

- **Candidato 4 no muerde**: el certificado **no calcula ningún P&L** — `Certificate.cs` no menciona
  `PNL_CHECKPOINT` ni una vez. Eso quedó escrito como **el motivo para NO agregar una cifra**: la
  fuente obvia cierra en `0.00` justo los días que importan.
- **Candidato 6 resuelto en `limitations`**: el campo se queda —quitarlo cambiaría la forma del
  documento para un verificador que no es nuestro— y el certificado **dice qué significa ese cero**.

**`limitRespected` no se tocó.** Es semántica y decisión de producto.

### El canal de sonido — sólo la parte pura
`SoundChannel.Assess` / `UseFallback` / `ShouldSoundNow` y `Messages.DetailSoundChannel`, con **10
tests** que fabrican el canal roto en sus tres formas — porque la máquina de Roberto lo tiene sano y
nunca lo va a producir sola.

---

## 3 · Qué quedó a medias, y por qué

| | por qué se paró |
|---|---|
| **el cableado del sonido en el adaptador** | no es testeable, pondría el repo fuera de paso con lo desplegado sin ganancia verificable, y su cadencia interactúa con el acuse, que está bloqueado |
| **el acuse de recibo** | bloqueado por el contrato de extensión con Ventana B |
| **el `dayKey` en `CONFIG_LOADED`** | cerraría el hueco conocido de cert-1, pero es un **campo aditivo** ⇒ mismo contrato |
| **`soak.GO`** | su aserción exige `ORDER_REJECTED_LOCKED` y una orden muerta; ninguna ocurre desde LT-1 |
| **`CertificateRequest.DaysCovered`** | superada e ignorada; quitarla rompe el adaptador ya compilado en la ventana previa al F5 ⇒ cambio coordinado |

---

## 4 · Preguntas para cuando vuelvas

**1 · Los tres gestos del panel.** ¿Se ve el chevron `⌃` o un cuadradito? Colapsado, ¿sigue visible
como `⌄`? ¿La franja se queda puesta? Es lo único que ningún log registra, y son los tres que ya
fallaron una vez.

**2 · La aserción del soak — bloquea reponer la compuerta.** ¿Qué debería probar ahora? Opciones:
*(a)* que nada se cancela al observar y la exposición se cierra por el aplanado siguiente — mide lo
que el producto hace hoy; *(b)* borrarla hasta que exista cancelación selectiva — pierde cobertura;
*(c)* dejarla roja como recordatorio — pero un rojo permanente entrena a ignorar la suite.

**3 · El contrato de extensión con Ventana B.** No es un campo, es una **categoría**: ¿el formato
admite hechos sobre la **interacción con el humano** además de hechos sobre la cuenta? Se decide una
vez y condiciona el acuse, el hash en `GUARDIAN_STARTED` y el `dayKey` de `CONFIG_LOADED`.

**4 · ¿Armar producción?** Está `DISARMED` a $600. No armé: es tu decisión, nunca inercia.

**5 · El enrutado de `SystemSounds`** — no verificado. ¿Sale por la sesión *"Sonidos del sistema"*
del mezclador o por la del proceso? No cambia el diseño (la audibilidad es inobservable igual), pero
decide si el respaldo sirve cuando NT8 está silenciado. Requiere Core Audio, no reflexión.

**6 · Candidato 11** — `NinjaTrader.Adapter.AccountLockoutNotifications`, público, con evento `Push`
y método `Raise`. No lo perseguí. El nombre es demasiado cercano a lo que hace este producto.

**7 · `ledgerPath`/`statePath` ignorados por el addon.** Defecto abierto: claves que afirman
configurar algo que está cableado. ¿Se borran del esquema o se hacen funcionar?

**8 · Los docs en inglés con un NT8 en español.** Está anotado hace días y sigue abierto; es el
espejo exacto del problema que perseguimos, y le va a pegar al primer beta que no sea Roberto.

---

## 5 · Errores propios de esta tanda

Tres, los tres agarrados por la comprobación siguiente y ninguno al escribirlos:

1. **La definición del día de cert-1** — rota para un día abierto. La atraparon cuatro C-tests.
2. **Los backticks en comillas dobles de bash** — una fila de `CLAUDE.md` se escribió con huecos
   donde iban sus términos técnicos. Es la regla 2.8 entrando por la otra puerta; quedó ampliada.
3. **El `csproj` con escapado mal** — falló la aserción y no escribió, que es lo correcto.

Y el patrón, que ya no es nuevo: **ninguno se vio al escribirlo.** *La vigilancia no sustituye a la
comprobación* — sostenido también trabajando sola.
