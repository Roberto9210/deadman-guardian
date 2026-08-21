# Certificado de Sesión Verificable — statement de conformidad

**17 de 18.**

No 18. La que falta se nombra abajo con su motivo, porque un statement que redondea hacia arriba
no es un statement, es publicidad. Estado al 2026-08-21.

---

## Cómo leer esto

Hay **dos suites independientes**, escritas en ese orden y a propósito:

| | dónde | qué prueba | conteo |
|---|---|---|---|
| **el juez** | `deadman` (público) `tests/test_c_certificate.py` | que un certificado mentiroso no sobrevive | 42 tests |
| **el acusado** | `deadman-guardian` (privado) `tests/GuardianCore.Tests/C_CertificateEmitterTests.cs` | que el emisor cuenta igual que el juez y no inventa | 22 tests |

El juez se escribió **antes** que el emisor. Si el orden hubiera sido el inverso, el verificador
habría terminado diseñado para aprobar lo que el emisor produce.

Y hay una prueba que ninguna suite puede darse a sí misma, hecha punta a punta:
**el emisor en C# produjo el certificado real del remojo y el verificador en Python lo aceptó**
(`certHash ef199bcc…`, 75 entradas, cadena OK, `VERIFIED at L1`, exit 0). Dos lenguajes, dos
repositorios, dos autores del código en momentos distintos, un solo documento.

---

## Las 18

| # | Garantía | Probada por |
|---|---|---|
| C1 | Certificado sobre ledger íntegro verifica y reporta la capa correcta | `test_c1_honest_certificate_verifies_and_reports_the_layer`, `test_c1_both_dialects_verify`, `test_c1_real_soak_ledger`, `test_c1_a_ledger_that_kept_growing_after_emission_still_verifies` · C# `C1_a_quiet_armed_day_produces_a_complete_certificate`, `C1_certhash_covers_the_document_without_certhash_and_signature`, `C1_the_json_is_canonical_and_reparses_identically` |
| C2 | Claim falsificado es detectado por recálculo | `test_c2_falsified_limit_respected_is_caught_by_recompute`, `test_c2_orders_rejected_distinct_by_order_id`, + control `test_c2_the_honest_version_of_the_same_day_passes` |
| C3 | Ledger editado rompe la verificación e identifica el seq | `test_c3_edited_ledger_breaks_verification_and_names_the_seq`, `test_c3_a_removed_line_is_caught_as_an_incomplete_range` |
| C4 | Sin ancla ⇒ declara L1 y lo dice | `test_c4_without_an_anchor_it_declares_l1_and_says_why`, `test_c4_declaring_l2_without_anchors_is_a_contradiction`, `test_c4_the_report_states_the_reached_level_not_the_declared_one` |
| C5 | Con ancla, una reescritura completa cae | `test_c5_full_rewrite_with_recompute_passes_the_chain_alone` (el límite documentado), `test_c5_the_laundered_day_falls_against_an_anchor_taken_before_the_rewrite`, `test_c5_the_same_rewrite_falls_against_a_third_party_anchor`, `test_c5_anchor_coverage_stops_where_the_anchor_stops` |
| C6 | Episodios fail-closed con sus causas, incluido el disparador | `test_c6_fail_closed_episodes_are_episodes_not_causes`, `test_c6_hiding_an_episode_is_a_contradiction`, `test_c6_an_episode_with_nothing_before_it_has_no_trigger`, `test_c6_a_boundary_marker_is_never_counted_as_a_trigger` · C# `C6_a_fail_closed_episode_includes_its_trigger`, `C6_an_open_episode_falsifies_limit_respected_and_writes_no_closing_time` |
| C7 | Ningún campo se rellena por defecto | `test_c7_an_absent_claim_is_declared_never_invented`, `test_c7_tradesobserved_is_reported_as_not_recomputable` · C# `C7_no_alias_is_refused_rather_than_invented`, `C7_without_a_seal_there_is_nothing_to_certify`, `C7_no_daykey_is_refused_rather_than_read_off_the_clock`, `C7_previous_cert_hash_is_null_on_the_first_day_never_fabricated` |
| C8 | El HTML deriva del JSON y no agrega afirmaciones | C# `C8_the_html_adds_no_value_that_is_not_in_the_json`, `C8_the_html_contains_no_verdict_words_of_its_own`, `C8_the_render_is_a_pure_function_of_the_document`, `C8_html_escapes_a_hostile_alias` |
| C9 | Sin datos personales ni operaciones individuales | `test_c9_individual_trade_data_in_the_document_is_refused` · C# `C9_account_names_are_hashed_and_no_individual_trade_appears` |
| C10 | Las limitaciones aparecen textualmente | `test_c10_the_limitations_must_appear_verbatim`, `test_c10_dropping_the_limitations_entirely_is_refused` · C# `C10_the_limitations_are_carried_verbatim`, `C10_every_limitation_names_something_the_certificate_does_not_prove` |
| C11 | Un día sin operaciones produce un certificado completo | `test_c11_a_day_with_no_trades_is_a_complete_certificate` — **y el certificado real del remojo es exactamente ese caso** |
| C12 | La cadena entre certificados detecta un día omitido | `test_c12_a_day_removed_from_the_middle_breaks_the_series` |
| C13 | Los huecos se declaran con motivo, nunca se omiten | `test_c13_an_undeclared_gap_is_caught`, `test_c13_a_declared_gap_with_a_reason_is_accepted`, `test_c13_a_gap_without_a_reason_is_refused` |
| C14 | Los intentos rechazados de aflojar el límite aparecen | `test_c14_rejected_attempts_to_loosen_the_limit_appear_in_the_claims` · C# `C14_rejected_attempts_to_loosen_the_limit_reach_the_certificate` |
| **C15** | **Firma** | **parcial — ver abajo** |
| C16 | No existe emisión automática ni ruta de red | C# `C16_the_emitter_has_no_route_to_send_anything`, `C16_nothing_in_the_guardian_calls_the_emitter_on_its_own` |
| C17 | Dialecto declarado que no coincide ⇒ falla cerrado | `test_c17_a_crossed_dialect_fails_closed`, `test_c17_the_other_crossing_too`, `test_c17_an_undeclared_dialect_is_unevaluable_not_guessed` |
| C18 | Códigos de salida distinguen contradicción de no-verificable | `test_c18_exit_codes_separate_a_lie_from_an_unreadable_input`, `test_c18_the_cli_returns_those_codes`, `test_c18_the_cli_output_is_ascii_safe` |

---

## C15 — la que falta, y por qué no la doy por buena

La spec la enuncia dos veces y **las dos redacciones no dicen lo mismo**:

- **§7**: «La firma del emisor verifica con la clave pública publicada, **y** una firma válida sobre
  claims falsos igual es detectada por recálculo».
- **A.5**: «Firma válida sobre claims falsos igual cae por recálculo».

**La segunda mitad está probada** y es la que importa contra un mentiroso:
`test_c15_a_valid_signature_over_false_claims_still_falls_to_recompute` firma un certificado con
una clave Ed25519 real, el verificador confirma que la firma es válida, **y aun así lo contradice**
por recálculo. Más `test_c15_without_the_extra_the_signature_degrades_it_never_validates` (sin el
extra degrada a NOT_VERIFIED, jamás a «válida») y `test_c15_l3_is_never_reached_without_l2`.

**La primera mitad no existe todavía.** Verificado, no supuesto: `Certificate.Issue` acepta un
parámetro `signer` que **nadie alimenta**, el exportador no pasa ninguno, y **no hay ninguna clave
pública publicada** en ningún repo (`find` sobre ambos: cero `.pem`). Bajo la redacción de A.5 esto
contaría como cumplido; bajo la de §7, no. **Tomo la lectura estricta**: la capacidad no está, y
contarla porque una de las dos redacciones la omite sería exactamente el redondeo que este statement
existe para no hacer.

**Y no la implemento por mi cuenta, porque hay una decisión de diseño real detrás:** ¿dónde vive la
clave privada? Si vive en la máquina del trader, la firma prueba que *ese trader* emitió el
documento, no que lo emitió *este software* — y entonces L3 no significa lo que §3b promete. Si vive
con nosotros, el certificado deja de generarse sin conexión y §3c se cae. Además publicar una clave
pública es un acto de publicación, y nada se publica sin decisión de Roberto. Las tres cosas son
suyas, no mías.

Nota técnica para cuando se decida: .NET 8 no trae Ed25519 en `System.Security.Cryptography`, así
que firmar del lado del emisor exige una dependencia externa (NSec o BouncyCastle) — y GuardianCore
hoy no tiene ninguna. Del lado del verificador ya está resuelto y es opcional
(`pip install deadman-kit[verify-sig]`), sin tocar el cero-dependencias del paquete base.

---

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

---

## Reproducirlo

```
cd deadman        && python -m pytest tests/test_c_certificate.py -q     # 42
cd deadman-guardian && dotnet test                                        # 159 (22 del emisor)
```

Y el certificado real, contradecible por cualquiera con el paquete público:

```
python -m deadman.verify_certificate certificate-2026-08-21.json ledger.jsonl
```
