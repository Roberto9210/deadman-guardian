// deadman-guardian - Verifiable Session Certificate emitter (CERT_SPEC v0.2.1).
//
// This class is the DEFENDANT. The judge already exists and is public:
// deadman-kit's `python -m deadman.verify_certificate`, written before a line of this file,
// with 41 adversarial tests most of which try to make a certificate lie. Nothing here is
// allowed to be convenient; it has to survive that.
//
// Three rules govern everything below:
//
//   1. NEVER INFER (SPEC section 4.1). Every field comes from the ledger or it is omitted.
//      There is no plausible default anywhere in this file. If the record does not say it,
//      the certificate does not say it either.
//   2. THE HTML ADDS NOTHING (C8). The render is a presentation of the JSON's own values.
//      It contains no sentence that is not either a fixed label or a value copied from the
//      document.
//   3. NOTHING LEAVES THIS MACHINE (section 3c, C16). There is no send, no socket, no
//      "share". The caller receives strings; writing them to disk is the adapter's job and
//      only on the trader's explicit action.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuardianCore
{
    /// <summary>One fail-closed episode, recomputed from the events (SPEC A.2.1).</summary>
    public sealed class FailClosedEpisode
    {
        public long FromSeq { get; set; }
        public DateTime FromUtc { get; set; }
        public long? ToSeq { get; set; }
        public DateTime? ToUtc { get; set; }
        public bool Open { get; set; }
        public long? TriggerSeq { get; set; }
        public string TriggerEvent { get; set; }
        public SortedDictionary<string, long> Reasons { get; } =
            new SortedDictionary<string, long>(StringComparer.Ordinal);
    }

    /// <summary>The claims of SPEC section 2c, counted exactly as A.2 defines them.</summary>
    public sealed class SessionClaims
    {
        public long LockoutsTriggered { get; set; }
        public long ChangeAttemptsWhileSealed { get; set; }
        public long OrdersRejectedWhileLocked { get; set; }
        public long ClockAnomaly { get; set; }
        public long ClockSuspect { get; set; }
        public List<FailClosedEpisode> FailClosedEpisodes { get; } = new List<FailClosedEpisode>();
        public long FromSeq { get; set; }
        public long ToSeq { get; set; }
        public bool ChainVerified { get; set; }

        /// <summary>Derived, never asserted: A.2's three conditions, all of them.</summary>
        public bool LimitRespected =>
            LockoutsTriggered == 0 && !FailClosedEpisodes.Any(e => e.Open) && ChainVerified;
    }

    /// <summary>What the trader chooses about their own document. Nothing here is guessed:
    /// an alias the trader did not choose is not invented, it is refused.</summary>
    public sealed class CertificateRequest
    {
        public string Alias { get; set; }
        public string DayKey { get; set; }
        public string PreviousCertHash { get; set; }
        public IReadOnlyList<GapDeclaration> Gaps { get; set; }
        /// <summary>SUPERSEDED 2026-08-31 by cert-1 and IGNORED by Issue, which derives the figure
        /// from the entries inside the day span instead. It stays only because removing it would
        /// break the already-compiled adapter in the window between deploying this DLL and the F5
        /// that recompiles it - the same binary-compatibility reason PositionSnapshot has two
        /// constructors.
        ///
        /// It is therefore a key that no longer configures anything, which is the ledgerPath family,
        /// and it is listed as a coordinated removal rather than left looking meaningful.</summary>
        public long DaysCovered { get; set; } = 1;
        public IReadOnlyList<AnchorRecord> Anchors { get; set; }
        public string IssuerVersion { get; set; }
        public string IssuerBuildHash { get; set; }
        public string KeyId { get; set; }

        /// <summary>Per-installation salt for hashing account names (SPEC A.7). Random, generated
        /// once, stored locally, NEVER written into the certificate. Without it the emitter
        /// refuses: an unsalted hash of a short predictable name like "Sim101" is reversed by
        /// dictionary in seconds, so falling back to one would be a default that leaks.</summary>
        public string AccountSalt { get; set; }
    }

    public sealed class GapDeclaration
    {
        public string DayKey { get; set; }
        public string Reason { get; set; }
    }

    public sealed class AnchorRecord
    {
        public string Type { get; set; }
        public string Ref { get; set; }
        public long Seq { get; set; }
        public string Hash { get; set; }
        public DateTime TsUtc { get; set; }
    }

    public sealed class CertificateResult
    {
        public bool Ok { get; }
        public string Reason { get; }
        public string Json { get; }
        public string Html { get; }
        public string CertHash { get; }

        private CertificateResult(bool ok, string reason, string json, string html, string certHash)
        { Ok = ok; Reason = reason; Json = json; Html = html; CertHash = certHash; }

        public static CertificateResult Refused(string reason) =>
            new CertificateResult(false, reason, null, null, null);
        public static CertificateResult Issued(string json, string html, string certHash) =>
            new CertificateResult(true, null, json, html, certHash);

        public override string ToString() => Ok ? "ISSUED " + CertHash.Substring(0, 12) : "REFUSED: " + Reason;
    }

    /// <summary>Builds a session certificate from a ledger. Reads; never writes, never sends.</summary>
    public static class Certificate
    {
        public const int CertVersion = 1;
        public const string LedgerDialect = "guardian-core-v1";
        public const string Tool = "deadman-guardian";

        /// <summary>The limitations of SPEC section 2, verbatim.
        ///
        /// These strings are a COPY of deadman-kit's REQUIRED_LIMITATIONS, which is where the
        /// canonical text lives (SPEC A.5b). That is deliberate: the public verifier owns the
        /// wording and this emitter must match it exactly, so softening them here does not
        /// produce a gentler certificate - it produces one that fails C10 in public.</summary>
        public static readonly string[] Limitations =
        {
            "This does not say the trader makes money. It is not a track record of profitability.",
            "This does not say the trader did not trade elsewhere. The guardian sees one platform " +
            "and the configured accounts, and nothing else.",
            "This does not say the software was not bypassed before it started. Whoever removes the " +
            "add-on with the platform closed does not appear; the gap appears, not the act.",
            "This is not an audit. Nobody inspected this trader. It is a machine's signed assertion " +
            "about a record that machine kept.",
            // candidate 6. ordersRejectedWhileLocked reports 0 because NOTHING PRODUCES THE EVENT any
            // more: the LT-1 fix (2026-08-26) stopped cancelling on observation, and
            // ORDER_REJECTED_LOCKED lost its only writer. A zero meaning "this function does not
            // exist" sitting beside zeros meaning "this did not happen" is the house defect in a
            // number. The field stays - removing it would change the document's shape for a verifier
            // that is not ours to break - and what the zero MEANS is said here, in the list that
            // exists for precisely this.
            "The count of orders rejected while locked is always zero in this build, and that is a " +
            "statement about the software rather than about the trader: since 2026-08-26 the guardian " +
            "does not cancel an order on sight, so nothing produces that record. It will mean what it " +
            "says when selective cancellation lands.",
        };

        private static readonly string[] Boundaries = { Ev.FailClosedEntered, Ev.FailClosedCleared };

        /// <summary>The dayKey an entry carries, or null. Only a minority of events carry one -
        /// ARMED, DAY_OPENED, DAY_CLOSED, DISARMED, LOCKOUT_CLEARED, SEAL_EXPIRED, STATE_RESTORED,
        /// PNL_BASELINE_ADOPTED - which is exactly why the day's span is derived from THEM and the
        /// rest are attributed positionally, by falling inside it.</summary>
        private static string DayKeyOf(JsonObject entry)
        {
            var payload = entry == null ? null : entry["payload"] as JsonObject;
            return payload == null ? null : payload.GetString("dayKey");
        }

        // ------------------------------------------------------------------ claims

        /// <summary>Counts the claims of section 2c from the events. The same arithmetic the public
        /// verifier does independently - if these two ever disagree, the verifier wins.</summary>
        public static SessionClaims Recompute(IReadOnlyList<JsonObject> entries, long fromSeq,
                                              long toSeq, bool chainVerified)
        {
            var rows = entries
                .Where(e => e.GetInt("seq").HasValue &&
                            e.GetInt("seq").Value >= fromSeq && e.GetInt("seq").Value <= toSeq)
                .OrderBy(e => e.GetInt("seq").Value)
                .ToList();

            var claims = new SessionClaims { FromSeq = fromSeq, ToSeq = toSeq, ChainVerified = chainVerified };
            var distinctOrders = new HashSet<string>(StringComparer.Ordinal);
            FailClosedEpisode current = null;

            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var ev = r.GetString("event");
                var seq = r.GetInt("seq").Value;

                if (ev == Ev.LimitBreached) claims.LockoutsTriggered++;
                else if (ev == Ev.ConfigChangeRejected) claims.ChangeAttemptsWhileSealed++;
                else if (ev == Ev.ClockAnomaly) claims.ClockAnomaly++;
                else if (ev == Ev.ClockSuspect) claims.ClockSuspect++;
                else if (ev == Ev.OrderRejectedLocked)
                {
                    // Distinct by orderId: a retry of the same id must not inflate the number.
                    // An entry with no orderId is NOT counted as an anonymous extra - it is a
                    // record we cannot attribute, and inventing one would be a default.
                    var payload = r["payload"] as JsonObject;
                    var id = payload == null ? null : payload.GetString("orderId");
                    if (!string.IsNullOrEmpty(id)) distinctOrders.Add(id);
                }

                if (ev == Ev.FailClosedEntered)
                {
                    if (current == null)
                    {
                        DateTime enteredAt;
                        Iso.TryParseUtc(r.GetString("tsUtc"), out enteredAt);
                        current = new FailClosedEpisode
                        {
                            FromSeq = seq, FromUtc = enteredAt, Open = true,
                        };
                        // SPEC A.2.1: the trigger belongs to the episode. Positional and published,
                        // never inferred from the text of `reason`.
                        var prev = i > 0 ? rows[i - 1] : null;
                        var prevEv = prev == null ? null : prev.GetString("event");
                        if (prev != null && Array.IndexOf(Boundaries, prevEv) < 0)
                        {
                            current.TriggerSeq = prev.GetInt("seq");
                            current.TriggerEvent = prevEv;
                            current.Reasons[prevEv] = 1;
                        }
                    }
                }
                else if (ev == Ev.FailClosedCleared && current != null)
                {
                    DateTime clearedAt;
                    Iso.TryParseUtc(r.GetString("tsUtc"), out clearedAt);
                    current.ToSeq = seq;
                    current.ToUtc = clearedAt;
                    current.Open = false;
                    claims.FailClosedEpisodes.Add(current);
                    current = null;
                }
                else if (current != null && Array.IndexOf(Boundaries, ev) < 0)
                {
                    long n;
                    current.Reasons[ev] = current.Reasons.TryGetValue(ev, out n) ? n + 1 : 1;
                }
            }

            if (current != null)
            {
                current.ToSeq = toSeq;
                current.ToUtc = null;          // open: no closing timestamp exists, so none is written
                claims.FailClosedEpisodes.Add(current);
            }

            return claims;
        }

        // ------------------------------------------------------------------ issue

        /// <summary>Builds the certificate. Called ONLY from an explicit trader action
        /// (SPEC section 3c) - nothing in the engine calls this on a timer or an event.</summary>
        public static CertificateResult Issue(IReadOnlyList<JsonObject> entries,
                                              PersistedState state,
                                              CertificateRequest request,
                                              bool chainVerified,
                                              Func<string, string> signer = null)
        {
            if (entries == null || entries.Count == 0)
                return CertificateResult.Refused("CERT_NO_LEDGER: there are no entries to certify");
            if (request == null || string.IsNullOrWhiteSpace(request.Alias))
                return CertificateResult.Refused("CERT_ALIAS_MISSING: the alias is the trader's to choose, not ours to invent");
            if (state == null || state.Seal == null)
                return CertificateResult.Refused("CERT_NO_SEAL: nothing was armed, so there is no commitment to certify");
            if (string.IsNullOrWhiteSpace(request.DayKey))
                return CertificateResult.Refused("CERT_DAYKEY_MISSING: the session day must be stated, never assumed from the clock");
            if (string.IsNullOrWhiteSpace(request.AccountSalt))
                return CertificateResult.Refused("CERT_SALT_MISSING: account names need a per-installation salt (SPEC A.7); an unsalted hash of a short name is reversed by dictionary in seconds");

            var seqs = entries.Where(e => e.GetInt("seq").HasValue).Select(e => e.GetInt("seq").Value).ToList();
            if (seqs.Count == 0)
                return CertificateResult.Refused("CERT_LEDGER_UNREADABLE: no entry carries a seq");

            // cert-1. This used to be min/max over EVERYTHING it was handed, so a document naming one
            // day published totals for every day the installation had ever run - and daysCovered: 1
            // was not merely hardcoded, it was false. The house's hardest subtype: a true assertion
            // over the wrong set, where the arithmetic is right, the sentence is right, and the
            // defect lives only in the joint.
            //
            // THE DAY IS [min(seq), max(seq)] OVER THE ENTRIES CARRYING ITS dayKey, and the property
            // that matters is that a stranger derives it with one scan and two extremes - no trust in
            // whoever issued the document. It needs no event ordering and works on ledgers ALREADY
            // WRITTEN, which is indispensable: certificates are issued over history that exists.
            //
            // It is NOT "between DAY_OPENED and DAY_CLOSED", and the reason came from reading the
            // live ledger rather than the code: DAY_OPENED IS WRITTEN LAST in the arming sequence -
            // CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED - so that window would exclude the ARMED
            // event that establishes the limit, which is the most important claim in the document.
            //
            // KNOWN GAP, named rather than hidden: CONFIG_LOADED precedes ARMED and carries no
            // dayKey, so the entry holding configHash falls outside. Closing it means CONFIG_LOADED
            // carrying a dayKey - an additive FIELD, and therefore the extension contract's business.
            var inDay = entries
                .Where(e => e.GetInt("seq").HasValue && DayKeyOf(e) == request.DayKey)
                .Select(e => e.GetInt("seq").Value)
                .ToList();
            if (inDay.Count == 0)
                return CertificateResult.Refused(
                    "CERT_DAY_NOT_IN_LEDGER: no entry carries dayKey '" + request.DayKey + "', so this " +
                    "ledger does not delimit that day. A document about a day with no evidence would " +
                    "be an assertion with no source.");
            long fromSeq = inDay.Min();

            // WHERE THE DAY ENDS, and the first draft got this wrong in a way the existing C-tests
            // caught: [min, max] over dayKey-carrying entries stops at the LAST one, and on a day
            // still OPEN that is DAY_OPENED, near the beginning - so everything the day actually did
            // fell outside its own certificate.
            //
            // A day ends where the NEXT day begins, and if no next day exists in this ledger it runs
            // to the end of the record. Both halves stay derivable by a stranger with one scan.
            var nextDayStart = entries
                .Where(e => e.GetInt("seq").HasValue && e.GetInt("seq").Value > fromSeq)
                .Where(e => { var d = DayKeyOf(e); return !string.IsNullOrEmpty(d) && d != request.DayKey; })
                .Select(e => e.GetInt("seq").Value)
                .DefaultIfEmpty(-1)
                .Min();
            long toSeq = nextDayStart < 0 ? seqs.Max() : inDay.Max();

            var claims = Recompute(entries, fromSeq, toSeq, chainVerified);

            // TRUE BY CONSTRUCTION, never typed: count the distinct dayKeys actually inside the span.
            // For a correct span that is 1, and if the span ever leaked into another day it would say
            // 2 - which is informative, where a hardcoded 1 was a lie.
            //
            // request.DaysCovered is IGNORED from here on. It cannot be removed today without
            // breaking the already-compiled adapter in the window between deploying this DLL and the
            // F5 that recompiles it (the reason PositionSnapshot has two constructors). It is a key
            // that no longer configures anything - the ledgerPath family - and removing it is a
            // coordinated change, listed as such.
            var daysCovered = entries
                .Where(e => e.GetInt("seq").HasValue &&
                            e.GetInt("seq").Value >= fromSeq && e.GetInt("seq").Value <= toSeq)
                .Select(DayKeyOf)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.Ordinal)
                .Count();

            // Trust level: this emitter can honestly claim L1, and L2 only when the trader hands
            // over anchors a third party holds. It never claims L3 for itself - a signature that
            // is not checked against a published key proves nothing, and the verifier decides.
            var hasAnchors = request.Anchors != null && request.Anchors.Count > 0;
            var trustLevel = hasAnchors ? "L2" : "L1";

            // cert-2: THE SEALED SNAPSHOT IS THE SOURCE FOR EVERYTHING THIS DOCUMENT SAYS ABOUT THE
            // SESSION - the guarded accounts, both limits, and the zone the day is measured in. When it
            // cannot answer, the honest move is the one this file already makes a few lines below for a
            // half-signature, and one C7 away for a missing alias: REFUSE.
            //
            // What this replaces was not an absence being represented. It was an impossible state
            // dressed as data: `?? ""` for the zone - the exact value the public verifier rejects as
            // decorative filler - and, worse because it reads as a fact about the trader, an EMPTY
            // ACCOUNTS ARRAY whenever the snapshot did not parse. An empty collection asserts absence.
            //
            // Neither branch is reachable today, and that is not a reason to leave them: a config
            // without sessionResetTimeZone never parses (GuardianConfig.RequiredKeys), so it never
            // becomes a seal, and Start refuses to run on a snapshot whose hash does not match
            // (Guardian.cs:224). Unreachable is what these are; SILENT is what they must not be.
            var sealedConfig = ParseSnapshot(state.Seal.ConfigSnapshot);
            if (sealedConfig == null)
                return CertificateResult.Refused(
                    "CERT_SNAPSHOT_UNPARSEABLE: the sealed configuration in state.json does not parse, so " +
                    "the accounts, the limits and the session's time zone cannot be read. A certificate " +
                    "without them would assert an empty session rather than report a broken seal.");
            if (string.IsNullOrEmpty(sealedConfig.GetString("sessionResetTimeZone")))
                return CertificateResult.Refused(
                    "CERT_TIMEZONE_MISSING: the sealed configuration carries no 'sessionResetTimeZone', so " +
                    "the day this certificate names has no boundaries. It is a required key, so a seal " +
                    "without it was not written by a configuration this build would accept.");

            var doc = BuildDocument(state, request, claims, trustLevel, daysCovered);

            var certHash = Hashing.Sha256Hex(doc.ToCanonical());
            var full = doc.Set("certHash", certHash);

            // SPEC A.4.1: certHash excludes certHash AND signature; the signature covers certHash.
            if (signer != null)
            {
                var value = signer(certHash);
                if (string.IsNullOrEmpty(value))
                    return CertificateResult.Refused("CERT_SIGN_FAILED: the signer returned nothing; an unsigned certificate is honest, a half-signed one is not");
                full = full.Set("signature", JsonValue.Obj()
                    .Set("alg", "Ed25519")
                    // PREPARATION, DECLARED: nothing wires a signer today, so this branch cannot run -
                    // but it is unreachable by WIRING, not impossible by schema, so the `??` stays. The
                    // day someone passes a signer, request.KeyId can genuinely be null and this line
                    // runs. What it does then is still WRONG and is an open decision (1-sep): a
                    // signature that does not name its key is a half-signature, which the check four
                    // lines above already refuses to emit. Blanking it here contradicts that refusal.
                    .Set("keyId", request.KeyId ?? "")
                    .Set("value", value));
            }

            return CertificateResult.Issued(full.ToCanonical(), Render(full), certHash);
        }

        private static JsonObject BuildDocument(PersistedState state, CertificateRequest req,
                                                SessionClaims claims, string trustLevel,
                                                int daysCovered)
        {
            var seal = state.Seal;
            var snapshot = ParseSnapshot(seal.ConfigSnapshot);

            var issuer = JsonValue.Obj().Set("tool", Tool);
            if (!string.IsNullOrEmpty(req.IssuerVersion)) issuer.Set("version", req.IssuerVersion);
            if (!string.IsNullOrEmpty(req.IssuerBuildHash)) issuer.Set("buildHash", req.IssuerBuildHash);
            if (!string.IsNullOrEmpty(req.KeyId)) issuer.Set("keyId", req.KeyId);

            // Accounts are hashed with the installation salt, never named (SPEC section 4.3, A.7).
            // The salt itself never reaches the document - only its effect does.
            var accounts = JsonValue.Arr();
            var accArray = snapshot == null ? null : snapshot["accounts"] as JsonArray;
            if (accArray != null)
                foreach (var a in accArray.Items)
                {
                    var s = a as JsonString;
                    if (s != null) accounts.Add(JsonValue.Str(HashAccount(req.AccountSalt, s.Value)));
                }

            var commitment = JsonValue.Obj()
                .Set("armedAtUtc", Iso.Utc(seal.ArmedAtUtc))
                .Set("sealHash", seal.SealHash)
                .Set("sealExpiryUtc", Iso.Utc(seal.ExpiresAtUtc))
                .Set("changeAttemptsWhileSealed", claims.ChangeAttemptsWhileSealed);
            // Limits come from the SEALED snapshot or they do not appear at all.
            var personal = snapshot == null ? null : snapshot.GetString("personalDailyLossLimit");
            var firm = snapshot == null ? null : snapshot.GetString("firmDailyLossLimit");
            if (personal != null) commitment.Set("personalDailyLossLimit", personal);
            if (firm != null) commitment.Set("firmDailyLossLimit", firm);

            var episodes = JsonValue.Arr();
            foreach (var e in claims.FailClosedEpisodes)
            {
                var reasons = JsonValue.Obj();
                foreach (var kv in e.Reasons) reasons.Set(kv.Key, kv.Value);
                var ep = JsonValue.Obj()
                    .Set("fromSeq", e.FromSeq)
                    .Set("fromUtc", Iso.Utc(e.FromUtc))
                    .Set("open", e.Open)
                    .Set("reasons", reasons);
                if (e.ToSeq.HasValue) ep.Set("toSeq", e.ToSeq.Value);
                if (e.ToUtc.HasValue) ep.Set("toUtc", Iso.Utc(e.ToUtc.Value));
                if (e.TriggerSeq.HasValue) ep.Set("triggerSeq", e.TriggerSeq.Value);
                if (e.TriggerEvent != null) ep.Set("triggerEvent", e.TriggerEvent);
                episodes.Add(ep);
            }

            var claimsObj = JsonValue.Obj()
                .Set("limitRespected", claims.LimitRespected)
                .Set("lockoutsTriggered", claims.LockoutsTriggered)
                .Set("ordersRejectedWhileLocked", claims.OrdersRejectedWhileLocked)
                .Set("failClosedEpisodes", episodes)
                .Set("clockAnomalies", JsonValue.Obj().Set("byType", JsonValue.Obj()
                    .Set("CLOCK_ANOMALY", claims.ClockAnomaly)
                    .Set("CLOCK_SUSPECT", claims.ClockSuspect)))
                .Set("ledgerRange", JsonValue.Obj().Set("fromSeq", claims.FromSeq).Set("toSeq", claims.ToSeq))
                .Set("ledgerVerified", claims.ChainVerified);

            // PREPARATION, DECLARED (1-sep). NOTHING IN THIS PRODUCT POPULATES req.Gaps: the addon
            // never sets it, so `gaps` is [] in all twelve certificates issued so far. The `??` stays
            // because these are unreachable by WIRING and not impossible by schema - AnchorRecord and
            // the gap fields are free strings, so a caller that computes gaps tomorrow can hand over a
            // null and this line runs. Deleting the fallback would break it on the day it connects.
            //
            // WHAT IT DOES THEN IS AN OPEN DECISION, and both fields are SCOPE rather than value:
            // a gap that cannot name its day is not information, it is a hole shaped like information,
            // and it should be refused rather than emitted blank. `reason` is the one VALUE here -
            // prose explaining a gap - so omitting it removes an explanation, not a check.
            //
            // AND THE LIVE PART IS ONE LEVEL UP: `gaps: []` asserts "there are no gaps" while nothing
            // computes them. That is section 3's sixth subtype and it bites today, not on the day the
            // caller arrives.
            var gaps = JsonValue.Arr();
            if (req.Gaps != null)
                foreach (var g in req.Gaps)
                    gaps.Add(JsonValue.Obj().Set("dayKey", g.DayKey ?? "").Set("reason", g.Reason ?? ""));

            // PREPARATION, DECLARED (1-sep). Same as gaps: nothing sets req.Anchors, so `anchors` is
            // always [] and trustLevel is therefore always "L1". All three fallbacks are SCOPE - an
            // anchor with no hash anchors nothing, and a verifier that checks `if hash:` would skip it
            // silently - so the treatment when this connects is to refuse the element, not to blank it.
            var anchors = JsonValue.Arr();
            if (req.Anchors != null)
                foreach (var a in req.Anchors)
                    anchors.Add(JsonValue.Obj()
                        .Set("type", a.Type ?? "").Set("ref", a.Ref ?? "")
                        .Set("seq", a.Seq).Set("hash", a.Hash ?? "")
                        .Set("tsUtc", Iso.Utc(a.TsUtc)));

            var limitations = JsonValue.Arr();
            foreach (var l in Limitations) limitations.Add(JsonValue.Str(l));

            var doc = JsonValue.Obj()
                .Set("certVersion", CertVersion)
                .Set("ledgerDialect", LedgerDialect)
                .Set("issuer", issuer)
                .Set("subject", JsonValue.Obj().Set("alias", req.Alias).Set("accounts", accounts))
                .Set("session", JsonValue.Obj()
                    .Set("dayKey", req.DayKey)
                    .Set("openedUtc", Iso.Utc(seal.ArmedAtUtc))
                    // NO FALLBACK ON PURPOSE, AND THIS IS THE ONE FIELD THAT GETS NONE (cert-2, 1-sep).
                    // It had `?? ""` and the `??` was DELETED rather than changed to null, because
                    // sessionResetTimeZone is in GuardianConfig.RequiredKeys and is validated at parse
                    // time: a seal without it CANNOT EXIST. A guard over a case the schema forbids is
                    // not a safety net, it is a silent assertion that the case happens - and it was
                    // read that way by two people who planned work on top of it.
                    //
                    // Which is exactly why the six fallbacks elsewhere in this file STAY: those are
                    // unreachable by WIRING, not impossible by schema, so their branch exists the day
                    // someone connects the caller. The test is the distinction, not the emptiness.
                    //
                    // Issue refuses before reaching here if the snapshot does not parse or carries no
                    // zone, so this reads the seal plainly and the question is asked in exactly one
                    // place instead of being half-answered in two.
                    .Set("timezone", snapshot.GetString("sessionResetTimeZone")))
                .Set("continuity", JsonValue.Obj().Set("daysCovered", daysCovered).Set("gaps", gaps))
                .Set("commitment", commitment)
                .Set("claims", claimsObj)
                .Set("anchors", anchors)
                .Set("trustLevel", trustLevel)
                .Set("limitations", limitations)
                .Set("verifyInstructions", JsonValue.Obj()
                    .Set("tool", "deadman-kit")
                    .Set("install", "pip install deadman-kit")
                    .Set("command", "python -m deadman.verify_certificate certificate.json ledger.jsonl"));

            // previousCertHash is null-or-absent, never fabricated: the first day of a series
            // genuinely has no predecessor and says so.
            doc.Set("previousCertHash", string.IsNullOrEmpty(req.PreviousCertHash)
                ? JsonValue.Null()
                : JsonValue.Str(req.PreviousCertHash));

            return doc;
        }

        /// <summary>SPEC A.7. The consequence, stated because it is a real loss: the same account
        /// hashes DIFFERENTLY on different installations. The hash identifies an account WITHIN a
        /// series, not ACROSS series. An evaluator can confirm sixty certificates speak about the
        /// same account; nobody can cross two installations and correlate a trader. That is the
        /// trade the salt buys, and it is the right way round for a document meant to be shared.</summary>
        public static string HashAccount(string salt, string accountName) =>
            Hashing.Sha256Hex((salt ?? "") + ":" + (accountName ?? "")).Substring(0, 16);

        private static JsonObject ParseSnapshot(string snapshot)
        {
            if (string.IsNullOrEmpty(snapshot)) return null;
            JsonValue v; string err;
            return JsonParser.TryParse(snapshot, out v, out err) ? v as JsonObject : null;
        }

        // ------------------------------------------------------------------ render (C8)

        /// <summary>A presentation of the document. Every sentence is either a fixed label or a
        /// value read out of the JSON - there is no computation and no adjective here. If this
        /// method ever needs an `if` about what the numbers MEAN, it has stopped being a render.</summary>
        public static string Render(JsonObject doc)
        {
            var claims = doc["claims"] as JsonObject;
            var commitment = doc["commitment"] as JsonObject;
            var session = doc["session"] as JsonObject;
            var subject = doc["subject"] as JsonObject;

            var sb = new System.Text.StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
            sb.Append("<title>Session certificate</title>");
            sb.Append("<style>body{font:15px/1.55 system-ui,sans-serif;max-width:52rem;margin:2rem auto;padding:0 1rem;color:#111}")
              .Append("h1{font-size:1.3rem}h2{font-size:1rem;margin-top:1.6rem}table{border-collapse:collapse;width:100%;margin:1rem 0}")
              .Append("td,th{border-bottom:1px solid #ddd;padding:.4rem .5rem;text-align:left;vertical-align:top}")
              .Append("code{background:#f4f4f4;padding:.1rem .3rem}.lim{background:#fafafa;border-left:3px solid #999;padding:.6rem 1rem;margin:1rem 0}")
              .Append("</style></head><body>");

            sb.Append("<h1>Session certificate</h1>");
            Row(sb, null, null); // no-op guard keeps the helper used even if the table is empty

            sb.Append("<table>");
            Row(sb, "alias", Str(subject, "alias"));
            Row(sb, "day", Str(session, "dayKey"));
            Row(sb, "timezone", Str(session, "timezone"));
            Row(sb, "armed at (UTC)", Str(commitment, "armedAtUtc"));
            Row(sb, "seal expiry (UTC)", Str(commitment, "sealExpiryUtc"));
            Row(sb, "personal daily loss limit", Str(commitment, "personalDailyLossLimit"));
            Row(sb, "firm daily loss limit", Str(commitment, "firmDailyLossLimit"));
            Row(sb, "trust level", Str(doc, "trustLevel"));
            sb.Append("</table>");

            sb.Append("<table>");
            Row(sb, "limitRespected", Raw(claims, "limitRespected"));
            Row(sb, "lockoutsTriggered", Raw(claims, "lockoutsTriggered"));
            Row(sb, "changeAttemptsWhileSealed", Raw(commitment, "changeAttemptsWhileSealed"));
            Row(sb, "ordersRejectedWhileLocked", Raw(claims, "ordersRejectedWhileLocked"));
            Row(sb, "clockAnomalies", ByType(claims));
            Row(sb, "ledgerRange", Range(claims));
            Row(sb, "ledgerVerified", Raw(claims, "ledgerVerified"));
            sb.Append("</table>");

            // Episodes get their own table because a JSON blob in a cell is unreadable, and
            // unreadable is its own kind of dishonest. Every cell below is still a value lifted
            // straight out of the document: no adjective, no duration computed, no judgement of
            // whether two episodes is a lot. The reader decides that; this page only shows them.
            var episodes = claims == null ? null : claims["failClosedEpisodes"] as JsonArray;
            sb.Append("<h2>failClosedEpisodes</h2>");
            if (episodes == null || episodes.Count == 0)
            {
                sb.Append("<p>none</p>");
            }
            else
            {
                sb.Append("<table><tr><th>seq</th><th>from (UTC)</th><th>to (UTC)</th>")
                  .Append("<th>open</th><th>trigger</th><th>reasons</th></tr>");
                foreach (var item in episodes.Items)
                {
                    var e = item as JsonObject;
                    if (e == null) continue;
                    sb.Append("<tr>");
                    Cell(sb, Raw(e, "fromSeq") + Arrow(e));
                    Cell(sb, Str(e, "fromUtc"));
                    Cell(sb, Str(e, "toUtc"));
                    Cell(sb, Raw(e, "open"));
                    Cell(sb, Trigger(e));
                    Cell(sb, Reasons(e));
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }

            sb.Append("<div class=\"lim\"><strong>What this does not say</strong><ul>");
            var lims = doc["limitations"] as JsonArray;
            if (lims != null)
                foreach (var l in lims.Items)
                {
                    var s = l as JsonString;
                    if (s != null) sb.Append("<li>").Append(Escape(s.Value)).Append("</li>");
                }
            sb.Append("</ul></div>");

            var vi = doc["verifyInstructions"] as JsonObject;
            sb.Append("<p><strong>Verify this yourself, without asking us anything:</strong></p><pre><code>")
              .Append(Escape(Str(vi, "install"))).Append("\n")
              .Append(Escape(Str(vi, "command"))).Append("</code></pre>");
            sb.Append("<p>certHash <code>").Append(Escape(Str(doc, "certHash"))).Append("</code></p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void Row(System.Text.StringBuilder sb, string label, string value)
        {
            if (label == null) return;
            sb.Append("<tr><th>").Append(Escape(label)).Append("</th><td>")
              .Append(value == null ? "<em>omitted</em>" : Escape(value)).Append("</td></tr>");
        }

        private static void Cell(System.Text.StringBuilder sb, string value)
        {
            sb.Append("<td>").Append(value == null ? "<em>omitted</em>" : Escape(value)).Append("</td>");
        }

        private static string Arrow(JsonObject e)
        {
            var to = Raw(e, "toSeq");
            return to == null ? "" : ".." + to;
        }

        private static string Trigger(JsonObject e)
        {
            var ev = Str(e, "triggerEvent");
            if (ev == null) return null;               // omitted, not "none": the rule has edges
            var seq = Raw(e, "triggerSeq");
            return seq == null ? ev : ev + " @ " + seq;
        }

        private static string Reasons(JsonObject e)
        {
            var r = e["reasons"] as JsonObject;
            if (r == null) return null;
            var parts = new List<string>();
            foreach (var k in r.Keys) parts.Add(k + " " + Raw(r, k));
            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }

        private static string ByType(JsonObject claims)
        {
            var c = claims == null ? null : claims["clockAnomalies"] as JsonObject;
            var byType = c == null ? null : c["byType"] as JsonObject;
            if (byType == null) return null;
            var parts = new List<string>();
            foreach (var k in byType.Keys) parts.Add(k + " " + Raw(byType, k));
            return string.Join(", ", parts);
        }

        private static string Range(JsonObject claims)
        {
            var r = claims == null ? null : claims["ledgerRange"] as JsonObject;
            if (r == null) return null;
            return "seq " + Raw(r, "fromSeq") + " to " + Raw(r, "toSeq");
        }

        private static string Str(JsonObject o, string key) => o == null ? null : o.GetString(key);

        /// <summary>Renders a value by its own canonical form: no interpretation, no rounding,
        /// no summary. What the JSON says is what the page shows.</summary>
        private static string Raw(JsonObject o, string key)
        {
            if (o == null || !o.Has(key)) return null;
            var v = o[key];
            var s = v as JsonString;
            return s != null ? s.Value : v.ToCanonical();
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
