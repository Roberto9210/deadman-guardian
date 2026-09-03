# El paquete del próximo F5 — se decide ENTERO o no se decide

**2026-09-03.** Documento datado: se anota, no se reescribe.

> **Este documento existe por un motivo concreto: el F5 no lleva una cosa, lleva tres, y las tres
> cambian algo observable. Si se decide de a una, se descubre a mitad de camino.**

---

## 0. El estado de hoy, para que la comparación sea posible

| | |
|---|---|
| `GuardianCore.dll` cargado | sha256 **`6529a92ae9b47240…`**, construido 2026-09-02 18:09:57 |
| `NinjaTrader.Custom.dll` cargado | sha256 **`134f6f82ce6ab47d…`**, construido 2026-09-02 18:14:51 |
| fuente que corresponde | `ea60de9` (`src/`, 1-sep 13:32) y `485787f` (`nt/addon/`, 1-sep 09:51) |
| commits de fuente **fuera** del binario | `05d20bb`, `21018bb` (2-sep) y `48c6155` (3-sep) |

**Cómo verificar cuál está cargado, sin acordarse de nada:** buscar los literales dentro del DLL en
sus dos codificaciones (nombres UTF-8, literales UTF-16). Es lo que probó P-A y sirve para las tres
piezas de abajo.

---

## 1. Las tres piezas

### PIEZA A — `ORDER_OBSERVED_WHILE_LOCKED` (hecho, sin desplegar)

**Commits**: `c9b196f` (rojo primero) y `48c6155` (verde). 355 tests.

**Qué cambia al desplegarse**: estando `LOCKED`, cada orden observada sobre la cuenta guardada
escribe una fila `ORDER_OBSERVED_WHILE_LOCKED {account, orderId, instrument, action}`. **No cancela
nada. Cero llamadas al broker**, fijado por `G8b_It_is_a_record_and_not_a_brake` (roto a propósito el
2026-09-03: agregar un `CancelAllOrders` ahí lo pone en rojo).

**Efecto colateral que hay que aceptar con la pieza**: NT8 emite varias `OrderUpdate` por orden —12
eventos para 4 orderIds el 26-ago— así que serán **unas pocas filas por orden**, sin deduplicar. El
motivo de no deduplicar está en el código: haría falta un conjunto de ids que un reinicio pierde.

**Contrato**: tipo de evento **aditivo**. Ventana B midió que su verificador **marca** los tipos
desconocidos (`UNKNOWN_EVENT_KIND`, exit 0) y no los rechaza ⇒ no rompe los certificados ya emitidos.
*(Referencia cruzada a otro repo, medida por ellos.)*

**Dependencia del sitio — ver `frases-retiradas-20260903.md` §5c.** El sitio dice hoy que la orden
*"no queda registrada"*, lo cual **es cierto ahora y falso el día del F5**. **La corrección del sitio
sale JUNTO con este despliegue, no después.**

---

### PIEZA B — los dos commits del 2-sep (`05d20bb`, `21018bb`)

**Qué cambia**:

| | antes (lo que corre) | después |
|---|---|---|
| clave del payload de `LOCKOUT_INCOMPLETE` | `"exhausted"` | **`"needsHuman"`** |
| constante | `MaxFlattenAttempts` | `FlattenAttemptsBeforeHuman` |
| `Messages.cs` | — | **sólo comentarios**, verificado quitando las líneas `//` |

**Lo que hay que decidir con los ojos abiertos: esto RETIRA P-A.** La predicción sellada del 3-sep
usa `"exhausted"` como falsador de identidad del binario. **Después de este F5 ese falsador deja de
existir** y hay que reemplazarlo — lo natural es que pase a ser `"needsHuman"`, es decir el mismo
falsador con el valor invertido, y **eso sólo funciona si alguien lo escribe antes del F5**.

**El lector del ledger**: `05d20bb` dejó el addon leyendo **las dos claves** (`needsHuman ?? exhausted`),
así que un ledger con filas viejas se sigue leyendo. **El escritor, en cambio, cambia de golpe**: el
ledger va a tener las dos claves en distintos tramos, para siempre. **Eso es correcto y es el
registro haciendo su trabajo — pero cualquier análisis que cuente `exhausted` tiene que mirar las
dos.**

---

### PIEZA C — identidad de build en `GUARDIAN_STARTED` — **HECHA 2026-09-03 (`0ca438a`), sin desplegar**

> **CORRECCIÓN AL PROPIO DOCUMENTO, mismo día.** Abajo esta pieza se llamaba `buildHash`. **El campo
> se llama `coreBuild`**, y el nombre estaba fijado desde el 2026-09-02 en
> `docs/freno-identidad-build-20260902.md` §2. Un segundo nombre para la misma cosa es el defecto que
> perseguimos toda la jornada; documento vivo, así que se corrige en vez de anotarse.
>
> **Y son DOS campos, no uno**, por una medición que pidió el operador antes de aplicar:
> `ReadCoreAssemblyBytes()` lee **el archivo en disco**, e `install.ps1` copia el DLL nuevo mientras
> el proceso sigue ejecutando el viejo en memoria ⇒ durante esa ventana un hash de archivo
> **declararía el binario nuevo mientras corre el viejo**, mintiendo en el único momento que importa.
>
> | campo | qué dice | de dónde sale |
> |---|---|---|
> | **`coreMvid`** | **qué se está EJECUTANDO** — `ModuleVersionId` del ensamblado cargado, de metadatos ya en memoria | lo calcula **Core**: quien contesta *"qué build soy"* debe ser el build que se describe |
> | **`coreBuild`** | **qué hay EN DISCO** — sha256[:16] del archivo, igual que el certificado | lo pasa el **host**: leer un archivo es I/O y Core no hace ninguna |
>
> **Su DESACUERDO es la ventana de dos etapas, visible en el registro por primera vez.**
> Hashear los **bytes cargados** no es alcanzable: la imagen en memoria no es byte-idéntica al
> archivo. **Ausente antes que inventado**: el valor que no se puede obtener no escribe su clave.

**Lo que sigue es el texto original de la pieza, conservado:**



**Qué falta hoy**: el ledger **no puede decir qué binario escribió ninguna de sus 8.119 filas**.
`GUARDIAN_STARTED` lleva sólo `{"state":…}`; un `grep` de `buildHash|addonBuild|coreBuild` sobre el
ledger entero da **0**; `adapter.log` tampoco lo registra. La única superficie que lo lleva es el
**certificado**, que se emite a pedido.

**Forma**: un campo `buildHash` en el payload de `GUARDIAN_STARTED`, escrito en cada arranque,
encadenado como todo lo demás — **así nadie tiene que acordarse de mirar**. Aditivo ⇒ mismo contrato
de extensión que la pieza A y la misma discusión pendiente que `configHash`.

**Y la razón por la que va EN ESTE F5 y no en otro**: **es la pieza que vuelve innecesaria a toda
esta sección 0.** Hoy identificar el binario cargado exige medir literales dentro de un DLL; con
`buildHash` en el arranque, **el propio registro lo dice**. La ironía a nombrar: es también la pieza
cuyo despliegue no se puede confirmar leyendo el ledger anterior a ella.

**No está implementada.** Su rojo-primero y su diseño son trabajo aparte.

---

## 2. Por qué se decide ENTERO

**Las tres se pisan:**

- **A cambia lo que el sitio debe decir.** Desplegar A sin corregir el sitio publica una frase falsa
  desde el minuto uno.
- **B destruye el falsador de identidad que usamos para verificar despliegues.** Desplegar B sin
  reemplazar P-A nos deja sin la técnica **justo cuando más hace falta** — en el F5 que cambia tres
  cosas a la vez.
- **C es la que arregla el problema que B empeora.** Si C entra en el mismo F5, el ledger empieza a
  declarar su binario y **B deja de importar como falsador**: se pasa de un truco (un literal
  elegido a mano) a un dato (el hash, escrito a propósito).

> **El orden correcto es C con B, y A con la corrección del sitio en el mismo movimiento.**
> Desplegar B solo es el peor de los tres caminos: quita la herramienta de verificación y no la
> reemplaza.

## 3. Lista de comprobación del F5, para el día que se decida

1. **Antes**: anotar el sha256 de los dos DLL cargados y el `seq` de cabeza del ledger.
2. **Después del F5**: confirmar que los sha256 **cambiaron**, y que los tres literales están donde
   deben — `ORDER_OBSERVED_WHILE_LOCKED` **presente**, `needsHuman` **presente**, `exhausted`
   **ausente** como literal del emisor.
3. **Primer arranque**: `GUARDIAN_STARTED` debe traer **`coreMvid`** y **`coreBuild`**, y este
   último debe coincidir con el sha256[:16] medido en el punto 2.
4b. **LOS DOS CAMPOS TIENEN QUE CONTAR LA MISMA HISTORIA.** Si `coreBuild` cambió y `coreMvid` no,
   el F5 quedó a medias: hay un DLL nuevo en disco y uno viejo ejecutando. Ésa es la ventana de dos
   etapas, y desde esta pieza **se lee en el registro** en vez de medirse a mano.
4. **El sitio**: aplicar la corrección de `frases-retiradas-20260903.md` §5c **el mismo día**.
5. **Reemplazar P-A** en cualquier predicción sellada futura: el falsador pasa a ser `needsHuman`
   presente, o directamente `buildHash` si C entró.

**Nada de esto está hecho. Este documento es la decisión, no su ejecución.**
