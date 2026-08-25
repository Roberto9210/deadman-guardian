# M2/M3 — de dónde sale la verdad al rearrancar

**Estado: OPCIÓN A IMPLEMENTADA el 2026-08-22, con las tres condiciones de Roberto.** El resto del
documento queda como registro de por qué la forma es ésta.

Las condiciones, y dónde viven:

1. **Un baseline adoptado puede bloquear, jamás aplanar.** `PnlBook.HasObservedFill` sólo lo enciende
   un fill real aplicado; la puerta del breach exige ese flag antes de `EnterLockout`, y sin él escribe
   `LIMIT_BREACHED_BASELINE_ONLY` (con `flattened: false`) y entra en fail-closed sin tocar al broker.
   La puerta corre ANTES de la rama de clear para que un breach de baseline sostenido no oscile.
2. **Discrepancia dentro de tolerancia ⇒ se adopta el más conservador** (`min(plataforma, checkpoint)`)
   y los dos números van a `PNL_BASELINE_ADOPTED` con su fuente. Nunca el más amable.
3. **El período.** Verificado por reflexión sobre `Account` y `AccountItem`: **NT8 no expone nada que
   diga qué período cubre su `GrossRealizedProfitLoss`** — números pelados, sin “desde cuándo”. La
   única corroboración disponible es el propio checkpoint del mismo `dayKey` en el ledger del guardián
   (que ahora registra `grossRealizedPerAccount` para eso). Sin checkpoint del día, o con una cifra que
   se movió más allá de la tolerancia mientras nadie corría, **no se adopta nada**: fills-en-ausencia y
   un reset de sesión de la plataforma son indistinguibles, y el motivo queda en la razón del estado y
   en `PNL_BASELINE_REFUSED` con los dos números.

Consecuencia honesta del punto 3, dicha en vez de resuelta por analogía: **el primer arranque con esta
versión sobre un ledger viejo no puede corroborar nada** (los checkpoints previos no llevan el campo) y
va a rehusar si hay realizado ≠ 0. Desde el primer día completo con esta versión, los checkpoints
llevan la cifra y la adopción funciona. Es el mismo principio de siempre: sin productor no se confía en
el consumidor.

2026-08-22.

Con M15 resuelto, "¿de qué cuenta hablamos?" ya lo contesta el sello. Queda la pregunta de fondo: el
libro de P&L de Core es memoria pura, y al rearrancar lee cero mientras la plataforma recuerda la
sesión. Si hubo realizado ≠ 0 → discrepancia inexplicable → `FailClosed` hasta que ruede el día
(**M2**, observado hoy: 103 `PNL_DISAGREEMENT`, "core 0.00 vs platform −50.00"). Si hay una posición
abierta → Core no la ve, jamás lee el no realizado → **reporta cero pérdida bajo una ventana `ARMED`**
(**M3** — no falla: miente).

**Criterio de aceptación acordado:** reiniciar NT8 a mitad de sesión con realizado ≠ 0 y una posición
abierta ⇒ el guardián vuelve a `ARMED` con el P&L del día correcto, y M2/M3 pasan de rojas (afirmando
el defecto) a verdes (afirmando lo correcto).

## Opción A — reconciliar contra la plataforma al arrancar

Al restaurar un sello del **mismo `dayKey`**, adoptar como base el realizado del día que reporta la
plataforma (`AccountItem.GrossRealizedProfitLoss`, ya mapeado) y las posiciones abiertas
(`GetPositions`, ya en el puerto `IBrokerActions`). El libro arranca con `baseline = platform` en vez
de cero, y el cross-check de §5.4 compara `baseline + lo_que_Core_vio_después` contra la plataforma.

- **Si la fuente falta** (desconectada al arrancar): no hay base ⇒ desconocido ⇒ `FailClosed` con
  motivo — exactamente lo de hoy, sin degradación. Se reconcilia en la primera observación coherente.
- **Si la fuente miente**: es **la misma fuente** contra la que §5.4 valida siempre. No aparece un
  tercer testigo: la plataforma ya es el árbitro del cross-check y el proveedor del unrealized. Un
  baseline falso produce las mismas consecuencias que un `GrossRealizedProfitLoss` falso hoy.
- **Modo de falla característico**: NT8 resetea su "día" en su propio horario de sesión. Si el día de
  NT8 y el `dayKey` del guardián no coinciden en el borde (reinicio 16:55–17:05 CT), el baseline
  adoptado puede ser del día viejo. Mitigación: reconciliar sólo dentro del mismo `dayKey` del sello,
  y registrar el baseline adoptado en el ledger (`PNL_BASELINE_ADOPTED`, con cuenta, monto y fuente)
  para que sea auditable y no un número que apareció de la nada.
- **Sello**: intacto. Sólo se toca el libro; límite, expiración y estado restaurado quedan como hoy.

## Opción B — persistir el libro de Core a disco en cada cambio

Escribir el libro (posiciones, realizado, comisiones, ids vistos) en cada `OnExecution`, y recargarlo
al arrancar.

- **Si el archivo falta o está roto**: desconocido ⇒ `FailClosed`. Recuperable sólo esperando el día.
- **Si miente** (archivo viejo, editado, o de otra sesión de NT8): Core hereda un estado falso **con
  la autoridad de propio**, y el cross-check pierde su gracia — las dos "fuentes" ya no son
  independientes si una se alimentó de un archivo que nadie valida contra el mundo.
- **Lo que NO resuelve, y es la objeción decisiva**: los fills que ocurren **mientras el proceso está
  muerto** (NT8 cerrado con órdenes que se ejecutan al reabrir, o el gap del F5). El archivo reproduce
  fielmente lo que Core vio, y lo que Core no vio sigue sin existir ⇒ **M2 reaparece intacto** en
  cuanto un fill cae en la ventana ciega. Persistir sólo arregla el caso en que no pasó nada durante
  el apagón — que es justamente el caso que menos importa.
- Costo adicional: un archivo más que puede corromperse, con escritura en la ruta caliente de cada
  ejecución, y una superficie nueva de manipulación (editar el libro para "resetear" las pérdidas del
  día — un archivo que el sello no cubre).

## Elección: **Opción A**, con dos reglas

1. **Reconciliar sólo al restaurar un sello del mismo `dayKey`**, nunca en el armado inicial (ahí el
   día empieza y cero es verdad) ni tras el rollover (ídem).
2. **El baseline adoptado se escribe en el ledger** — `PNL_BASELINE_ADOPTED` con cuenta, monto,
   posiciones adoptadas y `dayKey` — porque un número que entra al cálculo del límite sin dejar rastro
   sería un dato que el verificador no puede auditar.

**Motivo, en una línea:** A usa la fuente que ya es árbitro y cubre la ventana ciega; B agrega un
archivo, una superficie de manipulación y una falsa independencia, y deja M2 vivo para el único caso
difícil. B sólo gana si no confiáramos en la plataforma — y §5.4 entero está construido sobre
consultarla.

**Interacción con el certificado (aviso a Ventana B cuando se implemente):** aparece un evento nuevo
(`PNL_BASELINE_ADOPTED`). El verificador debería tratarlo como parte del día — el realizado del día ya
no siempre nace en cero dentro del segmento — y eso toca su derivación de `outcome` si suma pérdidas
desde eventos. Avisar antes de cortar release.
