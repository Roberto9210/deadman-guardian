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

> ### Hay DOS comparaciones, y cada valor estampado tiene la suya
>
> | comparación | qué detecta |
> |---|---|
> | **`coreBuild` vs `coreExpected`** | NT8 carga el Core **nuevo** contra el addon **viejo**, estampado con el hash del Core viejo ⇒ **la ventana de las dos etapas** |
> | **`addonBuild` vs `addonOnDisk`** | los `.cs` desplegados en `bin\Custom\AddOns` son **más nuevos** que los que este addon compiló ⇒ **hubo `install.ps1` y no hubo F5** |
>
> **Las dos son la misma pregunta**: *¿se instaló algo que todavía no se compiló?* Una la contesta
> por el lado del Core y la otra por el lado del addon, **y las dos tienen el mismo remedio de diez
> segundos: apretar F5.**

> ### `addonBuild` TIENE UN TRABAJO — corregido tras el fallo del operador, 2026-09-02
>
> **La primera versión de este diseño lo calculaba, lo estampaba, lo imprimía y NO LO MIRABA NADIE.**
> Eso es la familia que venimos persiguiendo toda la semana: **un campo impreso que nadie comprueba,
> adentro de un artefacto que declara verificar cosas — y que hereda la autoridad del que sí se
> comprueba.** Un valor estampado que nadie compara es **decoración, y la decoración en un freno es
> lo que hace que alguien cuente con él.**
>
> **Se elige darle trabajo, no quitarlo**, y el trabajo es exactamente el hueco que este mismo
> documento se había escrito: *«el freno no dice nada si cambia sólo el addon»*.
>
> **Cómo, sin circularidad**: `install.ps1` calcula el hash sobre **los 5 `.cs` gestionados** y lo
> escribe en un **sexto archivo generado** (`BuildStamp.cs`), que **queda fuera del hash**. En
> runtime el addon hashea esos mismos 5 archivos **desde `bin\Custom\AddOns`** y compara. Si
> difieren, los fuentes desplegados no son los que produjeron el binario que está corriendo.
>
> ⇒ **Ningún valor estampado queda sin comprobar, y el hueco declarado en §7 desaparece en vez de
> quedar declarado.**

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

**En los tres resultados posibles**, dos cosas no cambian nunca: **el guardián no se detiene** —sigue
ticando, sigue viendo la cuenta— y **no se deja de registrar**.

| resultado | armado | qué se registra |
|---|---|---|
| **coinciden** | permitido | nada especial |
| **DESACUERDO** medido | **REHUSADO** | el desacuerdo, con los dos hashes; el panel dice **«apretá F5»** |
| **NO SE PUDO MEDIR** | **PERMITIDO** | **evento propio, motivo propio**: el chequeo **no corrió**. El panel lo dice y **el certificado del día marca la sesión** |

**Fail-closed sobre la acción que importa y nada más** — y la acción que importa es la que **tiene
remedio**. Un desacuerdo de build no es motivo para dejar a alguien sin guardián; es motivo para no
dejarlo **empezar** con un emparejamiento que ningún test cubre, **cuando arreglarlo cuesta diez
segundos**.

---

## 5 · Si el addon NO PUEDE leer los bytes del Core — **hay bifurcación y traigo recomendación**

`ReadCoreAssemblyBytes()` devuelve `null` ante `IOException` o `UnauthorizedAccessException`.
Entonces `coreBuild` no existe y **no hay comparación posible**.

| opción | qué hace | costo |
|---|---|---|
| **(A) rehusar el armado también acá** | trata *«no pude comprobar»* distinto de *«coincide»* | **crea una forma nueva de que el guardián sea inusable** |
| **(B) permitir el armado** | *«no pude comprobar»* no es evidencia de desacuerdo | **el freno se apaga solo justo cuando algo raro pasa con el archivo** |

> ### Lo que yo había recomendado — **(A)**, y quedó escrito porque el motivo del fallo se pierde si se borra**
>
> *«Desde afuera, "no puedo leer el DLL" y "alguien cambió el DLL" son indistinguibles. La opción (B)
> elige la lectura benigna de una ambigüedad exactamente en el caso donde algo anómalo está pasando
> con ese archivo.»* Con tres condiciones: motivo propio y distinto, el panel dice qué hacer, y **se
> mide la frecuencia antes de confiar, porque seis no es una tasa**.

> ### ⚖ FALLO DEL OPERADOR, 2026-09-02: **(B). NO SE REHÚSA.** Y la premisa que falla es mía
>
> **La observación de la ambigüedad es correcta. Lo que estaba mal es a QUÉ le fallaba cerrado.**
>
> > **REHUSAR EL ARMADO ES FAIL-CLOSED RESPECTO DE LA CORRECCIÓN DEL GUARDIÁN Y FAIL-OPEN RESPECTO DE
> > LA EXPOSICIÓN DEL TRADER. Sin armar no hay protección.**
>
> Una persona parada frente a un mercado abierto, que no puede armar y no entiende por qué, **no deja
> de operar: opera sin guardián.** Las dos direcciones apuntan al revés y mi diseño las trataba como
> una sola.
>
> **Y el discriminador entre los dos casos no es la gravedad: es SI EXISTE REMEDIO EJECUTABLE.**
>
> | caso | qué se hace | por qué |
> |---|---|---|
> | **desacuerdo** (`coreBuild ≠ coreExpected`) | **se rehúsa el armado** | **el remedio existe, dura diez segundos y se puede nombrar en el panel: apretá F5.** Cuesta diez segundos, no una sesión sin protección |
> | **no se puede leer el DLL** | **NO se rehúsa: SE ARMA** | ahí **puede no haber ningún remedio que la persona pueda ejecutar** — si el antivirus tiene el archivo tomado, *«apretá F5»* no sirve de nada. Rehusar deja a alguien **sin camino hacia la protección** |
>
> **Lo que se hace en lugar de rehusar**: se arma **y se declara que el chequeo NO CORRIÓ.**
> - **evento propio con motivo propio** en el ledger;
> - **el panel lo dice**;
> - **el certificado del día marca la sesión** como una en la que la identidad de build **no pudo
>   verificarse**.
>
> > **Declarar que una comprobación no corrió no es lo mismo que decir que pasó — y es exactamente lo
> > contrario de rehusar, que convierte «no sé» en «no».**
>
> **Y el argumento que decide era mi propia tercera condición, dada vuelta:**
>
> > **NO SE PUEDE FALLAR CERRADO SOBRE UNA CONDICIÓN CUYA FRECUENCIA NO SE MIDIÓ, cuando el costo de
> > un falso positivo es que alguien opere sin freno.**
>
> Yo la había puesto como *condición para confiar en (A)*. Es la razón por la que (A) no va.
>
> *(El operador cita que «la otra ventana ya lo tiene resuelto en su escalón 4». **Referencia cruzada
> NO VERIFICADA desde este repositorio** — se anota como venía, sin adivinar a qué apunta.)*

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

1. ~~**No detecta que el addon sea viejo si el Core no cambió.**~~ **CERRADO por el fallo del
   2026-09-02**: era el hueco que dejaba a `addonBuild` sin trabajo, y ahora es su trabajo — la
   comparación `addonBuild` vs `addonOnDisk` cubre exactamente ese caso. **Se tacha en vez de
   borrarse, porque el hueco es el motivo por el que la segunda comparación existe.**
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
