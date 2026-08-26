# La tercera relación: DESPLEGADO contra LO QUE SE PROBÓ

**Estado: evaluación pedida, con una contrapropuesta. Ni una línea de código.** 2026-08-26.

`install.ps1` hoy verifica dos relaciones y las dos son reales:

1. **build contra fuente** — el `.cs` no es más nuevo que el `.dll` (`exit 5`).
2. **desplegado contra build** — los diez archivos coinciden byte a byte (`exit 6`).

Falta la tercera, y es la única que importa cuando lo que se produce es **evidencia**:
**¿el binario que corrió es el que se verificó?**

Hoy nada la cubre, y el 26-ago se materializó: `bin\Release` divergió a `57d36a5d6a8a9113` mientras lo
desplegado seguía en `4d20652361c0b468`. Correr `install.ps1` habría desplegado el primero **pasando
las dos guardas** — build corriente ✓, copia verificada ✓ — y la prueba viva habría medido otro código
creyendo que medía éste.

## La idea del `.deploy-pin`, evaluada

Un archivo con el hash esperado; mientras exista, el instalador se niega a desplegar otra cosa.

**Lo que tiene bien:** es una negativa **antes de mutar**, igual que las otras tres guardas, y su
existencia es toda su señal — no hay estado que mantener.

**Tres objeciones, en orden de peso:**

1. **Protege una dirección que no era el riesgo.** El peligro del 26-ago no era que el instalador
   desplegara de más: era **creer** que lo cargado era X cuando era Y. Con el pin puesto, si yo no
   corría `install.ps1` —que es lo que pasó— el pin no habría hecho nada, y el error habría sido
   idéntico. Cubre el caso en que alguien actúa, no el caso en que alguien supone.

2. **Un pin viejo es peor que ningún pin.** Si la prueba termina y nadie lo borra, todo despliegue
   futuro se rechaza, y el arreglo es *"borrá el archivo"*. En cuanto borrar el pin es rutina, el pin
   dejó de proteger — la misma erosión que una guarda que se dispara contra sus propios artefactos.
   Los rechazos que sobreviven son los que **nunca** tienen como respuesta correcta "desactivalo".

3. **Depende de que alguien se acuerde de crearlo.** Un plan ejecutado a mano —como el de esta
   noche— no crea archivos solo. Una protección que empieza con "acordate de" tiene el mismo modo de
   falla que verificar sha256 a mano, que es justamente lo que veníamos eliminando.

## Contrapropuesta, y creo que es más simple

**Que el ledger registre el hash del binario en `GUARDIAN_STARTED`.**

Verificado: hoy ese evento lleva sólo `state` (`Guardian.cs:154,183`). **El registro continuo no dice
qué binario lo produjo.** La identidad ya existe y ya se calcula —`IssuerIdentity.BuildHashOf` sobre
los bytes del ensamblado cargado— pero sólo aparece en el **certificado**, que es una foto de un día;
el ledger, que es el registro continuo, no la tiene.

Con esa línea:

- **La pregunta se contesta desde la evidencia, después del hecho, para siempre.** *"¿Qué código
  produjo este tramo?"* deja de depender de que alguien lo haya anotado.
- **No requiere que nadie se acuerde de nada.** Se escribe en cada arranque, salga bien o mal.
- **El chequeo previo sale gratis**: leer el último `GUARDIAN_STARTED` y comparar. Eso es lo que
  hubiera atrapado el caso del 26-ago, porque no depende de desplegar.
- **Es aditivo y no crea vocabulario peligroso.** No es "esto no cuenta" (prohibido por la decisión
  del 26-ago): es un dato más sobre lo que pasó, del lado bueno de esa línea.
- Cierra el hueco que el propio instalador nombra: *"la línea del Log de NT8 dice que algo cargó, no
  **cuál**."* Bueno — que lo diga el ledger.

**Costo honesto:** es un cambio de formato, y el formato es la evidencia. Un verificador viejo tiene
que ignorar el campo nuevo con elegancia, y eso hay que confirmarlo con Ventana B antes de escribirlo.

## Recomendación

**La línea en el ledger primero.** Cierra la relación que falta y no depende de la disciplina de
nadie. Después, si se quiere además una negativa antes de mutar durante una ventana de prueba, el pin
puede vivir encima — pero con el ledger anotando el binario, sospecho que sobra.

Y la relación queda cubierta en los dos tiempos que importan: **antes** (leer el ledger y comparar,
sin desplegar nada) y **después** (la evidencia dice qué código la produjo, sin que nadie lo recuerde).

---

## Apéndice: ¿`limitRespected` entra en la tanda de cert-1?

**No. Son ortogonales, y conviene que estén separados.**

- **cert-1 es de ALCANCE**: qué entradas se cuentan. El arreglo es mecánico —acotar al `dayKey` con la
  maquinaria que `Recompute` ya expone— y no cambia el significado de ninguna palabra.
- **`limitRespected` es de SEMÁNTICA**: qué quiere decir la palabra. Con alcance por día, el
  certificado del 27 va a decir que el trader **no respetó** su límite el día en que el guardián lo
  frenó **exactamente** en su límite. Eso no lo causa el arreglo de alcance: **lo hace visible**.

Arreglarlo cambia el vocabulario del certificado, y el vocabulario es contrato con el verificador de
Ventana B — que además ya está reemplazando `limitRespected` por un `outcome` con
`incompleteReason` al lado. Meter las dos cosas en un diff sería una decisión de producto escondida
adentro de un arreglo mecánico.

**Lo que sí aporta la tanda de cert-1:** el caso concreto. Después de esta noche existe un día real
en el que el guardián funcionó perfecto y el certificado dirá que el límite no se respetó. Ese día es
el argumento que hasta ahora era teórico — y el mejor insumo posible para la conversación con
Ventana B.
