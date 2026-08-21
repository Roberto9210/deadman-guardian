// deadman-guardian — one-shot latency probe.
//
// Measures the detect-and-cancel cycle of SPEC §9.5 by placing ONE resting limit order that cannot
// fill, watching it reach a working state, and cancelling it immediately.
//
// HARD LIMITS, enforced in code and not by intention:
//   * account "Sim101" ONLY, matched by exact ordinal name, and additionally PROVEN to be the
//     simulator (Provider == Simulator) before anything is sent. Any failed check aborts.
//   * ONE order, ever. A gate file must exist for the probe to run at all, and it is deleted before
//     the order is submitted, so a crash or a restart cannot produce a second one.
//   * LIMIT orders only. A market order is never constructed anywhere in this file.
//   * the limit price is far below the market so the order cannot fill.
//   * it never connects anything, never changes a setting, and never touches another account.
//
// To run: create the gate file, then restart NinjaTrader.
//     Documents\NinjaTrader 8\deadman-guardian-probe\latency_probe.GO
//
// Source of truth: <repo>/nt/probe/DeadmanGuardianLatencyProbe.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class DeadmanGuardianLatencyProbe : AddOnBase
    {
        private const string TargetAccount = "Sim101";
        private const int QuantityContracts = 1;

        private static readonly string OutDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "NinjaTrader 8", "deadman-guardian-probe");
        private static readonly string GatePath = Path.Combine(OutDir, "latency_probe.GO");
        private static readonly string ReportPath = Path.Combine(OutDir, "latency_report.md");

        private static readonly string[] InstrumentCandidates =
        {
            "MES 09-26", "MES 12-26", "MNQ 09-26", "ES 09-26", "NQ 09-26", "M2K 09-26"
        };

        private readonly List<string> _log = new List<string>();
        private readonly object _gate = new object();
        private Timer _timer;
        private Account _account;
        private Order _order;
        private bool _fired;              // one order, ever, within this process too
        private bool _cancelIssued;
        private bool _finished;

        private long _tSubmit, _tDetect, _tCancelIssued, _tCancelled;
        private DateTime _submitUtc;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "deadman-guardian latency probe (one-shot, Sim101 only)";
            }
            else if (State == State.Configure)
            {
                if (!File.Exists(GatePath)) return;         // not armed: do absolutely nothing
                Note("gate file present; latency probe armed");
                // Let connections settle. NT8 establishes them seconds after Configure (verified).
                _timer = new Timer(_ => RunOnce(), null, 30_000, Timeout.Infinite);
            }
            else if (State == State.Terminated)
            {
                try { _timer?.Dispose(); } catch { }
                Detach();
                if (_fired && !_finished) Finish("NinjaTrader terminated before the cycle completed");
            }
        }

        // ---------------- the one shot ----------------

        private void RunOnce()
        {
            try
            {
                lock (_gate)
                {
                    if (_fired) return;

                    // ---- 1. the account must be Sim101, and must be PROVEN to be the simulator ----
                    var matches = Account.All
                        .Where(a => string.Equals(a.Name, TargetAccount, StringComparison.Ordinal))
                        .ToList();

                    Note("Account.All = [" + string.Join(", ", Account.All.Select(a =>
                        a.Name + "/" + SafeProvider(a))) + "]");

                    if (matches.Count != 1)
                    {
                        Abort("expected exactly one account named '" + TargetAccount + "', found " + matches.Count);
                        return;
                    }
                    var account = matches[0];

                    var provider = SafeProvider(account);
                    if (provider != Provider.Simulator.ToString())
                    {
                        Abort("account '" + TargetAccount + "' reports Provider=" + provider +
                              ", not " + Provider.Simulator + ". Refusing to send anything.");
                        return;
                    }
                    Note("verified Provider=" + provider);

                    // Corroborating simulator indicators, reported whether or not they gate.
                    Note("SimulatorInitialCash=" + Safe(() => account.SimulatorInitialCash.ToString(CultureInfo.InvariantCulture)));
                    Note("denomination=" + Safe(() => account.Denomination.ToString()));
                    Note("connection options=" + DescribeConnection(account));

                    // ---- 2. the connection must be up; we never try to connect anything ----
                    if (account.Connection == null) { Abort("account has no connection object"); return; }
                    var status = Safe(() => account.ConnectionStatus.ToString());
                    if (status != ConnectionStatus.Connected.ToString())
                    {
                        Abort("account connection status is " + status + ", not Connected. " +
                              "Connect the simulation feed by hand; this probe will not connect anything.");
                        return;
                    }
                    Note("verified ConnectionStatus=" + status);

                    // ---- 3. an instrument the feed actually serves ----
                    Instrument instrument = null;
                    foreach (var candidate in InstrumentCandidates)
                    {
                        var found = Safe(() => Instrument.GetInstrument(candidate, false));
                        if (found != null) { instrument = found; Note("instrument resolved: " + candidate); break; }
                        Note("instrument not resolved: " + candidate);
                    }
                    if (instrument == null) { Abort("none of the candidate instruments resolved"); return; }

                    // ---- 4. a price that cannot fill ----
                    double reference = 0;
                    try
                    {
                        if (instrument.MarketData?.Last != null) reference = instrument.MarketData.Last.Price;
                        if (reference <= 0 && instrument.MarketData?.Bid != null) reference = instrument.MarketData.Bid.Price;
                        if (reference <= 0 && instrument.MarketData?.LastClose != null) reference = instrument.MarketData.LastClose.Price;
                    }
                    catch { }
                    Note("market reference price = " + reference.ToString(CultureInfo.InvariantCulture));

                    var tickSize = instrument.MasterInstrument.TickSize;
                    double limitPrice = reference > 0 ? reference * 0.10 : tickSize * 100.0;
                    limitPrice = instrument.MasterInstrument.RoundToTickSize(limitPrice);
                    if (limitPrice <= 0) { Abort("could not compute a safe limit price"); return; }
                    if (reference > 0 && limitPrice >= reference * 0.5)
                    { Abort("computed limit price is not far enough below the market; refusing"); return; }
                    Note("buy limit price = " + limitPrice.ToString(CultureInfo.InvariantCulture) +
                         " (tick size " + tickSize.ToString(CultureInfo.InvariantCulture) + ")");

                    // ---- 5. burn the gate BEFORE sending, so this can never happen twice ----
                    _fired = true;
                    try { File.Delete(GatePath); Note("gate file deleted before submitting"); }
                    catch (Exception ex) { Abort("could not delete the gate file, refusing to send: " + ex.Message); return; }

                    // ---- 6. one limit order ----
                    _account = account;
                    _account.OrderUpdate += OnOrderUpdate;

                    // Positional: the parameter names differ between overloads, so the compiler picks
                    // the 10-argument one by shape. Limit, 1 contract, Day, far below the market.
                    _order = _account.CreateOrder(
                        instrument,
                        OrderAction.Buy,
                        OrderType.Limit,
                        TimeInForce.Day,
                        QuantityContracts,
                        limitPrice,
                        0,
                        string.Empty,
                        "deadman-latency",
                        null);

                    _submitUtc = DateTime.UtcNow;
                    _tSubmit = Stopwatch.GetTimestamp();
                    Note("submitting 1 LIMIT buy @ " + limitPrice.ToString(CultureInfo.InvariantCulture) +
                         " on " + instrument.FullName + " / " + TargetAccount);
                    _account.Submit(new[] { _order });

                    // Safety net: if nothing comes back, report what we have.
                    _timer = new Timer(_ => { if (!_finished) Finish("timed out waiting for the order to work"); },
                                       null, 45_000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Abort("RunOnce threw: " + ex);
            }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                if (_order == null || e.Order == null) return;
                if (!ReferenceEquals(e.Order, _order) && e.Order.Id != _order.Id) return;

                var now = Stopwatch.GetTimestamp();
                Note("order state -> " + e.OrderState + "  (NT8 event time " +
                     e.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + ")");

                if (e.OrderState == OrderState.Rejected)
                {
                    Finish("order was REJECTED by the venue: " + Safe(() => e.Error.ToString()) +
                           " " + Safe(() => e.Comment));
                    return;
                }

                var isWorking = e.OrderState == OrderState.Working || e.OrderState == OrderState.Accepted ||
                                e.OrderState == OrderState.AcceptedByRisk;

                if (isWorking && !_cancelIssued)
                {
                    // ---- the measurement: we have DETECTED a live order; cancel it now ----
                    _tDetect = now;
                    _cancelIssued = true;
                    _account.Cancel(new[] { _order });
                    _tCancelIssued = Stopwatch.GetTimestamp();
                    Note("cancel issued");
                    return;
                }

                if (e.OrderState == OrderState.Cancelled)
                {
                    _tCancelled = now;
                    Finish(null);
                }
            }
            catch (Exception ex) { Note("OnOrderUpdate threw: " + ex.Message); }
        }

        // ---------------- reporting ----------------

        private static double Ms(long from, long to) =>
            from == 0 || to == 0 ? -1 : (to - from) * 1000.0 / Stopwatch.Frequency;

        private void Finish(string problem)
        {
            lock (_gate)
            {
                if (_finished) return;
                _finished = true;
            }
            Detach();

            var sb = new StringBuilder();
            sb.AppendLine("# deadman-guardian — detect-and-cancel latency");
            sb.AppendLine();
            sb.AppendLine("One resting limit order on **" + TargetAccount + "**, placed programmatically, watched to a");
            sb.AppendLine("working state and cancelled immediately. It could not fill: the limit sat far below the market.");
            sb.AppendLine();
            sb.AppendLine("- UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            sb.AppendLine("- submitted at: " + (_submitUtc == default(DateTime) ? "never" : _submitUtc.ToString("O", CultureInfo.InvariantCulture)));
            if (problem != null) sb.AppendLine("- **outcome: " + problem + "**");
            sb.AppendLine();

            sb.AppendLine("## Measurements");
            sb.AppendLine();
            sb.AppendLine("| leg | what it covers | ms |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| submit → working observed | our call out, the venue's accept, and NT8 raising the event | " + Fmt(Ms(_tSubmit, _tDetect)) + " |");
            sb.AppendLine("| **working observed → cancel issued** | **the guardian's own reaction — the only part this design controls** | " + Fmt(Ms(_tDetect, _tCancelIssued)) + " |");
            sb.AppendLine("| cancel issued → cancelled confirmed | the venue's round trip back | " + Fmt(Ms(_tCancelIssued, _tCancelled)) + " |");
            sb.AppendLine("| **submit → cancelled confirmed** | **the whole cycle, end to end** | " + Fmt(Ms(_tSubmit, _tCancelled)) + " |");
            sb.AppendLine();
            sb.AppendLine("The middle row is the number SPEC §9.5 is about. The others are the platform's and the venue's,");
            sb.AppendLine("and no add-on can shrink them — which is exactly why §2 says this bounds exposure and not loss.");
            sb.AppendLine();

            sb.AppendLine("## Trace");
            sb.AppendLine();
            sb.AppendLine("```");
            lock (_gate) foreach (var l in _log) sb.AppendLine(l);
            sb.AppendLine("```");

            try { Directory.CreateDirectory(OutDir); File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false)); }
            catch { }
        }

        private static string Fmt(double ms) => ms < 0 ? "—" : ms.ToString("F1", CultureInfo.InvariantCulture);

        private void Detach()
        {
            try { if (_account != null) _account.OrderUpdate -= OnOrderUpdate; } catch { }
        }

        private void Abort(string reason)
        {
            Note("ABORTED: " + reason);
            Finish("aborted before sending anything — " + reason);
        }

        private static string SafeProvider(Account a)
        {
            try { return a.Provider.ToString(); } catch { return "?"; }
        }

        private static string DescribeConnection(Account a)
        {
            try
            {
                var options = a.Connection?.Options;
                if (options == null) return "none";
                var type = options.GetType();
                var name = type.GetProperty("Name")?.GetValue(options) as string;
                var brand = type.GetProperty("BrandName")?.GetValue(options) as string;
                return type.Name + " name='" + (name ?? "") + "' brand='" + (brand ?? "") + "'";
            }
            catch (Exception ex) { return "err:" + ex.GetType().Name; }
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch (Exception ex) { return "err:" + ex.GetType().Name; }
        }

        private static Instrument Safe(Func<Instrument> f)
        {
            try { return f(); } catch { return null; }
        }

        private void Note(string line)
        {
            var stamped = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line;
            lock (_gate) _log.Add(stamped);
        }
    }
}
