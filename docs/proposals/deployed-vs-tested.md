# La tercera relación: DESPLEGADO contra LO QUE SE PROBÓ

**Estado: DECIDIDO el 2026-08-26. El pin queda descartado; el hash en `GUARDIAN_STARTED` aprobado.**
Va **después** de la prueba viva y **después** de cert-1. Ni una línea de código todavía.

Roberto, al descartar el pin: *"yo protegí la puerta equivocada"*. La objeción decisiva fue la
primera — con el pin puesto y sin correr `install.ps1`, que es literalmente lo que pasó hoy, no
habría hecho nada. Y queda como regla de la casa: **un freno cuyo arreglo habitual es "desactivalo"
ya dejó de ser un freno.**

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

## La precisión del NOMBRE, antes de escribir el campo

`IssuerIdentity.BuildHashOf` hashea `typeof(Certificate).Assembly.Location`: **el ARCHIVO desde el que
se cargó el ensamblado**, no la imagen en memoria. En Windows, con NT8 manteniendo el archivo
bloqueado, son la misma cosa — y por eso la verificación del 26-ago fue válida. Pero **el campo va a
sobrevivir a esa circunstancia**, y el día que alguien cargue el ensamblado desde bytes, con
shadow-copy o desde otra ruta, dejarán de coincidir sin que el nombre lo avise.

**Se nombra y se documenta por lo que establece: el hash del archivo desde el que se cargó este
ensamblado.** No "el código que está corriendo". Sería irónico construir la clase de la casa adentro
del arreglo que la persigue — y ya pasó una vez hoy, con el `ROLLED BACK` que anunciaba una
restauración que no había ocurrido, atrapado por su propio test.

## LO QUE HAY QUE ACORDAR CON VENTANA B PRIMERO, y es más grande que este campo

**La pregunta NO es "¿este campo rompe el verificador?".** Es el **contrato de extensión del formato**,
una sola vez y para siempre:

> ¿El verificador tolera **campos desconocidos en un evento conocido**? ¿Y esa tolerancia está
> **escrita** en algún lado, o es un accidente de implementación que el próximo refactor puede borrar
> sin que nadie note que borró un contrato?

El ledger es el formato de la evidencia. Uno que no se puede extender sin romper su verificador tiene
dos futuros y los dos son malos: **se congela**, o **se rompe en silencio** cuando alguien lo extiende
igual. Y no es hipotético: el acotado por día de cert-1 va a pedir campos también.

**Entregable, antes de escribir una línea del campo:** la regla escrita — dónde vive, quién la
respeta, qué hace un verificador viejo frente a un campo nuevo. **Si Ventana B contesta que hoy no hay
tolerancia, eso es un hallazgo más importante que el campo**, y cambia el orden de todo lo que sigue.

*Estado del envío: al 26-ago 13:20 la única sesión par visible (`alaya-06`) no pudo identificarse como
Ventana B — la de `deadman` era `alaya-4e`. No se le escribió a una ventana sin identificar. La
pregunta no es urgente: el campo va tercero en el orden.*

**Costo honesto del campo en sí:** es un cambio de formato, y el formato es la evidencia.

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

**Lo que sí aporta la tanda de cert-1, y hay que anotarlo porque es el mejor argumento que
`limitRespected` va a tener:** después de esta noche **existe un día real en el que el guardián
funcionó perfecto y el certificado dice que el límite no se respetó**. Deja de ser teórico. Un día
concreto, con su `dayKey`, sus `seq` y su cadena, en el que la palabra publicada contradice lo que el
producto hizo bien. Ese es el insumo para la conversación con Ventana B, y vale más que cualquier
argumento de diseño.
