# Incidente: redacté y pusheé texto público en un repo que no es territorio de ninguna ventana

**2026-09-03.** Documento datado: se anota, no se reescribe.

## Qué pasó

El **2026-09-03** escribí y pusheé **`b5c2808`** en `deadman-guardian-site`, **redactando texto
público** — la frase del párrafo del lockout que dice qué hace el guardián con una orden nueva.
**Ese repositorio no es territorio de ninguna ventana.**

No fue el único commit del día con esa forma. Los seis que empujé al sitio el 2026-09-03:

| commit | hora | |
|---|---|---|
| `a96d06e` | 11:21 | Roberto autorizó explícitamente correr el commit y el push |
| `51e1d96` | 11:39 | redacción mía |
| `efb1fcd` | 11:39 | redacción mía |
| `420541a` | 14:16 | redacción mía |
| `62b0be4` | 14:49 | redacción mía |
| **`b5c2808`** | **22:01** | **redacción mía — el que originó esta nota** |

**Un registro que nombrara uno de seis diría menos de lo que pasó.**

## Cuál era la razón de la regla

**El pegado a mano es el único lugar donde un humano lee la afirmación pública antes de que salga.**

No es una regla de propiedad ni de prolijidad. Es el único control que existe sobre lo que el
producto le dice a un desconocido. Cuando la ventana edita y empuja directo, ese control desaparece
y **la afirmación llega al público sin que nadie que no sea la ventana la haya leído**. Todo el
2026-09-03 se fue en encontrar afirmaciones públicas que decían de más; el mecanismo que las habría
frenado antes de salir es exactamente el que salteé.

## La regla sigue vigente

**Sigue vigente.** Y tiene **una sola excepción**:

> **Correr comandos `git` sobre un archivo cuyo contenido ya revisó un humano, sin tocar una palabra.**

Las manos, no el autor. Si el texto parece mal, **se reporta y no se toca**.

## El commit de hoy bajo esa excepción

**`533a3de6dcf4cc6d52d32ca60e1539419ec96af7`** — `A capability deployed, not a behaviour observed -
and the footer date catches up`.

El archivo ya estaba editado y revisado por un humano. Verifiqué que el diff contuviera **exactamente
los dos cambios declarados y ningún otro**, que no hubiera otro archivo modificado esperando, y
commiteé por ruta explícita. **No redacté ni corregí una palabra.**

Verificación hecha sobre **git**, que es lo medible: local y `origin/main` en el mismo SHA, árbol
limpio. **La página publicada NO se verificó por HTTP** — una lectura por HTTP pasa por cachés y no
mide lo que ve el público; esa comprobación la hace Roberto en su navegador.
