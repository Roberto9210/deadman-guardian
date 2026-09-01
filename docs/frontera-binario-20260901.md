# Frontera entre binarios — despliegue del 2026-09-01

**Anotada A MANO porque el ledger no puede anotarla solo.** Ver §3: es un defecto propio.

**Por qué existe este archivo**: el sonido y el panel son **conducta nueva** que vamos a querer
atribuir mañana. Si el registro no distingue las dos versiones, **todo lo que midamos sobre conducta
nueva estará sobre un registro que no sabe qué código lo escribió.** Un minuto de anotación es lo único
que fecha el corte.

---

## 1 · ANTES del despliegue — medido 2026-09-01 ~23:15Z

| | |
|---|---|
| **último `seq` escrito por el binario viejo** | **8100** (`STATE_RESTORED`, 2026-09-01T22:58:03.499Z) |
| **hash del binario viejo** | **`12fff6f6c76d838c`** (`GuardianCore.dll`, net48, 2026-08-31 19:09:03) |
| estado del guardián al medir | `DISARMED`, sin sello, `dayKey 2026-09-01` |

> **El 8100 es estable mientras el guardián siga `DISARMED`**, porque desarmado **no escribe nada** —
> comprobado: cero entradas nuevas entre las 22:58 y las 23:15 con NT8 abierto y ticando cada segundo.
> **Si alguien arma antes de desplegar, este número hay que volver a medirlo.**

## 2 · DESPUÉS del despliegue — a completar en el momento

| | |
|---|---|
| **hash del binario nuevo** | `________________` (los 16 de `install.ps1`, que son los mismos que un certificado publica en `issuer.buildHash`) |
| **primer `seq` escrito por el binario nuevo** | `________` (será el `GUARDIAN_STARTED` del arranque post-F5) |
| fecha y hora UTC del arranque | `____________________` |

**Comprobación cruzada gratis, y hay que hacerla**: ese primer arranque es el que
`docs/prediccion-sellada-20260901-despliegue.md` predice **evento por evento**. Anotar el `seq` y
comparar contra la predicción **es el mismo minuto de trabajo**.

---

## 3 · Y esto es un defecto propio, de la familia del día

> ### EL LEDGER NO PUEDE DECIR QUÉ CÓDIGO LO ESCRIBIÓ.

`GUARDIAN_STARTED` no lleva `buildHash`; sus dos formas completas son `{state, fresh}` y `{state}`.
Así que después de esta noche **las líneas de arranque del binario nuevo serán byte a byte
indistinguibles de las del viejo**, y la frontera **existe pero es inencontrable** desde el artefacto.

**Es exactamente la familia de todo lo demás de hoy**: *el artefacto que viaja no lleva el dato que el
lector necesita*. El ledger viaja —es lo que se cita, lo que se audita, lo que sobrevive— y no lleva su
propia procedencia.

**Dónde SÍ vive el hash hoy**: en el **certificado**, campo `issuer.buildHash`, que el addon calcula
del archivo del ensamblado. O sea: **un artefacto lo tiene y el otro no**, y el que no lo tiene es el
que se escribe siempre.

**El arreglo es el ítem 5 de la cola** —hash del binario en `GUARDIAN_STARTED`— y sigue esperando la
respuesta del contrato de extensión. **Mientras tanto, la frontera se anota a mano, acá.**

### Nota de resolución: dos afirmaciones que parecían contradecirse, y no lo hacían

- **Lo que decía el ítem 5 de la cola** (`CLAUDE.md`): *"el `buildHash` va en LOS DOS, no en uno"* —
  **es una instrucción de diseño para cuando ese ítem se implemente**, no una descripción de hoy. Está
  escrita dentro de un ítem que empieza con *"sólo después del 2"*, o sea pendiente y bloqueado.
- **Lo que se midió el 1-sep**: el evento **no tiene** el campo. Cierto, y sobre el presente.
- **Las dos son verdaderas y hablan de tiempos distintos** — pero la frase se lee como un hecho fuera
  de su contexto, **y así la leyó el operador**. Eso alcanza para considerarla defectuosa: se aclaró en
  `CLAUDE.md` en vez de dejarla como estaba.
- **Y el hash que sí existe hoy está en el certificado** (`issuer.buildHash`), que es de dónde venía la
  sensación de que el dato existía. **Existe: en el otro artefacto.**
