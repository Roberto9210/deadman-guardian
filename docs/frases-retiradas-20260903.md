# Frases retiradas: el test que impide que una retractación vuelva

**2026-09-03.** Documento del test `tests/GuardianCore.Tests/C_RetractedPhraseTests.cs`.
Documento datado: se anota, no se reescribe.

## 1. El caso que lo originó

El 2026-09-02 el sitio público dejó de decir *"a hand-written number can only ever go up"* —
literalmente falsa: **nada obliga a un número escrito a mano a bajar**, que es el defecto real. La
misma frase falsa siguió parada en `README.md:13` **hasta el 2026-09-03**.

Nadie fue descuidado. **La corrección viajó a la copia pública y no volvió a la fuente**, y no
existía ningún mecanismo capaz de notarlo. La retractación es el único tipo de edición donde **el
texto viejo se conoce EXACTAMENTE**, y por eso es el único que una máquina puede sostener.

## 2. El problema difícil, y cómo se resuelve

Una frase retirada **puede aparecer legítimamente dentro de una retractación**. Un test que hay que
apagar la primera vez que molesta no es un test. La solución son **dos decisiones mecánicas**,
ninguna de ellas una conjetura sobre el contexto:

**(a) Se prohíbe la FORMA ASERTIVA, no el tema.** `cancelled on sight` nombra un mecanismo y aparece
legítimamente **siete veces** en este repo — en pasado, entre comillas, negada. `is cancelled on
sight` / `are cancelled on sight` es **la afirmación hecha**, y hoy toda ocurrencia suya es falsa.
El estrechamiento hace el trabajo que haría una heurística de contexto, y lo hace **en la cadena**,
donde no se discute.

**(b) Se aplica sólo a documentos VIVOS.** Un registro datado **está para contener lo que
decíamos** — eso es lo que lo hace un registro, y corregirlo sería falsificar el log. Congelado se
decide por el nombre del archivo (`*-YYYYMMDD.*`) más cuatro rutas nombradas. Es **UNA frontera
compartida por todas las frases, nunca una excepción por frase.**

**La regla aplicada antes de escribirlo — ¿hay un cambio más barato que el arreglo real que ponga el
rojo en verde?** Dentro de un archivo vivo: **no**. No hay escape por comillas, ni por «solíamos
decir», ni lista de líneas permitidas. Hay que cambiar la oración. El único truco disponible es
**declarar registro congelado a un archivo vivo**, que es una línea en el test afirmando que un
README es un documento histórico: visible en el diff e imposible de escribir con cara seria.

## 3. Por qué `26 of 26` NO está en la lista

Porque `C_ConformanceCountTests` **ya mide esa propiedad directamente**: compara el número publicado
contra la tabla del SPEC y se pone rojo si discrepan (demostrado el 2026-09-03 rompiéndolo a
propósito: `25 of 26` → `26 of 26` ⇒ `[FAIL]`). Una prohibición de cadena sería **un proxy de una
propiedad que ya tiene test real**, y además dispararía sobre el comentario de cabecera de ese mismo
test. **Donde existe un test real, la prohibición de cadena es el instrumento débil y no se agrega.**

## 4. La lista, con su motivo y su fecha

| forma prohibida | por qué | retirada |
|---|---|---|
| `a hand-written number can only ever go up`, `can only ever go up` | literalmente falsa; nada obliga a un número escrito a mano a **bajar** | 2026-09-02 sitio / 2026-09-03 acá |
| `is cancelled on sight`, `are cancelled on sight`, `cancels every new order`, `every new order is cancelled` | `a916bba` retiró cancel-on-observation; G8 NO IMPLEMENTADA (A12). El pasado y la mención entre comillas siguen siendo legítimos | 2026-08-27 |
| `137 tests`, `137 collected test cases` | un conteo de tests sube solo, no dice nada de cobertura y vuelve a estar mal a la semana | 2026-09-01 |
| `it blocks new entries`, `blocks new entries until`, `and blocks new entries` | vocabulario de intención afirmando un efecto que no hay mecanismo para producir: 2.912 tipos barridos, **no hay pre-submit hook** | 2026-09-03 |
| `twelve of the trader's own exits`, `twelve of the trader's exits` | medido: 12 rechazos = **4 orderIds**, dos de ellos `SellShort` y `Buy`, que ABREN; el evento no lleva cantidad ni posición | 2026-09-02 |

## 5. Los controles, porque un barrido que no barre nada está verde

- `C_The_sweep_actually_reaches_the_files_it_claims_to_scan` — exige ≥80 archivos vivos, la presencia
  de `README.md` y `SPEC.md`, al menos un archivo bajo `src/`, `docs/`, `nt/` y `tests/`, y que la
  frontera de congelados **realmente excluya** `AMENDMENTS.md` y `docs/site-corrections-20260901.md`.
- `C_The_matcher_fires_on_a_sentence_that_asserts_a_retracted_claim` — control positivo: cada forma
  debe detectarse en una línea que la contenga **en mayúsculas**, y cada entrada debe traer su motivo.

## 5b. EL PATRÓN DEL DÍA — **el defecto no está en el código, está en el VERBO**

**Hoy cayeron cuatro afirmaciones de estado, todas de la misma forma:**

| lo que dábamos por cierto | lo que la medición dijo |
|---|---|
| **está bloqueado** | nada se cancela mientras está `LOCKED`; las 168 peticiones informan `count: 0` |
| **está cerrado** (`G8`) | seguía viva en la fuente **dos veces**: `SPEC.md:536` y `docs/configure.md:93` |
| **está pusheado** | `origin/main` llevaba **13 días** parado; el push fueron **132 commits**, no 4 |
| **está desplegado** | el binario que corre es el del **1-sep**, no el del 2-sep: dos commits de fuente quedaron afuera |

**Ninguna de las cuatro es un defecto de código.** Las cuatro son un **verbo en presente** que nadie
midió: *está*. Y las cuatro se sostuvieron por el mismo motivo — **a alguien le constaba**, y a nadie
le constaba con un instrumento.

> **CADA «ESTÁ» DE ESTE PROYECTO ES UNA AFIRMACIÓN QUE HAY QUE MEDIR.**
> (Regla del operador, 2026-09-03.)

Emparenta directo con la regla 19 —*nadie escribe un test sobre una frase*— y le agrega el
**tiempo verbal** como la señal a buscar. Una frase que describe un mecanismo se puede leer y
discutir; **una que afirma un estado presente sólo se puede medir, y caduca sin avisar.** El `26 of
26` era exactamente eso: un estado, escrito una vez, cierto durante seis días menos uno.

## 6. Lo que NO mide

Sostiene frases **ya cazadas**. No dice nada de la próxima frase falsa, que estará redactada con
palabras que nadie prohibió todavía. **Es un trinquete, no un guardián.**

## 7. ALCANCE — el hueco que queda abierto, y es a mano

**Este test barre ESTE repositorio.** El sitio público (`deadman-guardian-site`) es **otro repo, en
otro remoto**, y las frases retiradas viven ahí también — de hecho **ahí se corrigieron primero**.

Ningún test de acá puede alcanzarlo sin medir **la máquina donde corre** en vez de la propiedad: una
ruta hermana `../deadman-guardian-site` pasa en esta máquina y es vacía en un clon o en CI. Eso es
peor que no tener test, porque queda verde.

**Las tres formas que sí funcionarían, sin recomendación:**
1. **Submódulo git** del sitio dentro de este repo — el test lo alcanza de verdad, y el costo es que
   el sitio pasa a versionarse dos veces.
2. **La lista como archivo de datos** (p. ej. `docs/retracted-phrases.json`) publicado en un lugar
   que los dos repos puedan leer, y **una copia del test en el repo del sitio**. Dos tests, una lista.
3. **Comprobación a mano**, que es lo que hay hoy.

**Escrito porque acabamos de descubrir que la sincronía entre las dos copias no la comprueba nadie**,
y el caso de §1 es la evidencia: la corrección viajó en una sola dirección durante un día entero.

## 8. Lo que el rojo encontró el día que se escribió

Predicho: `README.md:13`. **Encontrados además dos que nadie sabía que estaban**, los dos afirmando
en presente un mecanismo retirado hace siete días:

- **`SPEC.md:536`** — paso 5 «Keep enforcing»: *"While `LOCKED`, every new order on a guarded account
  **is cancelled on sight** and logged `ORDER_REJECTED_LOCKED`"*. Es la especificación normativa
  afirmando la conducta que `a916bba` retiró. Documento **vivo** ⇒ se corrige, y el patrón está a
  seis líneas: la nota de `SPEC.md:530` («anotación, no reescritura», 2026-09-02).
- **`docs/configure.md:93`** — la guía de configuración **de cara al usuario**, tabla de estados:
  *"**LOCKED** (red) | … new orders **are cancelled on sight**"*. Es lo que lee quien instala.

**Ninguno de los dos es una copia del sitio**: son la fuente. El sitio se corrigió el 2026-09-01 y
**estos dos siguieron afirmándolo dos días más** — la misma dirección de fuga que §1, medida por
segunda vez en el mismo día y sobre otra frase.

> **Una familia no está cerrada porque un documento fechado lo diga, si la afirmación vive en varios
> lugares. Se cierra cuando algo mecánico comprueba que todos coinciden. `G8` se dio por cerrada dos
> veces y las dos veces seguía viva en la fuente.** (Regla del operador, 2026-09-03.)

**Resueltos el mismo día, cada uno con su tratamiento:** `SPEC.md:536` **anotado, no reescrito** —
paso 5 marcado `NOT IMPLEMENTED since 2026-08-27` con nota fechada al patrón de `SPEC.md:530`, y la
cláusula afirmativa **no restituida ni entre comillas**, porque repetirla volvería a poner la oración
retirada en un documento vivo. `docs/configure.md:93` **corregido directo**: es documento vivo de
cara al usuario, y además decía *"Everything was closed"*, que es la promesa que el sitio ya había
retirado — el registro es **un flatten verificado en toda la vida del producto**.
