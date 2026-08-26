# deadman-guardian — mapa para sesiones futuras (actualizar al cierre de cada tanda)

Última actualización: 2026-08-26 (guardas del instalador; M22 abierto y en cola).
Es un mapa, no un manual. El historial forense está en `git log`; la especificación en `SPEC.md`,
las decisiones revisadas en `AMENDMENTS.md`, y el diagnóstico del error espejo en
`docs/error_espejo.md`.

## 1. Qué es
Add-on de NinjaTrader 8 que impide a un trader de prop firm romper su propio límite de pérdida
diaria. Observa ejecuciones y P&L de una sola cuenta sellada; al alcanzarse el límite cancela las
órdenes de esa cuenta y aplana sus posiciones, y bloquea entradas por el resto del día. Corre local,
sin red, y su registro es un ledger encadenado por hash que sirve de evidencia.

**Las dos cosas que JAMÁS hace: abrir posiciones y generar señales.** No es una estrategia, no
opina sobre el mercado, y no envía una orden que no sea una cancelación o un aplanado de la cuenta
que le sellaron.

## 2. Método obligatorio
1. **Verificar contra el código antes de diseñar.** Toda afirmación —de un reporte previo, de este
   archivo, o de un prompt— se re-verifica con grep/lectura. Si no se puede verificar, se dice "no
   verificado" y no se construye encima. **Un hallazgo no verificado no es cierto.**
2. **Rojo primero.** La prueba se escribe antes del arreglo, se la ve fallar, y recién después se
   toca el código. **Un verde que no se puso rojo antes no prueba nada.** Si la decisión vive en
   código que un test no puede ejecutar (adaptador), extraerla a código puro primero: que algo sea
   inejecutable en un test es parte del defecto, no una excusa.
3. **Diffs antes de aplicar**, y esperar el OK en cualquier cambio que toque la puerta del breach.
4. **Commits separados por fix, por ruta explícita. Nunca `git add -A`.**
5. **Una sola sesión por repositorio.** Esta ventana es la de `deadman-guardian`; no toca `deadman`
   (Ventana B) ni `deadman-research` (exclusivo de Roberto, ni siquiera lectura).
6. Cada reporte abre identificando la ventana.
7. No declarar "arreglado" sin salida de prueba real. Reportar los fallos tal cual.
8. Herramientas: los heredocs destrozan los backslashes de rutas Windows (`nt\addon` llegó como BEL,
   `nt\bots` como backspace). Escribir los scripts de parche por archivo y correrlos.

## 3. La clase de defecto de la casa
**TEXTO QUE AFIRMA MÁS DE LO QUE SU PROPIO CÓDIGO COMPROBÓ.**

Dos instancias el mismo día, en archivos sin relación:
- `install.ps1` imprimía *"the deployed binary is the one you just built"* — en un script que **no
  compila nada**. Sobrevivió sin un murmullo a un despliegue de un build viejo.
- `Guardian.cs:657` escribe *"daily loss limit reached on adopted figures alone"* sobre una cifra
  adoptada **que puede ser cero** (M22, abajo).

Y una tercera dentro del arreglo de la primera: la reversión de `exit 6` anunciaba `ROLLED BACK`
cuando el backup faltaba y no había restaurado nada. La atrapó su propio test.

**La regla: cada mensaje afirma exactamente lo que su propia comprobación establece, ni una palabra
más.** Si hacen falta dos afirmaciones, se escriben dos (`COPY VERIFIED` / `BUILD CURRENT`).

Corolario hermano: **un chequeo que existe no es un chequeo que corre.** Antes de confiar en una
protección, buscar su productor y correrla. Y un chequeo inalcanzable se borra — no se deja
decorando.

## 4. Operativa
- **Instalar**: `.\nt\install.ps1 -WithSoak -WithBots`. Se niega **antes de mutar** en tres casos y
  **revierte** en el cuarto:

  | código | significa |
  |---|---|
  | `2` | NinjaTrader está abierto — tiene el DLL tomado. Nada se copió. |
  | `4` | Hay `.cs` en `nt/addon`, `nt/soak` o `nt/bots` que las listas del script no gestionan. Nada se copió. |
  | `5` | El build es más viejo que su fuente. Compilar primero. Nada se copió. |
  | `6` | Lo desplegado no coincide con el repo. **Revierte solo** y lo dice; si la reversión falla, nombra `-Uninstall`. |

- **NT8 compila NinjaScript recién con F5**, no al arrancar. Un reinicio no compila; borrar
  `NinjaTrader.Custom.dll` hace que NT8 restaure una copia de fábrica en vez de construir una
  (`STEP3_FINDINGS.md §6`).
- **Cerrar NT8 desde la bandeja del sistema** (flechita `^` junto al reloj → click derecho → Exit).
  Cerrar la ventana principal no alcanza. Cerrar con gracia, nunca matar el proceso: un kill duro le
  roba al guardián su `GUARDIAN_STOPPED`.
- **Deshacer**: `.\nt\install.ps1 -Uninstall` borra lo desplegado y restaura el `csproj` del backup.
- **Verificar qué está corriendo**: el hash corto del DLL desplegado es el mismo valor que el
  certificado reporta como `issuer.buildHash`. La línea del Log de NT8 (`version='0.1.0.0'`) es
  idéntica en todo build jamás hecho: dice que algo cargó, no **cuál**.
- El guardián nunca coloca órdenes de entrada. Los bots de prueba (`DeadmanBotA`/`B`) sí envían
  órdenes fillables, sólo en Sim101 verificada por `Provider == Simulator`, y sólo con su archivo
  `.GO` presente.

## 5. Defectos abiertos (verificados, con archivo:línea)
| # | qué | dónde |
|---|---|---|
| **M22** | La condición 1 usa `!HasObservedFill` como proxy: bloquea sin aplanar aunque la pérdida **en vivo** cruce el límite sola. `Unrealized` viene de la plataforma y nunca fue adoptado. **Es el próximo.** | `Guardian.cs:655`, mensaje en `:657`; `PnlAccounting.cs:222` |
| M4 | Suspensión de la máquina ⇒ salto de reloj hacia adelante ⇒ bloqueo. **Sin medir.** | `Guardian.cs:753` |
| M5 | Un tick sin precio con posición abierta ⇒ bloqueo. Diseñado así. | `PnlAccounting.cs:218` |
| M6 | Desconexión ⇒ bloqueo. Diseñado así; ocurrió varias veces. | `PnlAccounting.cs:198` |
| M7 | La deduplicación de fills es condicional: una ejecución sin `ExecutionId` se cuenta cada vez. Sin evidencia de que NT8 emita id nulo. | `PnlAccounting.cs:144` |
| M20 | El feed reporta no-realizado sin que el broker reporte posición ⇒ el guardián sigue ciego. Residual, sin arreglar a propósito. | `PnlAccounting.cs:210-222` |
| M21 | Primer arranque sobre un ledger viejo ⇒ rehúsa el baseline hasta que ruede el día. Una vez por instalación. | `Guardian.cs`, `LoadSameDayCheckpointGross` |
| cert-1 | `daysCovered` está cableado en 1: el certificado afirma una cobertura que nadie contó. | `Certificate.cs:67`, `tools/IssueCertificate/Program.cs:87` |
| cert-2 | Auditar si `SPEC §2b`/`CERT_CONFORMANCE` prometen L2 como alcanzado en vez de disponible. Prueba: leer la frase con la función apagada. | `SPEC.md`, `CERT_CONFORMANCE.md` |
| ui-1 | El titular se deriva del **estado**, no de la **causa**: `CANNOT SEE YOUR ACCOUNT` es falso cuando el guardián sí ve la cuenta y sólo discrepa con ella. | `Messages.cs:51` |

`docs/error_espejo.md` lleva la clasificación completa (M1–M22) con el costo de cada uno. Las
pruebas `M4`–`M7` están **verdes afirmando el defecto**: pasan porque documentan lo que el código
hace hoy. Cada una debe ponerse **roja** cuando llegue su arreglo; una que siga verde después de su
propio arreglo significa que el arreglo no tocó nada. `M1b` y `M13` son contenciones y van verdes
siempre.

---
**Actualizar y commitear este archivo es el último paso del cierre de cada tanda.**
