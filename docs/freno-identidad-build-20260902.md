# Freno de identidad de build — DISEÑO, 2026-09-02

**Diseño, no implementación. Nada aplicado.** Se trae para que el operador lo falle.

**Qué cierra**: dos defectos con un campo.

| defecto | estado |
|---|---|
| **el ledger no puede decir qué código lo escribió** | **demostrado en vivo el 2026-09-02** (`docs/frontera-binario-20260901.md` §3) |
| **la ventana de dos etapas**: 77 s corriendo Core nuevo + addon viejo, sin test que lo cubra | **medido el 2026-09-02**, `seq` 8102–8104 |

Y convierte una **regla de procedimiento** —*«apretá F5 antes de armar»*— en un **freno mecánico**.

---

## 0 · Lo que ya está desbloqueado y lo que ya está medido

- **Ventana B**: agregar `buildHash` a `GUARDIAN_STARTED` **NO cae bajo el contrato**, con una
  condición: **no perder `fresh`**, y ponerlo en **LOS DOS** sitios de emisión.
- **Los dos sitios existen y son exactamente dos**: `Guardian.cs:185` (arranque en frío,
  `{state:"DISARMED", fresh:true}`) y `Guardian.cs:214` (restauración, `{state:…}`).
- **La medición del hash del Core ya funciona en producción, dentro de NT8**:
  `IssuerIdentity.BuildHashOf` + `ReadCoreAssemblyBytes` producen `issuer.buildHash` en
  **6 de 6 certificados emitidos**, con **5 valores distintos** y estable dentro de un mismo build
  (`eea8193626b8579b` aparece en dos días seguidos). **No es una capacidad a construir: es una que ya
  corre y discrimina.**

---

## 1 · Qué identidad usaría cada lado, y de dónde la saca

**Son tres valores, no dos**, y el tercero es el que hace que el freno funcione.

| campo | qué es | de dónde sale |
|---|---|---|
| **`coreBuild`** | el Core que **realmente se cargó** | **medido en runtime**: el addon lee los bytes de `typeof(Certificate).Assembly.Location` y llama `IssuerIdentity.BuildHashOf`. Es el mecanismo que ya emite `issuer.buildHash` |
| **`coreExpected`** | el Core contra el que **este addon fue compilado** | **estampado por `install.ps1`** en el momento de copiar: el script ya calcula ese hash y ya lo imprime (`GuardianCore.dll now deployed : …`) |
| **`addonBuild`** | identidad del **conjunto de fuentes del addon** | **estampado por `install.ps1`**: SHA-256 sobre los 5 `.cs` gestionados, que el script **ya hashea uno por uno** para su verificación de copia |

> ### La comparación que decide es `coreBuild` vs `coreExpected`, y no involucra al addon
>
> En la ventana de las dos etapas: NT8 carga el Core **nuevo** contra el addon **viejo**, que fue
> estampado con el hash del Core **viejo**. ⇒ **`coreBuild ≠ coreExpected`. Detectado.**

### Por qué NO se hashea `NinjaTrader.Custom.dll`, que es lo primero que uno haría

Ese ensamblado contiene **todo el NinjaScript del usuario** — indicadores, estrategias, otros
add-ons. **Cambia cada vez que el usuario compila cualquier cosa**, aunque no tenga nada que ver con
el guardián. Un freno atado a ese hash **rechazaría el armado por agregar un indicador**.

**Es la regla de la casa aplicada antes de escribir el código**: *un freno que castiga el uso
correcto salta la cola de defectos*. Acá se evita eligiendo bien el insumo, no arreglándolo después.

---

## 2 · Qué se escribe en el ledger

```
Guardian.cs:185  (frío)         {"state":"DISARMED","fresh":true,
                                 "coreBuild":"…","coreExpected":"…","addonBuild":"…"}
Guardian.cs:214  (restaurado)   {"state":"…",
                                 "coreBuild":"…","coreExpected":"…","addonBuild":"…"}
```

**`fresh` se conserva intacto** — es la condición de Ventana B, y es además el único discriminante
del cuarto estado del inventario (`GUARDIAN_STARTED` en frío, 1 vez por instalación).

**Campo ausente antes que inventado**: si un valor no se puede obtener, **no se escribe la clave**.
Nunca `""`, nunca `"unknown"` — es la doctrina que ya costó siete `?? ""` y el rechazo de
`DECORATIVE_FILLER` en el verificador.

---

## 3 · Dónde vive la decisión: **en el Core, no en el addon**

`GuardianCore` **no hace I/O de archivos en ningún lado** — invariante escrita en
`IssuerIdentity.BuildHashOf`: *«el LLAMADOR provee los bytes»*. Se respeta:

- **el addon MIDE** (lee bytes, calcula hash) y **pasa los tres valores**;
- **el Core DECIDE** con una función pura, testeable sin NT8, como todo lo demás del proyecto.

Eso además hace que el freno tenga **prueba conductual con control**: mismo estado, dos entradas
—coincidencia y desacuerdo— y la conducta tiene que diferir. Sin abrir la plataforma.

---

## 4 · Qué pasa en desacuerdo — **proporción, que es lo que puede irse de mano**

| | |
|---|---|
| **NO se detiene** el guardián | sigue ticando, sigue viendo la cuenta |
| **NO se deja de registrar** | el ledger sigue escribiendo; el desacuerdo **se registra**, no se silencia |
| **SE REHÚSA EL ARMADO** | y el panel dice por qué, con los dos hashes |

**Fail-closed sobre la acción que importa y nada más.** Un desacuerdo de build no es motivo para
dejar a alguien sin guardián; es motivo para no dejarlo **empezar** con un emparejamiento que ningún
test cubre.

---

## 5 · Si el addon NO PUEDE leer los bytes del Core — **hay bifurcación y traigo recomendación**

`ReadCoreAssemblyBytes()` devuelve `null` ante `IOException` o `UnauthorizedAccessException`.
Entonces `coreBuild` no existe y **no hay comparación posible**.

| opción | qué hace | costo |
|---|---|---|
| **(A) rehusar el armado también acá** | trata *«no pude comprobar»* distinto de *«coincide»* | **crea una forma nueva de que el guardián sea inusable** |
| **(B) permitir el armado** | *«no pude comprobar»* no es evidencia de desacuerdo | **el freno se apaga solo justo cuando algo raro pasa con el archivo** |

> ### Recomiendo **(A)**, con tres condiciones — y el argumento no es doctrinal, es de simetría
>
> **Desde afuera, «no puedo leer el DLL» y «alguien cambió el DLL» son indistinguibles.** La opción
> (B) elige la lectura benigna de una ambigüedad **exactamente en el caso donde algo anómalo está
> pasando con ese archivo**.
>
> Las tres condiciones, y sin ellas retiro la recomendación:
> 1. **motivo propio y distinto** (`BUILD_IDENTITY_UNREADABLE`, no el mismo que el desacuerdo) — si
>    no, dos causas distintas producen la misma línea y nadie puede diagnosticar;
> 2. **el panel dice qué hacer**, no sólo que no puede — el aviso tiene que aterrizar donde la
>    persona está parada, que es el botón de Arm;
> 3. **se mide la frecuencia antes de confiar**: hoy la evidencia es **6 lecturas exitosas, 0
>    fallos**, todas dentro de NT8 con el DLL cargado. **Seis no es una tasa.** Si la lectura falla
>    alguna vez en operación real, (A) pasa a ser un modo de caída y hay que revisarla.

---

## 6 · Arranque en frío

**Se comporta igual, y por un motivo que conviene decir**: los tres valores **no dependen del
`state.json`**. Dos son constantes estampadas en el binario y el tercero se mide del ensamblado
cargado. Un arranque en frío tiene los tres **antes de mirar el disco**.

⇒ el frío escribe `fresh:true` **más** los tres campos, y queda `DISARMED` como siempre. **La puerta
del armado es la que decide, y en frío nadie está armado todavía** — o sea que el freno cae
naturalmente en el único momento en que importa.

---

## 7 · Lo que este freno NO hace — dicho antes de que alguien firme con él

1. **No detecta que el addon sea viejo si el Core no cambió.** Si se despliega sólo un cambio de
   addon y el Core queda igual, `coreBuild == coreExpected` y el freno **no dice nada**. Cubre la
   ventana de dos etapas **porque en ella el Core sí cambia**, que es el caso medido — no todas las
   ventanas concebibles.
2. **El primer despliegue después de que esto exista NO puede detectar su propia ventana**, porque el
   addon viejo —el que corre en esos segundos— **no tiene el chequeo**. Funciona **del segundo
   despliegue en adelante**. Es una limitación real y no se arregla con código.
3. **No dice que el código fuente sea el mismo**, sólo que el binario lo es —
   `IssuerIdentity.BuildHashOf` ya lo tiene escrito y aplica igual acá.

---

## 8 · Por qué este freno pasa los dos pasos de la auditoría, y con evidencia poco común

| paso | respuesta |
|---|---|
| **1 · ¿existe un input alcanzable que lo haga decir que no?** | **SÍ, y está DEMOSTRADO**: esta máquina produjo la condición el 2026-09-02 durante **77 segundos**, **sin que nadie la buscara** |
| **2 · ¿alguien cuenta con él?** | **sí, y estará justificado**: reemplaza una regla que hoy depende de que una persona se acuerde |

**Eso es más de lo que tiene casi todo el inventario**: la mayoría de las condiciones están
verificadas contra un test que las simula —*«una hipótesis en verde»*—, y ésta tiene **una ocurrencia
real, fechada y no buscada**. Es el caso raro en que el paso 1 se contesta con producción y no con
un doble.

---

## 9 · Mientras tanto — ya aplicado hoy

La regla de procedimiento **NUNCA ARMAR ENTRE ABRIR NT8 Y APRETAR F5** quedó puesta donde la persona
tropieza con ella:

- **última línea que imprime `nt/install.ps1`**, en amarillo, después de todo lo demás;
- **`docs/install.md`**, encabezando el paso 4 (el del F5).

**No en un documento de hallazgos**, que es donde no la lee nadie en el momento que importa.
