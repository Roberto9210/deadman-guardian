# Predicción sellada — el F5 posterior al despliegue de LT-1

**Escrita ANTES de cerrar NinjaTrader y commiteada antes de que ocurra nada.** 2026-08-27, 19:35 CT.

Existe por una razón concreta: **el F5 posterior al despliegue va a restaurar el sello SIN rearmar**,
que es exactamente el escenario de **LT-2**, que todavía no está arreglado. Si no lo predecimos ahora,
mañana lo leemos como una molestia en vez de como la confirmación en producción que es.

## Contexto verificado, no supuesto

| | |
|---|---|
| producción | `ARMED`, `dayKey 2026-08-28`, límite `600.00`, sello hasta `2026-08-28T22:00:00Z` |
| binario cargado | `4d20652361c0b468` — **el que tiene el defecto LT-1** |
| binario a desplegar | el de `bin\Release` tras compilar con LT-1 arreglado (`a916bba`) |
| compuertas | `botA.GO` **ausente**, `soak.GO` aparcado como `soak.GO.parked-for-livetest` |
| bots corriendo | ninguno |

**Nota sin adornar:** durante estos minutos producción está armada a $600 con el binario que tiene
LT-1. No hay bots ni posiciones, así que el riesgo real es nulo — pero es la misma condición de
anoche, y el despliegue la termina.

## Las cuatro predicciones

### 1. La ventana va a mostrar el límite como `$0.00`, aunque el sello diga `600.00`

`_personalLimit` se asigna **sólo** en la ruta de armado (`DeadmanGuardianAddOn.cs:268`). El F5
reconstruye el addon; `Start()` restaura `ARMED` desde el sello **sin ejecutar esa ruta**; el campo
queda en su default `0`.

**⇒ LT-2 confirmado en producción, con evidencia, sin provocar nada.**

### 2. La cláusula "until 17:00 (America/Chicago)" NO va a aparecer

`_resetLocalTime` y `_zoneId` (`:266-267`) quedan nulos por la misma razón, y `Messages.Until(null, …)`
devuelve `null` (`Messages.cs:204`), que `UntilClause` convierte en `""` (`:212`).

**La ausencia se comporta bien; el cero miente.** Los dos defectos en la misma pantalla, y la
diferencia entre ellos es únicamente el tipo del campo.

### 3. `PNL_BASELINE_ADOPTED` con `adopted 0.00`

Option A corriendo en producción por **tercera** vez, ahora con el binario nuevo. Sin realizado en el
día y sin checkpoint previo del `dayKey 2026-08-28`, debería tomar la rama trivialmente establecida.

### 4. El certificado emitido después del F5 reporta `issuer.buildHash` = el hash nuevo

`IssuerIdentity.BuildHashOf` hashea el ensamblado **desde el que se cargó**, y ese valor es el que el
certificado publica. **Es el código corriendo declarando qué binario es** — afirmación positiva desde
adentro del proceso, no inspección desde afuera ni inferencia por el bloqueo de archivo.

## LA CONDICIÓN DE PARADA

**Si 1 y 2 NO ocurren, nuestro diagnóstico de LT-2 está mal**, y hay que parar y revisarlo antes de
escribir una línea de esa tanda. Un arreglo construido sobre un mecanismo que no es el real sería
peor que el defecto.

## Lo que esta noche NO verifica

La verificación del paso 8 es **de identidad, no de conducta**. El certificado dice qué binario está
cargado; **no** demuestra que LT-1 esté arreglado en la máquina.

El marcador conductual definitivo es la **ausencia de `ORDER_REJECTED_LOCKED` tras un lockout**, junto
con un `FLATTEN_VERIFIED` que llegue. Eso exige provocar un lockout con el binario nuevo, y **no se
hace hoy**. Queda para después de que producción esté armada con el binario nuevo, planificado como la
prueba viva anterior: por escrito y antes de correrlo.

**No se finge lo que no se midió.**

---

## Una corrección que corresponde dejar escrita

En el reporte previo afirmé que `botA.GO` *"sigue puesto"*. **No estaba.** Lo leí mal de la salida de
mi propio comando: la línea del `ls *.GO` no imprimió nada y reporté lo contrario — una premisa no
verificada, en el mismo mensaje en que señalaba esa clase de error en otro lado.

Verificado después: **ningún código borra compuertas de bots** (sólo el probe borra la suya,
`DeadmanGuardianLatencyProbe.cs:164`), así que la quitó una persona. Y explica por qué BOT A no arrancó
en la recarga del addon: no había compuerta que leer.

El hecho operativo no cambia —no hay compuerta y BOT A no corrió, verificado por el log— pero el
razonamiento con el que lo dije estaba mal, y eso importa tanto como el hecho.
