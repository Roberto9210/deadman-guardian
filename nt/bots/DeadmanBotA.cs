// deadman-guardian — BOT A, "el desastroso": a bot whose PURPOSE IS TO LOSE.
//
// ============================================================================================
//  THIS IS NOT A STRATEGY. IT IS NOT A DRAFT OF A STRATEGY. IT HAS NO EDGE, BY CONSTRUCTION,
//  AND ANY VERSION OF IT THAT STARTS WINNING IS BROKEN AND MUST BE FIXED BACK.
//
//  Bot A exists to provoke the guardian. It loses money in a controlled, bounded, deliberate way
//  until the guardian's daily loss limit is breached, and then it KEEPS TRYING TO ENTER, so that
//  the record can distinguish the two things people confuse:
//
//      a FLATTEN is one action.  a LOCKOUT is a standing state that keeps acting.
//
//  If you find yourself tuning the entry, adding a filter, or "improving" anything below to make
//  it lose less: stop. Losing IS the specification. The only legitimate changes here are ones that
//  make it lose more RELIABLY or more SAFELY - never ones that make it lose less.
// ============================================================================================
//
// HOW IT LOSES, and the arithmetic, so nobody has to guess:
//   Pure churn. Market in, hold HoldMs, market out. Every round trip crosses the spread twice and
//   therefore pays ONE MES TICK = $1.25 even when the price has not moved at all. That is the
//   expected loss per round trip; the price wandering during the hold is noise around it, roughly
//   +/- $2.65 (1 sigma, 3s of MES). Reaching the $50 sandbox limit needs ~40 round trips of drift;
//   the budget below allows 100. It is not a coin flip dressed as a plan - the drift is structural
//   and the budget is 2.5x what the drift needs.
//
//   If the budget or the time runs out before the limit is reached, THE RUN SAYS SO. It does not
//   raise the size, lengthen the hold, or otherwise chase the loss. A test that cannot reach its
//   condition reports that it did not, which is the same fail-closed manners the thing it is
//   testing has.
//
// WHAT IT MEASURES AFTER THE LOCKOUT - the part that is actually new evidence:
//   nt\addon\DeadmanGuardianAddOn.cs states, from a runtime scan of 2,912 types, that NT8 offers no
//   pre-submit veto, so enforcement is DETECT-AND-CANCEL. That has a window, and an honest report
//   has to measure it rather than assert it away. So the provocation alternates two probe kinds:
//
//     odd probes  - a resting LIMIT far from the market. The guardian CAN cancel this one before it
//                   ever fills. Measures: cancelled? how many ms from submit to cancelled?
//     even probes - a MARKET order, which on a simulator fills essentially at once. The guardian
//                   cannot stop it. Measures: how many ms until it was FLATTENED, and whether the
//                   guardian stayed LOCKED and flattened it AGAIN the next time.
//
//   The second column is the uncomfortable one and it is the point. "The lockout blocks orders" is
//   false as stated for market orders; what is true is "the lockout refuses to let exposure stand".
//   Bot A produces the number that lets the certificate say the true thing instead of the neat one.
//
// SAFETY RAILS (all in BotGuardrails.cs, all refusing in code):
//   Sim101 only, Provider PROVEN == Simulator, exactly one match, Connected - or abort before
//   constructing an order. 1 contract per order, 1 net contract, MaxOrdersPerSession hard cap, gate
//   file burned before the first send, never runs while Bot B's gate exists, auto-shutdown at the
//   session boundary with its own orders cancelled and its own position flattened.
//
//   It drives its OWN guardian over a SANDBOX state and ledger with a small limit, so the
//   production guardian's files are never touched. The PORTS UNDERNEATH ARE THE REAL ONES - real
//   NT8 broker, real account feed, real executions - which is exactly what the soak could not do.
//
// Deployed to: Documents\NinjaTrader 8\bin\Custom\AddOns\DeadmanBotA.cs
// Gate:        Documents\NinjaTrader 8\deadman-guardian-bots\botA.GO
// Source:      <repo>/nt/bots/DeadmanBotA.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using GuardianCore;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class DeadmanBotA : AddOnBase
    {
        // ---- the deliberate-loss parameters. Every one of them is a bound, not a tuning knob. ----
        private const string OrderTag = "deadman-botA";
        private const int Quantity = 1;
        private const int HoldMs = 3_000;
        private const int PauseBetweenRoundTripsMs = 1_500;
        private const int MaxOrdersPerSession = 200;   // 100 round trips; the drift needs ~40
        private const int MaxContractsPerOrder = 1;
        private const int MaxNetContracts = 1;
        private const int LossPhaseMaxMinutes = 45;

        // ---- the sandbox guardian's limits: small on purpose, so a real breach is minutes away ----
        private const string SandboxPersonalLimit = "50.00";
        private const decimal SandboxFirmLimit = 100.00m;

        // ---- the provocation ----
        private const int PostLockoutProbes = 8;
        private const int PostLockoutProbeIntervalMs = 6_000;
        private const int FlattenObservationMs = 15_000;

        private static readonly string[] InstrumentCandidates =
            { "MES 09-26", "MES 12-26", "MES 03-27", "MES" };

        private readonly BotLog _log = new BotLog();
        private readonly SessionBudget _budget =
            new SessionBudget(MaxOrdersPerSession, MaxContractsPerOrder, MaxNetContracts);

        private readonly object _gate = new object();
        private readonly Dictionary<string, ProbeOrder> _probes =
            new Dictionary<string, ProbeOrder>(StringComparer.Ordinal);
        private readonly HashSet<string> _weCancelled = new HashSet<string>(StringComparer.Ordinal);

        private Account _account;
        private Instrument _instrument;
        private BotSandboxGuardian _sandbox;
        private Timer _startTimer;
        private Timer _tickTimer;
        private Thread _worker;
        private volatile bool _stopping;
        private bool _ran;

        // ---- what the report is made of ----
        private readonly List<long> _gateMicros = new List<long>();
        private volatile bool _abortRun;
        private string _unsafeReason;

        private DateTime _startedUtc;
        private int _roundTrips;
        private DateTime? _lockedAtUtc;
        private decimal _dayLossAtLockout;
        private string _lockoutReason = "";
        private int _probesLimit, _probesMarket;
        private int _limitProbesCancelledByGuardian, _limitProbesLeftWorking;
        private int _marketProbesFilled, _marketProbesFlattened, _marketProbesLeftOpen;
        private readonly List<long> _cancelLatenciesMs = new List<long>();
        private readonly List<long> _flattenLatenciesMs = new List<long>();
        private bool _stateLeftLocked;
        private string _abortReason;

        private sealed class ProbeOrder
        {
            public Order Order;
            public string Kind;              // "limit" or "market"
            public long SubmittedTicks;
            public bool Filled;
            public bool CancelledByGuardian;
            public long? ResolvedMs;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "deadman BOT A - the disaster (Sim101 only, loses on purpose)";
            }
            else if (State == State.Configure)
            {
                if (!File.Exists(BotPaths.Gate("A"))) return;
                Note("gate present; bot A armed, starting in 45s");
                _startTimer = new Timer(_ => Launch(), null, 45_000, Timeout.Infinite);
            }
            else if (State == State.Terminated)
            {
                Shutdown("NinjaTrader terminated the add-on");
            }
        }

        private void Launch()
        {
            lock (_gate) { if (_ran) return; _ran = true; }
            _worker = new Thread(Run) { IsBackground = true, Name = "deadman-botA" };
            _worker.Start();
        }

        // ------------------------------------------------------------------ the run

        private void Run()
        {
            _startedUtc = DateTime.UtcNow;
            try
            {
                if (BotGate.OtherGateBlocks("A", Note)) { Abort("bot B's gate is present"); return; }

                _account = BotSafety.VerifyAccount(Note);
                if (_account == null) { Abort("account verification failed"); return; }

                _instrument = BotSafety.ResolveInstrument(Note, InstrumentCandidates);
                if (_instrument == null) { Abort("no usable instrument"); return; }

                if (!BotGate.Burn("A", Note)) { Abort("gate could not be burned"); return; }

                _sandbox = new BotSandboxGuardian("A", _startedUtc, Note);
                if (!_sandbox.Arm(SandboxPersonalLimit, SandboxFirmLimit))
                { Abort("the sandbox guardian refused to arm"); return; }

                Subscribe();
                _tickTimer = new Timer(_ => SafeTick(), null, Constants.PnlEvaluationIntervalMs,
                                       Constants.PnlEvaluationIntervalMs);

                LossPhase();
                ProvocationPhase();
            }
            catch (Exception ex)
            {
                Note("RUN THREW: " + ex);
                Abort("run threw: " + ex.Message);
            }
            finally
            {
                Shutdown("run finished");
                WriteReport();
            }
        }

        /// <summary>Phase 1: lose, on purpose, until the guardian locks out. Nothing here tries to be
        /// clever about WHEN to trade - a bot that picked its moments would be a strategy.</summary>
        private void LossPhase()
        {
            Note("---- loss phase: churning until the sandbox guardian locks at $" + SandboxPersonalLimit + " ----");
            var deadline = DateTime.UtcNow.AddMinutes(LossPhaseMaxMinutes);

            while (!_stopping && !_abortRun && DateTime.UtcNow < deadline)
            {
                if (_sandbox.Status.Kind == StateKind.Locked)
                {
                    _lockedAtUtc = DateTime.UtcNow;
                    _dayLossAtLockout = _sandbox.DayLoss();
                    _lockoutReason = _sandbox.Status.Reason ?? "";
                    Note("LOCKED after " + _roundTrips + " round trips; dayLoss=" +
                         Money.Format(_dayLossAtLockout) + "; reason=" + _lockoutReason);
                    return;
                }

                if (!RoundTrip()) return;
                Sleep(PauseBetweenRoundTripsMs);
            }

            // Honest failure. No escalation.
            Note("loss phase ENDED WITHOUT A LOCKOUT after " + _roundTrips + " round trips (" +
                 _budget.Summary() + "). Not escalating size or hold: reporting that it did not reach the limit.");
        }

        /// <summary>One deliberate round trip: in at the market, hold, out at the market. The loss is
        /// the spread, twice, and it is the whole mechanism.</summary>
        private bool RoundTrip()
        {
            var side = (_roundTrips % 2 == 0) ? OrderAction.Buy : OrderAction.SellShort;
            var exit = (side == OrderAction.Buy) ? OrderAction.Sell : OrderAction.BuyToCover;

            var entry = SubmitMarket(side, "entry");
            if (entry == null) return false;

            Sleep(HoldMs);

            var close = SubmitMarket(exit, "exit");
            if (close == null)
            {
                // We are possibly holding exposure we could not close through the normal path.
                // Flatten is not optional here: leaving a position open is the one outcome this bot
                // is never allowed to produce.
                Note("exit order refused - flattening directly");
                FlattenOwn("exit order refused");
                return false;
            }

            _roundTrips++;
            if (_roundTrips % 5 == 0)
                Note("round trip " + _roundTrips + "; sandbox dayLoss=" + Money.Format(_sandbox.DayLoss()) +
                     "; " + _budget.Summary());
            return true;
        }

        /// <summary>Phase 2: the guardian is LOCKED. Keep trying to open exposure and record exactly
        /// what it does about it. This is where "a flatten is not a lockout" is either shown or not.</summary>
        private void ProvocationPhase()
        {
            if (_sandbox.Status.Kind != StateKind.Locked)
            {
                Note("---- provocation phase SKIPPED: the guardian never locked, so there is nothing to provoke ----");
                return;
            }

            Note("---- provocation phase: " + PostLockoutProbes + " entry attempts against a LOCKED guardian ----");

            for (var i = 1; i <= PostLockoutProbes && !_stopping && !_abortRun; i++)
            {
                if (_sandbox.Status.Kind != StateKind.Locked)
                {
                    _stateLeftLocked = true;
                    Note("!!! the guardian LEFT the Locked state during provocation: " + _sandbox.Status.Kind);
                    break;
                }

                if (i % 2 == 1) ProbeWithRestingLimit(i);
                else ProbeWithMarketOrder(i);

                Sleep(PostLockoutProbeIntervalMs);
            }

            if (_sandbox.Status.Kind != StateKind.Locked) _stateLeftLocked = true;
            Note("provocation finished; guardian state = " + _sandbox.Status.Kind +
                 "; net position left = " + NetQty());
        }

        /// <summary>An order the guardian CAN stop: it rests, so detect-and-cancel has time to work.</summary>
        private void ProbeWithRestingLimit(int n)
        {
            var price = FarBelowMarketPrice();
            if (price <= 0) { Note("probe " + n + " (limit): could not compute a resting price, skipped"); return; }

            var order = Submit(OrderAction.Buy, OrderType.Limit, price, "probe" + n + "-limit");
            if (order == null) return;
            _probesLimit++;

            var key = KeyOf(order);
            lock (_gate)
                _probes[key] = new ProbeOrder { Order = order, Kind = "limit", SubmittedTicks = Stopwatch.GetTimestamp() };

            Note("probe " + n + " (limit) submitted @ " + price.ToString(CultureInfo.InvariantCulture) +
                 " - the guardian should cancel it");

            Sleep(FlattenObservationMs);

            lock (_gate)
            {
                var p = _probes[key];
                if (p.CancelledByGuardian)
                {
                    _limitProbesCancelledByGuardian++;
                    if (p.ResolvedMs.HasValue) _cancelLatenciesMs.Add(p.ResolvedMs.Value);
                    Note("probe " + n + " (limit): CANCELLED by the guardian after " + p.ResolvedMs + " ms");
                }
                else if (IsWorking(p.Order))
                {
                    _limitProbesLeftWorking++;
                    Note("probe " + n + " (limit): STILL WORKING after " + FlattenObservationMs +
                         " ms - the guardian did not cancel it");
                }
                else
                {
                    Note("probe " + n + " (limit): resolved without a guardian cancel, state=" + StateOf(p.Order));
                }
            }

            CancelOwn(key);
        }

        /// <summary>An order the guardian CANNOT stop before it fills. What must happen instead is that
        /// the exposure does not survive: the guardian flattens it, and stays locked.</summary>
        private void ProbeWithMarketOrder(int n)
        {
            var order = SubmitMarket(OrderAction.Buy, "probe" + n + "-market");
            if (order == null) return;
            _probesMarket++;

            var key = KeyOf(order);
            lock (_gate)
                _probes[key] = new ProbeOrder { Order = order, Kind = "market", SubmittedTicks = Stopwatch.GetTimestamp() };

            Note("probe " + n + " (market) submitted - the guardian cannot stop the fill; it must flatten it");

            var start = Stopwatch.GetTimestamp();
            var flattened = false;
            var filled = false;
            var deadline = DateTime.UtcNow.AddMilliseconds(FlattenObservationMs);

            while (DateTime.UtcNow < deadline && !_stopping)
            {
                lock (_gate) { filled = filled || _probes[key].Filled; }
                if (filled && NetQty() == 0) { flattened = true; break; }
                Sleep(200);
            }

            var elapsed = ElapsedMs(start);

            if (filled) _marketProbesFilled++;

            if (filled && flattened)
            {
                _marketProbesFlattened++;
                _flattenLatenciesMs.Add(elapsed);
                Note("probe " + n + " (market): FILLED, then flattened by the guardian after " + elapsed + " ms");
            }
            else if (filled)
            {
                _marketProbesLeftOpen++;
                Note("probe " + n + " (market): FILLED and STILL OPEN after " + FlattenObservationMs +
                     " ms - net " + NetQty() + ". Flattening it ourselves so the run leaves nothing behind.");
                FlattenOwn("probe " + n + " left exposure open");
            }
            else
            {
                Note("probe " + n + " (market): never filled, state=" + StateOf(OrderOf(key)));
            }
        }

        // ------------------------------------------------------------------ order plumbing

        private Order SubmitMarket(OrderAction action, string label)
        {
            return Submit(action, OrderType.Market, 0, label);
        }

        private Order Submit(OrderAction action, OrderType type, double limitPrice, string label)
        {
            if (!GateOpen()) return null;

            var opening = action == OrderAction.Buy || action == OrderAction.SellShort;
            var netAfter = opening
                ? NetQty() + (action == OrderAction.Buy ? Quantity : -Quantity)
                : 0;

            string denial;
            if (!_budget.TryReserveOrder(Quantity, netAfter, out denial))
            {
                Note("order REFUSED by the budget (" + label + "): " + denial);
                return null;
            }

            try
            {
                // The 12-argument overload, not the 10-argument one the latency probe used: reflection
                // over NinjaTrader.Core shows that one carries [Obsolete]. OrderEntry.Automated is not
                // cosmetic either - it is how NT8 records in its own log that a machine sent this. A bot
                // filing its orders as Manual would be lying in the platform's evidence, which is the
                // evidence these runs are going to cite.
                var order = _account.CreateOrder(
                    _instrument,
                    action,
                    type,
                    OrderEntry.Automated,
                    TimeInForce.Day,
                    Quantity,
                    limitPrice,
                    0,
                    string.Empty,
                    OrderTag,
                    Core.Globals.MaxDate,
                    null);

                _account.Submit(new[] { order });
                return order;
            }
            catch (Exception ex)
            {
                Note("submit threw (" + label + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>THE GATE, before every single send. "Listed but disconnected" stops being true
        /// the moment somebody clicks Connect, so a start-up check cannot hold it - the same reason the
        /// armed check moved here rather than staying at boot. If it closes mid-run the bot stops and
        /// writes WHY into its own chained record, not only into the log.</summary>
        private bool GateOpen()
        {
            long micros;
            var verdict = BotSafety.StillSafe(out micros);
            lock (_gate) _gateMicros.Add(micros);
            if (verdict.Allowed) return true;
            HaltUnsafe(verdict.Reason);
            return false;
        }

        private void HaltUnsafe(string reason)
        {
            lock (_gate)
            {
                if (_unsafeReason != null) return;   // say it once
                _unsafeReason = reason;
            }
            Note("!!! ACCOUNT GATE CLOSED MID-RUN, stopping: " + reason);
            if (_sandbox != null)
            {
                _sandbox.RecordBotEvent("BOT_ACCOUNT_UNSAFE",
                    JsonValue.Obj().Set("bot", "A").Set("reason", reason));
            }
            // NOT _stopping: that flag makes Shutdown() return early, and this run still has to cancel
            // its orders and flatten. This one only ends the trading loops.
            _abortRun = true;
        }

        private void Subscribe()
        {
            _account.ExecutionUpdate += OnExecutionUpdate;
            _account.OrderUpdate += OnOrderUpdate;
            Note("subscribed to " + BotSafety.TargetAccount + " (executions and orders feed the sandbox guardian)");
        }

        /// <summary>Every fill goes to the sandbox guardian exactly as the production adapter does it -
        /// same translator, same call. This is the whole reason the run is worth anything: the P&amp;L
        /// the guardian acts on is NinjaTrader's, not a number this file made up.</summary>
        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                if (e == null || e.Execution == null) return;
                var record = ExecutionTranslator.Translate(e.Execution);
                _sandbox.OnExecution(record);

                var order = e.Execution.Order;
                if (order == null) return;
                var key = KeyOf(order);
                lock (_gate) { ProbeOrder p; if (_probes.TryGetValue(key, out p)) p.Filled = true; }
            }
            catch (Exception ex) { Note("OnExecutionUpdate: " + ex.Message); }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                if (e == null || e.Order == null) return;

                if (Accounts.IsWorking(e.OrderState))
                {
                    _sandbox.OnOrderObserved(new OrderSnapshot(
                        BotSafety.TargetAccount,
                        e.Order.OrderId ?? e.Order.Id.ToString(CultureInfo.InvariantCulture),
                        e.Order.Instrument == null ? "?" : e.Order.Instrument.FullName,
                        e.Order.OrderAction.ToString()));
                    return;
                }

                if (e.OrderState != OrderState.Cancelled) return;

                var key = KeyOf(e.Order);
                lock (_gate)
                {
                    ProbeOrder p;
                    if (!_probes.TryGetValue(key, out p)) return;
                    if (_weCancelled.Contains(key)) return;   // ours, not the guardian's - do not claim it
                    p.CancelledByGuardian = true;
                    p.ResolvedMs = ElapsedMs(p.SubmittedTicks);
                }
            }
            catch (Exception ex) { Note("OnOrderUpdate: " + ex.Message); }
        }

        private void SafeTick()
        {
            try { _sandbox.Tick(); } catch (Exception ex) { Note("tick: " + ex.Message); }
        }

        // ------------------------------------------------------------------ cleanup

        private void Shutdown(string why)
        {
            if (_stopping) return;
            _stopping = true;
            Note("shutdown: " + why);

            try { if (_startTimer != null) _startTimer.Dispose(); } catch { }
            try { if (_tickTimer != null) _tickTimer.Dispose(); } catch { }

            if (_account != null)
            {
                try { _account.ExecutionUpdate -= OnExecutionUpdate; } catch { }
                try { _account.OrderUpdate -= OnOrderUpdate; } catch { }
                CancelAllOwn();
                FlattenOwn("shutdown");
            }

            try { if (_sandbox != null) _sandbox.Stop(); } catch { }
        }

        private IEnumerable<Order> OwnWorkingOrders()
        {
            try
            {
                return _account.Orders
                    .Where(o => string.Equals(o.Name, OrderTag, StringComparison.Ordinal) &&
                                Accounts.IsWorking(o.OrderState))
                    .ToList();
            }
            catch { return new List<Order>(); }
        }

        private void CancelAllOwn()
        {
            try
            {
                var ours = OwnWorkingOrders().ToList();
                if (ours.Count == 0) return;
                foreach (var o in ours) lock (_gate) _weCancelled.Add(KeyOf(o));
                Note("cleanup: cancelling " + ours.Count + " of our own working order(s)");
                _account.Cancel(ours);
            }
            catch (Exception ex) { Note("cleanup cancel threw: " + ex.Message); }
        }

        private void CancelOwn(string key)
        {
            try
            {
                var order = OrderOf(key);
                if (order == null || !Accounts.IsWorking(order.OrderState)) return;
                lock (_gate) _weCancelled.Add(key);
                _account.Cancel(new[] { order });
            }
            catch (Exception ex) { Note("cancel threw: " + ex.Message); }
        }

        /// <summary>The one outcome this bot may never produce is a position left standing. Unlike the
        /// soak's ScopedNtBroker - which refuses to flatten because it must never touch a position it
        /// did not create - Bot A DOES flatten, because on this account every open contract is its
        /// own. That difference is deliberate and is the reason the account check above is absolute.</summary>
        private void FlattenOwn(string why)
        {
            try
            {
                var instruments = _account.Positions
                    .Where(p => p.Quantity != 0 && p.MarketPosition != MarketPosition.Flat)
                    .Select(p => p.Instrument)
                    .Distinct()
                    .ToList();
                if (instruments.Count == 0) return;
                Note("flatten (" + why + "): " + instruments.Count + " instrument(s)");
                _account.Flatten(instruments);
            }
            catch (Exception ex) { Note("flatten threw: " + ex.Message); }
        }

        // ------------------------------------------------------------------ helpers

        private int NetQty()
        {
            try
            {
                var p = _account.Positions.FirstOrDefault(x => x.Instrument == _instrument);
                if (p == null || p.MarketPosition == MarketPosition.Flat) return 0;
                return p.MarketPosition == MarketPosition.Short ? -p.Quantity : p.Quantity;
            }
            catch { return 0; }
        }

        private double FarBelowMarketPrice()
        {
            try
            {
                double reference = 0;
                if (_instrument.MarketData != null)
                {
                    if (_instrument.MarketData.Last != null) reference = _instrument.MarketData.Last.Price;
                    if (reference <= 0 && _instrument.MarketData.Bid != null) reference = _instrument.MarketData.Bid.Price;
                    if (reference <= 0 && _instrument.MarketData.LastClose != null) reference = _instrument.MarketData.LastClose.Price;
                }
                if (reference <= 0) return 0;

                var price = _instrument.MasterInstrument.RoundToTickSize(reference * 0.10);
                if (price <= 0 || price >= reference * 0.5) return 0;   // refuse anything that could fill
                return price;
            }
            catch { return 0; }
        }

        private static string KeyOf(Order o)
        {
            try { return o.OrderId ?? o.Id.ToString(CultureInfo.InvariantCulture); }
            catch { return Guid.NewGuid().ToString("N"); }
        }

        private Order OrderOf(string key)
        {
            lock (_gate) { ProbeOrder p; return _probes.TryGetValue(key, out p) ? p.Order : null; }
        }

        private static bool IsWorking(Order o)
        {
            try { return o != null && Accounts.IsWorking(o.OrderState); } catch { return false; }
        }

        private static string StateOf(Order o)
        {
            try { return o == null ? "?" : o.OrderState.ToString(); } catch { return "?"; }
        }

        private static long ElapsedMs(long sinceTicks)
        {
            return (long)((Stopwatch.GetTimestamp() - sinceTicks) * 1000.0 / Stopwatch.Frequency);
        }

        private void Sleep(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (!_stopping && !_abortRun && DateTime.UtcNow < deadline) Thread.Sleep(50);
        }

        private void Abort(string reason)
        {
            _abortReason = reason;
            Note("ABORTED: " + reason);
        }

        private void Note(string message) { _log.Note(message); }

        // ------------------------------------------------------------------ the report

        private void WriteReport()
        {
            var body = new List<string>();
            var title = "Bot A run " + _startedUtc.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture);

            if (_abortReason != null)
            {
                body.Add("**Aborted before completing: " + _abortReason + "**");
                body.Add("");
                body.Add("Orders placed: " + _budget.Summary() + ".");
                _log.AppendSection(BotPaths.Report("A"), title, body);
                return;
            }

            var lockedOut = _lockedAtUtc.HasValue;

            body.Add("- account: `Sim101` (Provider proven `Simulator` before anything was sent)");
            body.Add("- sandbox guardian limit: personal $" + SandboxPersonalLimit +
                     ", firm $" + Money.Format(SandboxFirmLimit) + " (production's files untouched)");
            body.Add("- orders: " + _budget.Summary());
            body.Add("- round trips before the lockout: **" + _roundTrips + "**");
            body.Add("");

            body.Add("### What the guardian did");
            body.Add("");
            body.Add("| | |");
            body.Add("|---|---|");
            body.Add("| fired | " + (lockedOut
                ? "**yes**, " + _lockedAtUtc.Value.ToString("HH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                : "**NO - the limit was never reached in budget**") + " |");
            if (lockedOut)
            {
                body.Add("| day loss at the lockout | $" + Money.Format(_dayLossAtLockout) + " |");
                body.Add("| reason | " + Escape(_lockoutReason) + " |");
            }
            body.Add("| ledger events | " + Escape(_sandbox.EventsSummary()) + " |");
            body.Add("| ledger chain | " + _sandbox.VerifyChain() + " |");
            body.Add("| left the Locked state during provocation | " +
                     (_stateLeftLocked ? "**YES - that is a failure**" : "no") + " |");
            body.Add("| position left open at the end | " + NetQty() + " |");
            body.Add("");

            body.Add("### Post-lockout entry attempts");
            body.Add("");
            body.Add("Two kinds, because NT8 has no pre-submit veto (addon header, 2,912 types scanned) and the");
            body.Add("two kinds meet enforcement differently. Reported separately on purpose.");
            body.Add("");
            body.Add("| probe kind | submitted | stopped by the guardian | not stopped | latency |");
            body.Add("|---|---|---|---|---|");
            body.Add("| resting LIMIT (cancellable) | " + _probesLimit + " | " + _limitProbesCancelledByGuardian +
                     " cancelled | " + _limitProbesLeftWorking + " left working | " + Latency(_cancelLatenciesMs) + " |");
            body.Add("| MARKET (fills first) | " + _probesMarket + " | " + _marketProbesFlattened +
                     " flattened after filling | " + _marketProbesLeftOpen + " left open | " +
                     Latency(_flattenLatenciesMs) + " |");
            body.Add("");
            body.Add("Market probes that filled: " + _marketProbesFilled + " of " + _probesMarket + ". A market order");
            body.Add("reaching a fill is **not** a guardian failure - it is the documented consequence of");
            body.Add("detect-and-cancel. The claim under test is the next column: the exposure did not survive.");
            body.Add("");

            body.Add("### The account gate, re-asked before every send");
            body.Add("");
            body.Add("| | |");
            body.Add("|---|---|");
            body.Add("| times evaluated | " + _gateMicros.Count + " |");
            body.Add("| cost per call | " + Micros(_gateMicros) + " |");
            body.Add("| closed mid-run | " + (_unsafeReason == null ? "no" : "**YES** - " + Escape(_unsafeReason)) + " |");
            body.Add("| bot-events chain | " + _sandbox.VerifyBotChain() + " |");
            body.Add("| bot-event write failures | " + _sandbox.BotEventFailures +
                     (_sandbox.BotEventFailures == 0 ? " (a zero that is always printed, so it is a verified zero)" : " **- events were lost**") + " |");
            body.Add("");

            body.Add("### Did this run disturb production?");
            body.Add("");
            body.Add("- production guardian state after the run: **" + ProductionState() + "**");
            body.Add("- production files were never opened for writing by this bot; its limit is $600 and this");
            body.Add("  run's losses are bounded by the sandbox limit of $" + SandboxPersonalLimit + ".");

            _log.AppendSection(BotPaths.Report("A"), title, body);
        }

        private static string Micros(List<long> samples)
        {
            if (samples == null || samples.Count == 0) return "n/a";
            return "min " + samples.Min() + " us, median " + Median(samples) + " us, max " + samples.Max() + " us";
        }

        private static string Latency(List<long> samples)
        {
            if (samples == null || samples.Count == 0) return "n/a";
            return "min " + samples.Min() + " ms, median " + Median(samples) + " ms, max " + samples.Max() + " ms";
        }

        private static long Median(List<long> samples)
        {
            var sorted = samples.OrderBy(x => x).ToList();
            return sorted[sorted.Count / 2];
        }

        /// <summary>Read-only, and it says "unreadable" rather than guessing - the same manners as the
        /// thing being tested.</summary>
        private static string ProductionState()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                        "NinjaTrader 8", "deadman-guardian", "state.json");
                if (!File.Exists(path)) return "no state file";
                var text = File.ReadAllText(path);
                var marker = "\"state\":\"";
                var i = text.IndexOf(marker, StringComparison.Ordinal);
                if (i < 0) return "unreadable";
                var j = text.IndexOf('"', i + marker.Length);
                if (j < 0) return "unreadable";
                return text.Substring(i + marker.Length, j - i - marker.Length);
            }
            catch { return "unreadable"; }
        }

        private static string Escape(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|");
        }
    }
}
