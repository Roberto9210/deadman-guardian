// deadman-guardian — BOT B, "el prudente": a bot whose PURPOSE IS TO BE IGNORED.
//
// ============================================================================================
//  THIS IS NOT A STRATEGY EITHER, AND IT IS NOT TRYING TO WIN.
//
//  Bot B's entry rule is a clock. It trades on a fixed cadence, in an alternating direction, with
//  a symmetric stop and target. Its expected value is negative by exactly the friction, and that is
//  FINE, because profit is not what it is measuring. Bot B measures the opposite of Bot A:
//
//      that an armed guardian does NOT interfere with a session that never misbehaves.
//
//  Zero interventions. Zero orders cancelled that the bot did not cancel itself. Zero flattens it
//  did not ask for. Whole sessions where the guardian's ledger contains nothing but the ordinary
//  heartbeat. Those are the clean days a certificate can count, and today they are clean only
//  because nobody traded — Bot B is what makes a clean day mean something.
//
//  If you are tempted to "improve the entry": that is a different project, and it is not this one.
//  A Bot B that started winning would still be measuring the same zero. A Bot B that started
//  taking bigger risks to win would destroy the only number it produces.
// ============================================================================================
//
// HOW IT STAYS OUT OF THE GUARDIAN'S WAY - three separate margins, not one:
//
//   1. SIZE. One contract, one net contract, ever. The cap refuses in code (SessionBudget).
//   2. A REAL STOP AT THE VENUE. Every entry is followed by a StopMarket order sent to NinjaTrader,
//      not a stop kept in this process. The distinction is the whole point of the sibling library:
//      a stop that lives in the bot dies with the bot. The TARGET, by contrast, IS managed in
//      process on purpose - a target that fails to fire costs an opportunity, and an opportunity is
//      not a risk event. Risk goes to the venue; convenience stays here.
//   3. A SELF-STOP FAR BELOW THE GUARDIAN'S LIMIT. Bot B stops trading for the day at
//      SelfStopUsd, which is a fraction of the guardian's limit. The guardian is never asked to do
//      its job, because the bot does its own first. That ordering is the claim: a well-behaved
//      trader meets their own limit, not the guardian's.
//
// WHAT COUNTS AS A CLEAN SESSION - fixed here, before any run, so it cannot be relaxed afterwards.
// All five, or the session is not clean:
//   * the guardian never left ARMED;
//   * its ledger holds no LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED, ORDER_REJECTED_LOCKED,
//     FAIL_CLOSED_ENTERED, PNL_DISAGREEMENT or PNL_UNCOMPUTABLE;
//   * every order this bot cancelled, this bot asked to cancel;
//   * the ledger chain verifies;
//   * the bot shut itself down at the session boundary holding nothing.
// The first four are Certificate.LimitRespected plus the events that would contradict it. The fifth
// is Bot B's own manners.
//
// SAFETY RAILS: identical to Bot A's, in BotGuardrails.cs. Sim101 only, Provider PROVEN Simulator,
// exactly one match, Connected, or abort before an order exists. Gate burned before the first send.
// Never runs while Bot A's gate exists.
//
// Deployed to: Documents\NinjaTrader 8\bin\Custom\AddOns\DeadmanBotB.cs
// Gate:        Documents\NinjaTrader 8\deadman-guardian-bots\botB.GO
// Source:      <repo>/nt/bots/DeadmanBotB.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using GuardianCore;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class DeadmanBotB : AddOnBase
    {
        private const string OrderTag = "deadman-botB";
        private const int Quantity = 1;
        private const int StopTicks = 8;              // MES: 8 ticks = 2.00 points = $10.00
        private const int TargetTicks = 8;            // symmetric on purpose: no edge is claimed
        private const int MaxHoldMinutes = 10;
        private const int MinutesBetweenTrades = 20;
        private const int MaxTradesPerSession = 12;
        private const int MaxOrdersPerSession = 48;   // entry + protective stop + exit, with slack
        private const int MaxContractsPerOrder = 1;
        private const int MaxNetContracts = 1;
        private const decimal SelfStopUsd = 15.00m;   // 30% of the sandbox limit below
        private const int ShutdownMarginMinutes = 10;

        private const string SandboxPersonalLimit = "50.00";
        private const decimal SandboxFirmLimit = 100.00m;
        private const string SessionZone = "America/Chicago";
        private static readonly TimeSpan SessionReset = new TimeSpan(17, 0, 0);

        private static readonly string[] InstrumentCandidates =
            { "MES 09-26", "MES 12-26", "MES 03-27", "MES" };

        /// <summary>An event in the sandbox ledger that would mean the guardian did something. If any
        /// of these appears, the session is not clean, and the report says which one.</summary>
        private static readonly string[] InterventionEvents =
        {
            Ev.LimitBreached, Ev.OrdersCancelled, Ev.FlattenRequested, Ev.OrderRejectedLocked,
            Ev.FailClosedEntered, Ev.PnlDisagreement, Ev.PnlUncomputable, Ev.ClockAnomaly,
            Ev.SealMismatch, Ev.ConfigTampered, Ev.StateCorrupt, Ev.LockoutIncomplete
        };

        private readonly BotLog _log = new BotLog();
        private readonly SessionBudget _budget =
            new SessionBudget(MaxOrdersPerSession, MaxContractsPerOrder, MaxNetContracts);

        private readonly object _gate = new object();
        private readonly HashSet<string> _weCancelled = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _cancelledNotByUs = new List<string>();

        private Account _account;
        private Instrument _instrument;
        private BotSandboxGuardian _sandbox;
        private SessionCalendar _calendar;
        private Timer _startTimer;
        private Timer _tickTimer;
        private Thread _worker;
        private volatile bool _stopping;
        private bool _ran;

        private DateTime _startedUtc;
        private DateTime _sessionEndUtc;
        private int _trades, _stopsFilled, _targetsHit, _timeExits;
        private decimal _worstDayLoss;
        private bool _selfStopped;
        private string _abortReason;
        private Order _protectiveStop;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "deadman BOT B - the prudent one (Sim101 only, never near the limit)";
            }
            else if (State == State.Configure)
            {
                if (!File.Exists(BotPaths.Gate("B"))) return;
                Note("gate present; bot B armed, starting in 45s");
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
            _worker = new Thread(Run) { IsBackground = true, Name = "deadman-botB" };
            _worker.Start();
        }

        // ------------------------------------------------------------------ the run

        private void Run()
        {
            _startedUtc = DateTime.UtcNow;
            try
            {
                if (BotGate.OtherGateBlocks("B", Note)) { Abort("bot A's gate is present"); return; }

                _account = BotSafety.VerifyAccount(Note);
                if (_account == null) { Abort("account verification failed"); return; }

                _instrument = BotSafety.ResolveInstrument(Note, InstrumentCandidates);
                if (_instrument == null) { Abort("no usable instrument"); return; }

                if (!ResolveSessionEnd()) { Abort("could not resolve the session boundary"); return; }

                if (!BotGate.Burn("B", Note)) { Abort("gate could not be burned"); return; }

                _sandbox = new BotSandboxGuardian("B", _startedUtc, Note);
                if (!_sandbox.Arm(SandboxPersonalLimit, SandboxFirmLimit))
                { Abort("the sandbox guardian refused to arm"); return; }

                Subscribe();
                _tickTimer = new Timer(_ => SafeTick(), null, Constants.PnlEvaluationIntervalMs,
                                       Constants.PnlEvaluationIntervalMs);

                TradeTheSession();
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

        /// <summary>The session boundary comes from the SAME calendar the guardian uses, so the bot's
        /// shutdown and the guardian's day rollover cannot drift apart.</summary>
        private bool ResolveSessionEnd()
        {
            TimeZoneInfo zone; string error;
            if (!TimeZoneMap.TryResolve(SessionZone, out zone, out error))
            { Note("ABORT: time zone '" + SessionZone + "' did not resolve: " + error); return false; }

            _calendar = new SessionCalendar(zone, SessionReset);
            _sessionEndUtc = _calendar.SessionEndUtc(DateTime.UtcNow).AddMinutes(-ShutdownMarginMinutes);
            Note("session ends " + _sessionEndUtc.ToString("u", CultureInfo.InvariantCulture) +
                 " (reset " + SessionReset + " " + SessionZone + ", minus a " + ShutdownMarginMinutes +
                 " minute margin)");
            return true;
        }

        private void TradeTheSession()
        {
            Note("---- trading the session: at most " + MaxTradesPerSession + " trades, one contract, " +
                 "self-stop at $" + Money.Format(SelfStopUsd) + " against a $" + SandboxPersonalLimit + " limit ----");

            while (!_stopping && DateTime.UtcNow < _sessionEndUtc && _trades < MaxTradesPerSession)
            {
                var loss = _sandbox.DayLoss();
                if (loss > _worstDayLoss) _worstDayLoss = loss;

                if (loss >= SelfStopUsd)
                {
                    _selfStopped = true;
                    Note("SELF-STOP at $" + Money.Format(loss) + " (limit is $" + SandboxPersonalLimit +
                         "). The bot stops itself; the guardian is never asked.");
                    return;
                }

                if (_sandbox.Status.Kind != StateKind.Armed)
                {
                    Note("!!! the guardian is no longer ARMED (" + _sandbox.Status.Kind +
                         ") - stopping. This is exactly the outcome bot B exists to never cause.");
                    return;
                }

                OneTrade();
                Sleep(MinutesBetweenTrades * 60_000);
            }

            if (_trades >= MaxTradesPerSession) Note("trade budget reached (" + _trades + ")");
            else if (DateTime.UtcNow >= _sessionEndUtc) Note("session boundary reached");
        }

        /// <summary>One ordinary trade: in, a real stop at the venue, out on target, stop or time.</summary>
        private void OneTrade()
        {
            var longSide = _trades % 2 == 0;
            var entryAction = longSide ? OrderAction.Buy : OrderAction.SellShort;

            var entry = Submit(entryAction, OrderType.Market, 0, 0, "entry");
            if (entry == null) return;

            if (!WaitForPosition(TimeSpan.FromSeconds(20)))
            {
                Note("entry did not produce a position within 20s - cancelling and moving on");
                CancelAllOwn();
                FlattenOwn("entry never confirmed");
                return;
            }

            _trades++;
            var avg = AveragePrice();
            var tick = TickSize();
            if (avg <= 0 || tick <= 0)
            {
                // Unknown entry price means we cannot compute a stop. Fail closed: exit now rather
                // than hold a position we cannot protect.
                Note("entry price or tick size unknown - closing immediately rather than holding unprotected");
                CloseNow("no usable entry price");
                return;
            }

            var stopPrice = Round(longSide ? avg - StopTicks * tick : avg + StopTicks * tick);
            var targetPrice = Round(longSide ? avg + TargetTicks * tick : avg - TargetTicks * tick);

            _protectiveStop = Submit(longSide ? OrderAction.Sell : OrderAction.BuyToCover,
                                     OrderType.StopMarket, 0, stopPrice, "protective stop");
            if (_protectiveStop == null)
            {
                Note("the protective stop was refused - closing immediately. A position without its " +
                     "stop is the one thing bot B does not hold.");
                CloseNow("protective stop refused");
                return;
            }

            Note("trade " + _trades + ": " + (longSide ? "long" : "short") + " @ " +
                 avg.ToString(CultureInfo.InvariantCulture) + ", stop " +
                 stopPrice.ToString(CultureInfo.InvariantCulture) + " (venue), target " +
                 targetPrice.ToString(CultureInfo.InvariantCulture) + " (in process)");

            ManageTrade(longSide, targetPrice);
        }

        private void ManageTrade(bool longSide, double targetPrice)
        {
            var deadline = DateTime.UtcNow.AddMinutes(MaxHoldMinutes);

            while (!_stopping && DateTime.UtcNow < deadline)
            {
                if (NetQty() == 0)
                {
                    _stopsFilled++;
                    Note("trade " + _trades + ": the venue stop took it out");
                    CancelOwnStop();
                    return;
                }

                var last = LastPrice();
                if (last > 0 && ((longSide && last >= targetPrice) || (!longSide && last <= targetPrice)))
                {
                    _targetsHit++;
                    Note("trade " + _trades + ": target reached at " + last.ToString(CultureInfo.InvariantCulture));
                    CloseNow("target");
                    return;
                }

                Sleep(1_000);
            }

            if (NetQty() != 0)
            {
                _timeExits++;
                Note("trade " + _trades + ": time exit after " + MaxHoldMinutes + " minutes");
                CloseNow("time");
            }
            else
            {
                _stopsFilled++;
                CancelOwnStop();
            }
        }

        private void CloseNow(string why)
        {
            CancelOwnStop();
            var net = NetQty();
            if (net == 0) return;
            var action = net > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
            var exit = Submit(action, OrderType.Market, 0, 0, "exit (" + why + ")");
            if (exit == null) FlattenOwn("exit order refused (" + why + ")");
        }

        // ------------------------------------------------------------------ order plumbing

        private Order Submit(OrderAction action, OrderType type, double limitPrice, double stopPrice, string label)
        {
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
                // The non-obsolete 12-argument overload. OrderEntry.Automated files the order as
                // machine-sent in NT8's own record, which is where this bot's clean days get read from.
                var order = _account.CreateOrder(
                    _instrument, action, type, OrderEntry.Automated, TimeInForce.Day, Quantity,
                    limitPrice, stopPrice, string.Empty, OrderTag, Core.Globals.MaxDate, null);
                _account.Submit(new[] { order });
                return order;
            }
            catch (Exception ex)
            {
                Note("submit threw (" + label + "): " + ex.Message);
                return null;
            }
        }

        private void Subscribe()
        {
            _account.ExecutionUpdate += OnExecutionUpdate;
            _account.OrderUpdate += OnOrderUpdate;
            Note("subscribed to " + BotSafety.TargetAccount);
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                if (e == null || e.Execution == null) return;
                _sandbox.OnExecution(ExecutionTranslator.Translate(e.Execution));
            }
            catch (Exception ex) { Note("OnExecutionUpdate: " + ex.Message); }
        }

        /// <summary>Two jobs: feed working orders to the guardian the way the production adapter does,
        /// and notice any cancel this bot did not ask for. The second one is the false-positive
        /// detector - it is the number the report exists to publish.</summary>
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
                if (!string.Equals(e.Order.Name, OrderTag, StringComparison.Ordinal)) return;

                var key = KeyOf(e.Order);
                lock (_gate)
                {
                    if (_weCancelled.Contains(key)) return;
                    _cancelledNotByUs.Add(key + " (" + e.Order.OrderAction + ")");
                }
                Note("!!! one of our orders was cancelled and WE did not cancel it: " + key);
            }
            catch (Exception ex) { Note("OnOrderUpdate: " + ex.Message); }
        }

        private void SafeTick()
        {
            try
            {
                _sandbox.Tick();
                var loss = _sandbox.DayLoss();
                if (loss > _worstDayLoss) _worstDayLoss = loss;
            }
            catch (Exception ex) { Note("tick: " + ex.Message); }
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

        private void CancelOwnStop()
        {
            try
            {
                if (_protectiveStop == null) return;
                if (Accounts.IsWorking(_protectiveStop.OrderState))
                {
                    lock (_gate) _weCancelled.Add(KeyOf(_protectiveStop));
                    _account.Cancel(new[] { _protectiveStop });
                }
                _protectiveStop = null;
            }
            catch (Exception ex) { Note("cancel stop threw: " + ex.Message); }
        }

        private void CancelAllOwn()
        {
            try
            {
                var ours = _account.Orders
                    .Where(o => string.Equals(o.Name, OrderTag, StringComparison.Ordinal) &&
                                Accounts.IsWorking(o.OrderState))
                    .ToList();
                if (ours.Count == 0) return;
                foreach (var o in ours) lock (_gate) _weCancelled.Add(KeyOf(o));
                Note("cleanup: cancelling " + ours.Count + " of our own working order(s)");
                _account.Cancel(ours);
            }
            catch (Exception ex) { Note("cleanup cancel threw: " + ex.Message); }
        }

        private void FlattenOwn(string why)
        {
            try
            {
                var instruments = _account.Positions
                    .Where(p => p.Quantity != 0 && p.MarketPosition != MarketPosition.Flat)
                    .Select(p => p.Instrument).Distinct().ToList();
                if (instruments.Count == 0) return;
                Note("flatten (" + why + "): " + instruments.Count + " instrument(s)");
                _account.Flatten(instruments);
            }
            catch (Exception ex) { Note("flatten threw: " + ex.Message); }
        }

        // ------------------------------------------------------------------ helpers

        private bool WaitForPosition(TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (!_stopping && DateTime.UtcNow < deadline)
            {
                if (NetQty() != 0) return true;
                Sleep(200);
            }
            return NetQty() != 0;
        }

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

        private double AveragePrice()
        {
            try
            {
                var p = _account.Positions.FirstOrDefault(x => x.Instrument == _instrument);
                return p == null ? 0 : p.AveragePrice;
            }
            catch { return 0; }
        }

        private double LastPrice()
        {
            try
            {
                if (_instrument.MarketData == null) return 0;
                if (_instrument.MarketData.Last != null) return _instrument.MarketData.Last.Price;
                if (_instrument.MarketData.Bid != null) return _instrument.MarketData.Bid.Price;
                return 0;
            }
            catch { return 0; }
        }

        private double TickSize()
        {
            try { return _instrument.MasterInstrument.TickSize; } catch { return 0; }
        }

        private double Round(double price)
        {
            try { return _instrument.MasterInstrument.RoundToTickSize(price); } catch { return price; }
        }

        private static string KeyOf(Order o)
        {
            try { return o.OrderId ?? o.Id.ToString(CultureInfo.InvariantCulture); }
            catch { return Guid.NewGuid().ToString("N"); }
        }

        private void Sleep(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (!_stopping && DateTime.UtcNow < deadline && DateTime.UtcNow < _sessionEndUtc)
                Thread.Sleep(100);
        }

        private void Abort(string reason) { _abortReason = reason; Note("ABORTED: " + reason); }

        private void Note(string message) { _log.Note(message); }

        // ------------------------------------------------------------------ the report

        private void WriteReport()
        {
            var body = new List<string>();
            var title = "Bot B session " + _startedUtc.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture);

            if (_abortReason != null)
            {
                body.Add("**Aborted before completing: " + _abortReason + "**");
                body.Add("");
                body.Add("Orders placed: " + _budget.Summary() + ".");
                _log.AppendSection(BotPaths.Report("B"), title, body);
                return;
            }

            var interventions = _sandbox.Events()
                .Where(e => InterventionEvents.Contains(e, StringComparer.Ordinal))
                .GroupBy(e => e, StringComparer.Ordinal)
                .Select(g => g.Key + " x" + g.Count())
                .ToList();

            var chain = _sandbox.VerifyChain();
            var stateOk = _sandbox.Status.Kind == StateKind.Armed;
            var unexplainedCancels = _cancelledNotByUs.Count;
            var flat = NetQty() == 0;
            var clean = stateOk && interventions.Count == 0 && unexplainedCancels == 0 &&
                        chain == "OK" && flat;

            body.Add("- account: `Sim101` (Provider proven `Simulator` before anything was sent)");
            body.Add("- guardian limit: personal $" + SandboxPersonalLimit + "; bot self-stop: $" +
                     Money.Format(SelfStopUsd));
            body.Add("- trades: **" + _trades + "** (" + _targetsHit + " target, " + _stopsFilled +
                     " venue stop, " + _timeExits + " time) · orders: " + _budget.Summary());
            body.Add("- worst day loss reached: **$" + Money.Format(_worstDayLoss) + "** — " +
                     PercentOfLimit(_worstDayLoss) + " of the guardian's limit");
            body.Add("- self-stopped before the guardian: " + (_selfStopped ? "**yes**" : "no (never reached the self-stop)"));
            body.Add("");

            body.Add("### Clean session? **" + (clean ? "YES" : "NO") + "**");
            body.Add("");
            body.Add("All five conditions, fixed before the run:");
            body.Add("");
            body.Add("| condition | result |");
            body.Add("|---|---|");
            body.Add("| guardian never left ARMED | " + Mark(stateOk) + " (" + _sandbox.Status.Kind + ") |");
            body.Add("| no intervention event in the ledger | " + Mark(interventions.Count == 0) + " " +
                     (interventions.Count == 0 ? "" : "— " + string.Join(", ", interventions)) + " |");
            body.Add("| every cancel was ours | " + Mark(unexplainedCancels == 0) + " (" +
                     unexplainedCancels + " unexplained) |");
            body.Add("| ledger chain verifies | " + Mark(chain == "OK") + " (" + chain + ") |");
            body.Add("| shut down holding nothing | " + Mark(flat) + " (net " + NetQty() + ") |");
            body.Add("");
            body.Add("Ledger events: " + _sandbox.EventsSummary());

            _log.AppendSection(BotPaths.Report("B"), title, body);
        }

        private static string Mark(bool ok) { return ok ? "PASS" : "**FAIL**"; }

        private static string PercentOfLimit(decimal loss)
        {
            decimal limit;
            if (!Money.TryParse(SandboxPersonalLimit, out limit) || limit <= 0m) return "?";
            return Math.Round(loss * 100m / limit, 1).ToString(CultureInfo.InvariantCulture) + "%";
        }
    }
}
