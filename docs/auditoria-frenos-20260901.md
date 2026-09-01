# Auditoría de frenos — 2026-09-01

**Sólo lectura. Ninguna decisión, ningún arreglo, ningún cambio de código.**
Encargada tras un hallazgo de ALAYA, aplicado a este repositorio.

---

## 1 · La pregunta, y por qué no es "¿funciona?"

En ALAYA encontraron un freno —el OCG— **bien cableado, corriendo en cada ciclo, incapaz de rechazar
nada**: sus doce criterios reciben constantes escritas a mano que nunca los activan. Predijeron cero
bloqueos leyendo el código y lo confirmaron contra catorce días: **180.688 evaluaciones, cero
rechazos.** Y el motor firma en su propio registro que el OCG validó.

> ### Un freno que no puede fallar no es un freno, es una firma.

De ahí el diagnóstico que se aplica acá: **ante cualquier freno, la pregunta no es «¿funciona?» sino
«¿existe algún input ALCANZABLE que lo haga decir que no?»** — y después, por separado: **¿alguna vez
lo dijo?**

Las dos respuestas dan cuatro casos:

| | |
|---|---|
| **alcanzable y disparó** | protección demostrada |
| **alcanzable y nunca disparó** | plausible, sin evidencia |
| **NO alcanzable** | es una firma, no un freno |
| **no determinable** | se dice, no se estira |

**Y un QUINTO caso, que este esquema no tenía y que esta auditoría encontró** (agregado por el
operador el 1-sep):

| | |
|---|---|
| **disparó, fue correcto, y NO CAMBIÓ NADA** | un freno desoído es, en resultado, un freno que no disparó |

Es el caso más humano de los cinco y **es la misma cosa que los 165 avisos**: no alcanza con que un
freno actúe: tiene que llegar a alguien que lo atienda. Un esquema que sólo mira el código no tiene
dónde poner esto, porque el fallo no está en el código.

---

## 2 · Dónde se buscó la evidencia

| fuente | alcance |
|---|---|
| `ledger.jsonl` | **8.027 entradas**, seq 1 → 8027, del **2026-08-21 05:15Z** al **2026-09-01 00:18Z** (viva; el guardián está corriendo mientras se escribe esto) |
| `adapter.log` | **69 arranques**, misma ventana. Es el único rastro durable del adaptador |
| logs de NinjaTrader | **99 archivos**, 2026-08-20 → 2026-08-31 |
| `git log` | historia completa del repositorio |
| certificados emitidos | 12 en `deadman-guardian/certificates/` |

**Control de que el espacio de búsqueda es el correcto** (una ausencia en el lugar equivocado no es
evidencia de nada): los logs de NT8 contienen 2.176 líneas con `deadman`, 16 `subscribed to` y la
línea `LOCKED …` del 31-ago 09:10:30. El `adapter.log` contiene los 69 `boot`, 9 `ARMED`, 367
`flatten` y 23 `certificate issued`. **Las fuentes sí registran lo que este producto hace.**

---

## 3 · El inventario, freno por freno

### 3.1 · Instalador — cuatro guardas, **ningún registro propio**

`install.ps1` no escribe archivo de log. Lo que una guarda hizo se sabe **sólo si alguien lo
escribió en prosa** — y eso es, por sí mismo, un resultado de esta auditoría.

| freno | ¿input alcanzable? | ¿disparó? | clasificación |
|---|---|---|---|
| **exit 2** — NinjaTrader abierto (`:128`) | sí: correr el script con NT8 abierto | **SÍ, el 2026-08-21**, registrado en el mensaje del commit `ab5df29` | **protección demostrada** ⚠ ver abajo |
| **exit 4** — `.cs` en el repo que el instalador no despliega (`:81`) | sí: agregar un archivo sin agregarlo a las listas (pasó dos veces esta semana con `PanelPlacement.cs` y `SoundChannel.cs`, ambos agregados a mano) | **sin evidencia** | plausible, sin evidencia |
| **exit 5** — build más viejo que su fuente (`:204`) | sí — **y la condición es VERDADERA ahora mismo**: DLL 19:09:03, `Certificate.cs` 19:38, `Messages.cs` 19:41 | **sin evidencia** de que haya disparado nunca | plausible, sin evidencia |
| **exit 6** — la copia no verifica ⇒ revierte (`:425`) | sí: archivo bloqueado, antivirus, disco | **sin evidencia**. Su propio defecto (anunciar `ROLLED BACK` sin haber restaurado) lo atrapó **un test**, no una corrida | plausible, sin evidencia |

⚠ **exit 2 es el QUINTO CASO**, y está escrito por quien lo vivió (`ab5df29`):
*"on 2026-08-21 a guard fired, printed one line, and the operator watched it scroll past and pressed
F5 anyway."* **Disparó, fue correcto, y no cambió nada** — la regla del canal, en un instalador.
Por eso su clasificación de arriba lleva el aviso: contarlo como *protección demostrada* a secas
sería el mismo optimismo que la auditoría persigue.

**Precedente propio, y es el que prueba que esta clase ya se cazó acá una vez**: el `exit 3` fue
**borrado por inalcanzable** en ese mismo commit, con el motivo escrito en el hueco que dejó
(`install.ps1:443-445`):

> *"a check that cannot run is not a check, it is decoration that suggests a protection nobody has."*

### 3.2 · Núcleo del guardián — respaldado por el ledger

**Disparó (protección demostrada):**

| freno | evidencia en 8.027 entradas |
|---|---|
| Lockout por límite diario (`Guardian.cs:767-771`) | **`LIMIT_BREACHED` ×2** — 2026-08-26 23:44Z y 2026-08-31 14:10Z |
| Fail-closed por cuenta ilegible (`:704`) | **12 de los 13** `FAIL_CLOSED_ENTERED`: *"AccountUnknown on Sim101: account is Disconnected"* |
| Fail-closed por fuentes que discrepan (`:704`) | **1 de 13**: *"SourcesDisagree … differ by … tolerance 5.0"* |
| Rechazo de órdenes durante el lockout | **`ORDER_REJECTED_LOCKED` ×12**, todas del 2026-08-26 |
| Cruce de P&L que se niega a promediar | **`PNL_DISAGREEMENT` ×2960** |
| Aviso de aplanado incompleto | **`LOCKOUT_INCOMPLETE` ×169** |

**Nunca disparó — trece tipos de evento declarados y cero veces escritos:**

| freno | ¿input alcanzable? | clasificación |
|---|---|---|
| `CONFIG_TAMPERED` (`:562`) editar `config.json` bajo sello | **sí**: el addon observa el archivo cada tick (`DeadmanGuardianAddOn.cs:333`) | plausible, sin evidencia |
| `SEAL_MISMATCH` (`:225`) editar a mano el sello dentro de `state.json` | **sí**: archivo de texto en Documentos, se verifica en cada arranque (51 `SEAL_VERIFIED`) | plausible, sin evidencia |
| `STATE_CORRUPT` (`:440`) | **sí**: un corte de luz durante una escritura. En otro repositorio de esta casa pasó exactamente eso el 31-ago | plausible, sin evidencia |
| `CONFIG_REJECTED` (`:462,469,475`) config inválido al armar | **sí**: editar el config con el guardián desarmado. Nunca hubo un `arm rejected` en 69 arranques | plausible, sin evidencia |
| `LEDGER_VERIFY_FAILED` (`:209`) | **sí**: editar `ledger.jsonl`. La cadena sobrevivió incluso a un apagón | plausible, sin evidencia |
| `PNL_UNCOMPUTABLE` (`:575,691,701`) | **sí** | plausible, sin evidencia |
| `CLOCK_ANOMALY` / `CLOCK_SUSPECT` (`:817,818,825`) | **sí**, pero **esta máquina no lo produce** (M4). Simulado por `G13a`-`G13d` | plausible, sin evidencia — *simulada pero no producida* |
| `PNL_BASELINE_REFUSED` (`:420`), 4 ramas | **sí**: la más alcanzable es *"realised P&L sin checkpoint del mismo día"* — operar por la mañana y armar después. 12 adopciones, 0 negativas | plausible, sin evidencia |
| `LIMIT_BREACHED_BASELINE_ONLY` — la puerta de **M22** (`:736`) | **sí, pero exige una conjunción**: reinicio con baseline adoptada + el total cruza el límite + lo observado por sí solo NO lo cruza + ningún fill observado (`:721-723`) | plausible, sin evidencia |
| `NOTIFY_FAILED` (`:648`) | **sí**: el addon SÍ registra un observador (`:114`); alcanzable si `OnLedgerEntry` lanza | plausible, sin evidencia |
| `CONFIG_CHANGE_REJECTED` (`:540`) | **ver §4.1 — la API no tiene llamador; sólo se alcanza de rebote** | **RECLASIFICADO 1-sep: alcanzable Y DISPARÓ** ⚠ ver §4.1 |
| `FOREIGN_ACCOUNT_ORDER_OBSERVED` (`:605`) | **NO alcanzable** — ver §4.2 | **no alcanzable** (honesto) |

### 3.3 · Riel de cuenta de los bots (`nt/bots/BotAccountRule.cs:87-130`)

**Ocho ramas de `Deny`.** Alcanzables: la cuenta fondeada `2127534` está presente y **desconectada**
en las tres corridas registradas — la rama *"a non-simulator account is CONNECTED"* está **a una
conexión** de disparar. El riel **se evaluó 72 veces en una sola corrida** y siempre permitió.

**Clasificación: alcanzable, nunca rechazó.** Con una salvedad de método: el veredicto va al informe
del bot, no a un ledger, y **sólo se conservan 3 corridas**. Un `DENY` de una corrida borrada no
dejaría rastro ⇒ para las corridas viejas es **no determinable**.

**Y un freno vecino que SÍ disparó**: el presupuesto del propio bot, *"order REFUSED by the budget
(entry): OVER_NET_CAP"*, **dos veces y en dos corridas distintas** — la del **2026-08-26 23:41Z** y
la del **2026-08-31 14:07Z**. Protección demostrada. La segunda cayó **tres segundos después** del
`LIMIT_BREACHED` de las 14:10:29Z; que sean el mismo episodio es plausible y **no se verificó**.

### 3.4 · Contención de vocabulario de los mensajes

`Messages.Retired` + `C_RetiredPhrasesTests` (124 líneas). Es un freno **de construcción**, no de
ejecución: su "no" es un test rojo.

**Disparó, y en su primera corrida**, con el commit como evidencia (`a7af16e`):
*"A10 gets a check for the surfaces that cannot obey it — **and it caught two on its first run**."*
**Protección demostrada** — y la única de esta lista cuyo disparo no dependió de que el mundo
produjera una condición.

---

## 4 · Los hallazgos

### 4.1 · `TryChangeConfig` no tiene llamador en el producto — y el certificado cuenta lo que produce

`Guardian.TryChangeConfig` (`:524`) es público y **ningún componente del producto lo llama**. El addon
llama a doce miembros del guardián; ése no está. Sus únicos llamadores son **los tests** y **`Arm`**,
que delega en él cuando ya hay sello vivo (`:457`).

> ### ⚠ RECLASIFICADO EL MISMO DÍA: alcanzable **y disparó**, y no por un argumento
>
> Unas horas después de escribir esto, **un test nuevo (`Cert2_SealedSnapshotTests`) armó dos veces
> por un descuido mío**, el segundo `Arm` cayó en `TryChangeConfig` y **el freno lo rechazó**:
> *"the configuration is sealed until … every change is rejected while sealed"*. Sale del bucket 2 y
> entra en **protección demostrada** — con la precisión que corresponde: **disparó en el banco de
> pruebas, no en producción** (el ledger sigue en 0).
>
> **Y la lección de método es más grande que el freno**: yo había contestado la pregunta 1 con un
> **razonamiento** sobre dobles clics. La alcanzabilidad quedó demostrada por **accidente**, y
> escribiendo otra cosa. **Un input que aparece solo vale más que uno que se argumenta** — y sugiere
> la técnica para el resto del bucket 2: en vez de discutir si son alcanzables, **intentar
> alcanzarlos** desde un test.

**El único camino alcanzable desde la interfaz, entonces**: apretar *"Arm for today"* **dos veces**
antes de que el panel se refresque. El botón sólo es visible en `Disarmed` (`DeadmanGuardianAddOn.cs:1093`), el
manejador **no lo deshabilita al clickear** (`:820-824`) y el refresco es de **1 segundo**
(`PnlEvaluationIntervalMs`). Un doble clic entra. ⇒ **alcanzable, nunca disparó.**

**Lo que importa no es el freno, es lo que se publica sobre él.** El certificado publica
`commitment.changeAttemptsWhileSealed`, y su única fuente es ese evento (`Certificate.cs:193`).
El campo se llama *intentos de aflojar el límite estando sellado*; **el conjunto que cuenta es
«clics de más en Arm»**.

### 4.2 · `FOREIGN_ACCOUNT_ORDER_OBSERVED` es inalcanzable — y aun así NO es una firma

El addon se suscribe **sólo a la cuenta guardada** (`Accounts.Find(_guardedAccount)`, `:248-259`), así
que ninguna orden ajena llega jamás a `OnOrderObserved`. **No hay input alcanzable.**

**Y sin embargo no es el caso de ALAYA**, por tres motivos que conviene tener separados:

1. **el código lo declara**: *"if a foreign order ever reaches this method the wiring changed
   underneath us, and that is precisely the thing worth seeing"*;
2. **nadie firma con él**: ningún mensaje, documento ni certificado afirma que esta comprobación
   protege algo;
3. **cuesta cero**: no evalúa nada, sólo mira una lista.

> **La diferencia entre un cable trampa y una firma no es la alcanzabilidad: es si algo AFIRMA que
> te protegió.** El OCG de ALAYA es grave porque el motor firma «validado … + OCG Guard». Éste no
> firma nada.

### 4.3 · **El hallazgo grande, y no lo buscaba**: dos lockouts que el certificado no ve

`Ev.LimitBreached` se escribe en **un solo lugar** (`Guardian.cs:767`). Pero `EnterLockout` tiene
**tres** llamadores:

| ruta | evento que deja | ¿lo cuenta el certificado? |
|---|---|---|
| límite diario (`:771`) | `LIMIT_BREACHED` | **sí** → `lockoutsTriggered` |
| **config editado bajo sello** (`:566`) | `CONFIG_TAMPERED` | **NO** |
| **sello editado a mano en `state.json`** (`:228`) | `SEAL_MISMATCH` | **NO** |

El certificado lee **seis** tipos de evento en total (`Certificate.cs:192-206`): `LIMIT_BREACHED`,
`CONFIG_CHANGE_REJECTED`, `CLOCK_ANOMALY`, `CLOCK_SUSPECT`, `ORDER_REJECTED_LOCKED` y los dos
límites de episodio fail-closed. **`CONFIG_TAMPERED` y `SEAL_MISMATCH` no están.**

Consecuencia, leyendo `Certificate.cs:56` (`LimitRespected => LockoutsTriggered == 0 && …`):

> ### Un día en el que el trader editó el config para aflojar su límite y quedó bloqueado por eso sale con `lockoutsTriggered: 0`, `changeAttemptsWhileSealed: 0` y **`limitRespected: true`**.

**El freno funciona. La evidencia no lo lleva.** Es la primera clase de la casa —una afirmación
cierta sobre el conjunto equivocado— **en el único artefacto que va a un tercero**, y llegó por la
pregunta encargada: *¿qué input alcanzable hace que este freno diga que no?*

**Y es EL día que un trader querría poder mostrar**: la prueba de que alguien intentó aflojarse el
freno y no pudo. El único que el documento no sabe contar.

> **Es el inverso exacto del hallazgo de ALAYA, y le da a la clase madre una cara que no habíamos
> visto.** Allá, un freno que **no podía fallar** firmaba que había protegido. Acá, un freno que
> **sí actuó** no aparece. Los dos producen un documento que no coincide con la realidad: **uno
> afirma de más, el otro de menos.**

**No verificado**: si la ventana de tiempo del lockout cae dentro de un episodio fail-closed abierto,
el evento entra al certificado como clave del mapa `reasons`. No se comprobó si eso puede ocurrir en
la práctica, y **no cambia el veredicto de los tres campos de arriba**.

### 4.4 · Tres claims del certificado que sólo pueden valer una cosa

Mientras se trazaba lo anterior: **nada en el producto llena `Gaps`, `Anchors` ni `KeyId`** — no hay
una sola asignación en `src/` ni en `nt/`. Por lo tanto, en los 12 certificados emitidos:

- `continuity.gaps` es **siempre `[]`** — que se lee *"no hay huecos"* cuando lo que pasa es que
  **nadie los calcula**;
- `anchors` es **siempre `[]`**;
- el bloque `signature` **nunca se emite** (ningún firmante cableado).

La lista `limitations` ya hace exactamente este trabajo para `ordersRejectedWhileLocked` (*"always
zero in this build, and that is a statement about the software rather than about the trader"*).
**Estos tres no están en esa lista.**

---

## 5 · No determinable, dicho como tal

- **Si las guardas 4, 5 y 6 del instalador dispararon alguna vez.** No hay registro; sólo prosa.
  La respuesta honesta es *no determinable*, no *"nunca"*.
- **Veredictos del riel de los bots en corridas ya borradas** (se conservan 3).
- **Dos excepciones `Tick: Object reference not set…`** en `adapter.log`, ambas del **primer arranque
  del producto** (2026-08-21T05:15:05, líneas 3 y 5) y **nunca más en 69 arranques**. Lo que ese tick
  no evaluó, no se sabe.

---

## 6 · Lo que esta auditoría NO dice

1. **No dice que los frenos del grupo 2 fallen.** Dice que **nadie los vio decir que no**, que es un
   estado distinto de "andan".
2. **No es una revisión de corrección.** Un freno puede ser alcanzable, haber disparado, y estar mal.
3. **La ventana es la vida entera del producto** (21-ago → hoy), que son **once días**. Un freno anual
   no aparecería acá aunque fuera perfecto.
4. **`limitRespected` no se tocó**, y sigue siendo semántica y decisión de producto.
