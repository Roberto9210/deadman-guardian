# Campos que piden prestada credibilidad — certificado, lado emisor

**2026-09-01. Sólo la lista, ningún arreglo** (pedido del operador).
Regla que la origina, medida en el verificador por Ventana B:

> ### UN CAMPO IMPRESO DENTRO DE UN VEREDICTO HEREDA LA AUTORIDAD DEL VEREDICTO.

Su caso: el verificador imprime `VALID (keyId=key-ALPHA)` cuando el documento **reclama** `key-ALPHA`
y firmó otra clave. El campo nunca se verifica; está ahí sólo para interpolarse en la línea del
veredicto, y el lector entiende *"lo firmó key-ALPHA y lo comprobé"*.

**La pregunta aplicada acá**: ¿qué campos NO verificados se renderizan junto a afirmaciones que sí lo
están? El HTML los pone **a todos en una sola tabla**, sin marca de procedencia, así que la
convivencia es total: cada fila hereda la credibilidad de las demás.

---

## 0 · La keystone, y es exactamente la forma de su hallazgo

> ### `ledgerVerified` NO ES UNA VERIFICACIÓN. Es un parámetro.

`Certificate.Issue(entries, state, request, **bool chainVerified**, signer)` — el emisor **no verifica
ninguna cadena**. Copia lo que le pasan a `claims.ledgerVerified`, y lo imprime **como última fila de
la tabla de números contados**.

- **Es la única fila cuyo nombre suena a comprobación**, y es la que el emisor no hizo.
- **`limitRespected` se calcula a partir de ella** (`Certificate.cs:56`), así que el veredicto más
  fuerte del documento hereda esa confianza sin comprobarla.
- **Hoy el valor es correcto** —el addon pasa `verify.Ok` de `VerifyLedger()` (`:413`)—, y por eso es
  peligroso: **la mitad medida es verdadera**, y lo que no aguanta es la estructura. Un llamador
  distinto, o el mismo con un bug, imprime `true` sin que nada lo note.

---

## 1 · Los cinco del SELLO — impresos sin comprobar el sello

`Issue` **nunca llama `SnapshotMatchesHash()`**. La única llamada en todo el repo está en
`Guardian.cs:223`, en el arranque. Un certificado emitido a las 16:55 **no re-verifica** que el
snapshot sellado siga correspondiendo a su hash.

| fila del HTML | de dónde sale |
|---|---|
| `timezone` | snapshot sellado |
| `armed at (UTC)` | `seal.ArmedAtUtc` |
| `seal expiry (UTC)` | `seal.ExpiresAtUtc` |
| **`personal daily loss limit`** | snapshot sellado |
| **`firm daily loss limit`** | snapshot sellado |

**Las dos últimas son la promesa entera del documento** — el número contra el que se juzga todo lo
demás — y se imprimen junto a `lockoutsTriggered` y `ledgerVerified` sin que el emisor haya
comprobado el sello del que salieron.

**Matiz que corresponde, y evita exagerar**: el guardián **sí** verifica el sello al arrancar y
**bloquea** si no coincide (`SEAL_MISMATCH` ⇒ lockout), así que un sello adulterado no llega lejos en
un proceso vivo. Lo que no existe es la comprobación **en el momento de emitir**, que es cuando el
documento se firma con esos números.

También sale de ahí, en el JSON y no en el HTML: `subject.accounts[]`, los hashes de las cuentas
guardadas.

## 2 · Los que el llamador provee y nadie contrasta

| campo | quién lo pone | comprobación |
|---|---|---|
| **`alias`** | el trader, en `alias.txt` | **ninguna** — sólo se rehúsa si falta (`C7`) |
| **`trust level`** | derivado de si el llamador pasó `anchors` | **ninguna**; hoy siempre `L1` porque nada llena `anchors` |
| `issuer.version` / `issuer.buildHash` | el llamador (JSON, no HTML) | el hash es una medición real **del archivo**, y está nombrado así a propósito |

`alias` encabeza la tabla. Es el nombre bajo el cual se lee todo lo demás, y es texto libre.

## 3 · Los que el emisor SÍ calcula — con la deuda de la keystone

`lockoutsTriggered`, `changeAttemptsWhileSealed`, `ordersRejectedWhileLocked`, `clockAnomalies`,
`ledgerRange`, `failClosedEpisodes`, `daysCovered`, `certHash`.

Son cómputos genuinos sobre las entradas recibidas — **pero sobre entradas cuya cadena el emisor no
verificó** (§0). Su aritmética es correcta; su autoridad se apoya en la fila que no comprobó nada.

## 4 · El contraejemplo, que es lo que hace creíble a esta lista

**`day` SÍ se verifica contra el ledger.** Desde cert-1, un día que el ledger no delimita se **rehúsa**
(`CERT_DAY_NOT_IN_LEDGER`). No todo lo que se imprime es palabra del llamador, y por eso la mezcla es
más engañosa: **hay filas comprobadas de verdad en la misma tabla.**

## 5 · Fuera de la tabla, y es de los dos lados

El HTML cierra con:

```
Verify this yourself, without asking us anything:
    pip install deadman-kit
    python -m deadman.verify_certificate certificate.json ledger.jsonl
```

Cadenas fijas (`Certificate.cs:542-545`). **No es una afirmación del emisor sobre los datos**, pero sí
una invitación a confiar en la herramienta del otro lado — **la misma cuya línea de veredicto acaba de
medirse imprimiendo un `keyId` que no comprueba**. Se anota como ítem de coordinación, no como defecto
de este repo.

---

## 6 · Lo que esta lista NO dice

1. **No dice que ningún valor sea falso hoy.** Dice **cuáles no fueron comprobados por quien los
   imprime**, que es una propiedad de la estructura y no del día.
2. **No propone arreglos**: ni marcar procedencia en el render, ni verificar el sello al emitir, ni
   cambiar la firma de `Issue`. Cualquiera de los tres cambia lo que un verificador ve.
3. **No cubre el verificador**, que es de Ventana B.
