# Certificado de Sesión Verificable — statement de conformidad

**18 de 18 en alcance. 19 definidas. 1 declarada fuera de alcance (C15b), a la vista.**

El denominador no esconde nada: C15b existe, está numerada, y su exclusión se argumenta abajo en
vez de desaparecer del conteo. Estado al 2026-08-21, noche.

---

## Cómo leer esto

Hay **dos suites independientes**, escritas en ese orden y a propósito:

| | dónde | qué prueba | conteo |
|---|---|---|---|
| **el juez** | `deadman` (público) `tests/test_c_certificate.py` | que un certificado mentiroso no sobrevive | 42 tests |
| **el acusado** | `deadman-guardian` (público) `tests/GuardianCore.Tests/C_CertificateEmitterTests.cs` | que el emisor cuenta igual que el juez y no inventa | 28 tests |

El juez se escribió **antes** que el emisor. Si el orden hubiera sido el inverso, el verificador
habría terminado diseñado para aprobar lo que el emisor produce.

Y hay una prueba que ninguna suite puede darse a sí misma, hecha punta a punta:
**el emisor en C# produjo el certificado real del remojo y el verificador en Python lo aceptó**
(`certHash 19de6993…`, 78 entradas, cadena OK, `VERIFIED at L1`, exit 0). Dos lenguajes, dos
repositorios, escritos en ese orden para que el juez no pudiera moldearse alrededor del acusado.

*(El primer certificado emitido, `ef199bcc…`, queda superado: hasheaba la cuenta sin sal. Se
reemplazó por el salado tras la revisión de Roberto. Se deja dicho en vez de borrado.)*

---

## Las 19

| # | Garantía | Probada por |
|---|---|---|
| C1 | Certificado sobre ledger íntegro verifica y reporta la capa correcta | `test_c1_honest_certificate_verifies_and_reports_the_layer`, `test_c1_both_dialects_verify`, `test_c1_real_soak_ledger`, `test_c1_a_ledger_that_kept_growing_after_emission_still_verifies` · C# `C1_a_quiet_armed_day_produces_a_complete_certificate`, `C1_certhash_covers_the_document_without_certhash_and_signature`, `C1_the_json_is_canonical_and_reparses_identically` |
| C2 | Claim falsificado es detectado por recálculo | `test_c2_falsified_limit_respected_is_caught_by_recompute`, `test_c2_orders_rejected_distinct_by_order_id`, + control `test_c2_the_honest_version_of_the_same_day_passes` |
| C3 | Ledger editado rompe la verificación e identifica el seq | `test_c3_edited_ledger_breaks_verification_and_names_the_seq`, `test_c3_a_removed_line_is_caught_as_an_incomplete_range` |
| C4 | Sin ancla ⇒ declara L1 y lo dice | `test_c4_without_an_anchor_it_declares_l1_and_says_why`, `test_c4_declaring_l2_without_anchors_is_a_contradiction`, `test_c4_the_report_states_the_reached_level_not_the_declared_one` |
| C5 | Con ancla, una reescritura completa cae | `test_c5_full_rewrite_with_recompute_passes_the_chain_alone` (el límite documentado), `test_c5_the_laundered_day_falls_against_an_anchor_taken_before_the_rewrite`, `test_c5_the_same_rewrite_falls_against_a_third_party_anchor`, `test_c5_anchor_coverage_stops_where_the_anchor_stops` |
| C6 | Episodios fail-closed con sus causas, incluido el disparador | `test_c6_fail_closed_episodes_are_episodes_not_causes`, `test_c6_hiding_an_episode_is_a_contradiction`, `test_c6_an_episode_with_nothing_before_it_has_no_trigger`, `test_c6_a_boundary_marker_is_never_counted_as_a_trigger` · C# `C6_a_fail_closed_episode_includes_its_trigger`, `C6_an_open_episode_falsifies_limit_respected_and_writes_no_closing_time` |
| C7 | Ningún campo se rellena por defecto | `test_c7_an_absent_claim_is_declared_never_invented`, `test_c7_tradesobserved_is_reported_as_not_recomputable` · C# `C7_no_alias_is_refused_rather_than_invented`, `C7_without_a_seal_there_is_nothing_to_certify`, `C7_no_daykey_is_refused_rather_than_read_off_the_clock`, `C7_previous_cert_hash_is_null_on_the_first_day_never_fabricated` |
| C8 | El HTML deriva del JSON y no agrega afirmaciones | C# `C8_the_html_adds_no_value_that_is_not_in_the_json`, `C8_the_html_contains_no_verdict_words_of_its_own`, `C8_the_render_is_a_pure_function_of_the_document`, `C8_html_escapes_a_hostile_alias`, `C8_episodes_are_rendered_readably_not_as_a_json_blob`, `C8_a_day_with_no_episodes_says_none_rather_than_showing_an_empty_table` |
| C9 | Sin datos personales ni operaciones individuales, y hashes de cuenta **con sal por instalación** | `test_c9_individual_trade_data_in_the_document_is_refused` · C# `C9_account_names_are_hashed_and_no_individual_trade_appears`, `C9_the_salt_never_appears_in_the_certificate_in_any_form`, `C9_an_unsalted_hash_of_a_short_account_name_is_refused`, `C9_the_same_account_hashes_differently_under_different_salts`, `C9_the_salt_actually_changes_the_published_hash` |
| C10 | Las limitaciones aparecen textualmente | `test_c10_the_limitations_must_appear_verbatim`, `test_c10_dropping_the_limitations_entirely_is_refused` · C# `C10_the_limitations_are_carried_verbatim`, `C10_every_limitation_names_something_the_certificate_does_not_prove` |
| C11 | Un día sin operaciones produce un certificado completo | `test_c11_a_day_with_no_trades_is_a_complete_certificate` — **y el certificado real del remojo es exactamente ese caso** |
| C12 | La cadena entre certificados detecta un día omitido | `test_c12_a_day_removed_from_the_middle_breaks_the_series` |
| C13 | Los huecos se declaran con motivo, nunca se omiten | `test_c13_an_undeclared_gap_is_caught`, `test_c13_a_declared_gap_with_a_reason_is_accepted`, `test_c13_a_gap_without_a_reason_is_refused` |
| C14 | Los intentos rechazados de aflojar el límite aparecen | `test_c14_rejected_attempts_to_loosen_the_limit_appear_in_the_claims` · C# `C14_rejected_attempts_to_loosen_the_limit_reach_the_certificate` |
| C15a | Firma válida sobre claims falsos igual cae por recálculo | `test_c15_a_valid_signature_over_false_claims_still_falls_to_recompute` (clave Ed25519 real), `test_c15_without_the_extra_the_signature_degrades_it_never_validates`, `test_c15_l3_is_never_reached_without_l2` |
| ~~C15b~~ | ~~Verificación contra clave publicada por nosotros~~ | **FUERA DE ALCANCE en v1** — ver abajo |
| C16 | No existe emisión automática ni ruta de red | C# `C16_the_emitter_has_no_route_to_send_anything`, `C16_nothing_in_the_guardian_calls_the_emitter_on_its_own` |
| C17 | Dialecto declarado que no coincide ⇒ falla cerrado | `test_c17_a_crossed_dialect_fails_closed`, `test_c17_the_other_crossing_too`, `test_c17_an_undeclared_dialect_is_unevaluable_not_guessed` |
| C18 | Códigos de salida distinguen contradicción de no-verificable | `test_c18_exit_codes_separate_a_lie_from_an_unreadable_input`, `test_c18_the_cli_returns_those_codes`, `test_c18_the_cli_output_is_ascii_safe` |

---

## C15b — fuera de alcance, con el razonamiento a la vista

La spec enunciaba C15 dos veces con redacciones que se contradecían. Se partió en dos:

- **C15a — cumplida.** `test_c15_a_valid_signature_over_false_claims_still_falls_to_recompute`
  firma un certificado con una clave Ed25519 real, el verificador confirma que **la firma es
  válida**, y aun así **contradice el documento** por recálculo. Más: sin el extra criptográfico
  degrada a `NOT_VERIFIED`, jamás a «válida», y **L3 nunca se alcanza sin L2** — una firma no
  fecha nada.
- **C15b — fuera de alcance en v1.** Pediría verificar contra una clave publicada **por nosotros**,
  lo que sólo significa algo si la clave privada es **nuestra**.

**Decisión de Roberto, 2026-08-21: la clave privada vive en la máquina del trader.** Con eso, L3
prueba **origen del emisor local** —que este documento salió de esa instalación y nadie lo editó
después— y **NO autoría del software**. La §3b de la spec se reescribió para decir exactamente eso,
en vez de romper la arquitectura para sostener una frase más vendible.

Las tres razones, en orden de peso:

1. **Rompería §13 y §3c.** Una clave nuestra firmando en la máquina del trader significa o
   distribuir nuestra privada —que entonces no prueba nada— o pedir la firma a un servidor. Lo
   segundo obliga al guardián a abrir socket y mata la emisión sin conexión. Todo el producto
   descansa en no tener red.
2. **Compraría menos de lo que parece.** Aun con firma nuestra, la veracidad seguiría viniendo del
   recálculo: firmar un dato falso da una firma válida sobre un dato falso.
3. **Coste técnico real.** .NET 8 no trae Ed25519, así que el emisor necesitaría una dependencia
   externa en un core que hoy tiene **cero**.

Queda declarado, no prometido: si algún día existe un emisor alojado, donde la red ya está por otras
razones, C15b vuelve a la mesa.

## Lo que ninguna garantía cubre

Se repite acá porque un statement de conformidad es el lugar donde más tienta omitirlo:

- **L1 sola no ve una reescritura completa.** `test_c5_full_rewrite_with_recompute_passes_the_chain_alone`
  existe para probar que **pasa**, no que falla. El certificado real del remojo está en L1: hoy no
  hay ancla externa, y el verificador lo dice en su propia salida sin que nadie se lo pregunte.
- **El guardián ve una plataforma y las cuentas configuradas.** Un certificado limpio es compatible
  con un desastre en otra cuenta.
- **`tradesObserved` no es recalculable**: ningún evento del vocabulario registra un conteo de fills,
  así que no se afirma. El verificador lo reporta como no verificable en vez de inventarlo.
- **La serie vale por continuidad, no por un día.** El certificado real es el primero: `daysCovered: 1`,
  `previousCertHash: null`. Vale poco hoy y mucho en tres meses, y eso ya estaba escrito en §2b antes
  de tener uno.
- **El hash de cuenta identifica DENTRO de una serie, no ENTRE series.** La sal de A.7 es por
  instalación, así que la misma cuenta da hashes distintos en máquinas distintas. Un evaluador puede
  confirmar que sesenta certificados hablan de la misma cuenta; nadie puede cruzar dos instalaciones
  y correlacionar a un trader. Es una pérdida real y es deliberada.
- **L3 no dice «lo emitió deadman-guardian».** Ver C15b arriba. Cualquier página que lo insinúe está
  mintiendo.

## `issuer.buildHash` — definición exacta y cómo recomputarlo

Un campo que existe para ser contradicho tiene que ser recomputable por quien no nos cree, con una
herramienta que ya tiene. La definición es, entonces, la que un extraño busca primero:

```
buildHash = HEX(SHA-256(bytes_del_archivo))[0:16]      en minúsculas
```

- `bytes_del_archivo`: el `GuardianCore.dll` completo, byte por byte, **sin normalización de ningún
  tipo** — la cabecera PE, su timestamp y el MVID entran al hash junto con el código.
- `[0:16]`: los primeros 16 de los 64 caracteres hexadecimales.

Una línea, cualquiera de las dos:

```
sha256sum GuardianCore.dll | cut -c1-16
(Get-FileHash GuardianCore.dll -Algorithm SHA256).Hash.Substring(0,16).ToLower()
```

Verificado contra el emisor real el 21-ago-2026: el código devolvió `7cc9a56217c62681` y `sha256sum`
devolvió `7cc9a56217c62681`. La fórmula está fijada por
`C_IssuerIdentityTests.Build_hash_is_exactly_the_first_16_hex_of_sha256_over_the_bytes`, no sólo por
este párrafo: la documentación no rompe un build.

**Qué responde y qué no.** Responde *«¿es el mismo binario que me dieron?»*. **No** responde *«¿se
compiló del mismo código?»*: dos builds del mismo fuente normalmente difieren acá, porque el toolchain
escribe un MVID y un timestamp nuevos en la cabecera PE. Quien compare dos certificados aprende que los
binarios difieren; no aprende que difiera el código.

**Historial, porque explica la forma.** Hasta el 21-ago-2026 la implementación hasheaba el **texto
base64** de los bytes. Era determinista y era sensible — los tests de comportamiento pasaban todos —
pero producía un valor que nadie podía reproducir sin conocer la rareza: quien corriera el comando obvio
veía que no coincidía y tenía todos los motivos para concluir que el campo era mentira. **Una
verificación que da falsas alarmas es peor que una que nadie corre.** Se quitó el día que costaba menos:
sin base instalada, sin certificados de terceros en circulación.

---

## Reproducirlo

```
cd deadman          && python -m pytest tests/test_c_certificate.py -q    # 42
cd deadman-guardian && dotnet test                                        # 175 (29 del emisor)
```

Y el certificado real, contradecible por cualquiera con el paquete público:

```
python -m deadman.verify_certificate certificate-2026-08-21.json ledger.jsonl
```
