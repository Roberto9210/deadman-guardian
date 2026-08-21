// deadman-guardian — Step 3 platform probe.
//
// READ-ONLY. This AddOn places no orders, cancels nothing, flattens nothing and opens no socket.
// Its only job is to answer, from inside the real NinjaTrader process, the questions SPEC v0.4 left
// marked as "verify in Step 3":
//
//   1. Does the IANA -> Windows time zone fallback of SPEC 5.1 actually work in here?
//   2. How do the candidate monotonic clock sources behave, and what does the wall clock do
//      relative to them (SPEC 6.4, and the suspend question of SPEC 17.2)?
//   3. What is the real AddOn lifecycle, with timestamps (SPEC 3.3)?
//   4. Is there any pre-submit interception point at runtime (SPEC 3.3, 9.5)?
//   5. What is the observed latency from "NT8 raises the order event" to "a decision could be taken"?
//
// It writes one report file and appends a JSONL trace. Nothing else leaves this class.
//
// Deployed to: Documents\NinjaTrader 8\bin\Custom\AddOns\DeadmanGuardianProbe.cs
// Source of truth: <repo>/nt/probe/DeadmanGuardianProbe.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class DeadmanGuardianProbe : AddOnBase
    {
        private const string GuardedAccount = "Sim101";   // Sim101 ONLY. Never a live account.
        private static readonly string OutDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "NinjaTrader 8", "deadman-guardian-probe");

        private static readonly string ReportPath = Path.Combine(OutDir, "probe_report.md");
        private static readonly string TracePath = Path.Combine(OutDir, "probe_trace.jsonl");

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private readonly List<string> _lifecycle = new List<string>();
        private Timer _timer;
        private long _ticks;
        private DateTime _wallAtStart;
        private long _swAtStart;
        private ulong _tickCountAtStart;
        private readonly object _gate = new object();

        // latency sampling: NT8 raises the event -> we are able to decide
        private readonly List<double> _orderEventLatencyMs = new List<double>();
        private readonly List<double> _execEventLatencyMs = new List<double>();
        private int _ordersSeen, _execsSeen;

        protected override void OnStateChange()
        {
            Note("OnStateChange -> " + State);

            if (State == State.SetDefaults)
            {
                Name = "deadman-guardian probe (read-only)";
            }
            else if (State == State.Configure)
            {
                Directory.CreateDirectory(OutDir);
                _wallAtStart = DateTime.UtcNow;
                _swAtStart = Stopwatch.GetTimestamp();
                _tickCountAtStart = GetTickCount64();

                Subscribe();
                WriteReport("Configure");

                // 30 s cadence: enough samples to see clock drift without becoming a busy loop.
                _timer = new Timer(_ => Tick(), null, 30_000, 30_000);
            }
            else if (State == State.Terminated)
            {
                try { _timer?.Dispose(); } catch { }
                Unsubscribe();
                WriteReport("Terminated");
            }
        }

        // ---------- subscriptions (read-only) ----------

        private Account _account;

        private void Subscribe()
        {
            try
            {
                _account = Account.All.FirstOrDefault(a =>
                    string.Equals(a.Name, GuardedAccount, StringComparison.OrdinalIgnoreCase));

                Note("Account.All = [" + string.Join(", ", Account.All.Select(a => a.Name)) + "]");
                if (_account == null)
                {
                    Note("account '" + GuardedAccount + "' NOT FOUND at Configure");
                    return;
                }

                Note("found " + _account.Name +
                     " connection=" + SafeConnection(_account) +
                     " denomination=" + _account.Denomination);

                _account.OrderUpdate += OnOrderUpdate;
                _account.ExecutionUpdate += OnExecutionUpdate;
                _account.PositionUpdate += OnPositionUpdate;
                _account.AccountItemUpdate += OnAccountItemUpdate;
                Note("subscribed to OrderUpdate, ExecutionUpdate, PositionUpdate, AccountItemUpdate");
            }
            catch (Exception ex) { Note("Subscribe FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void Unsubscribe()
        {
            try
            {
                if (_account == null) return;
                _account.OrderUpdate -= OnOrderUpdate;
                _account.ExecutionUpdate -= OnExecutionUpdate;
                _account.PositionUpdate -= OnPositionUpdate;
                _account.AccountItemUpdate -= OnAccountItemUpdate;
            }
            catch { }
        }

        private static string SafeConnection(Account a)
        {
            try { return a.Connection == null ? "null" : a.ConnectionStatus.ToString(); }
            catch (Exception ex) { return "err:" + ex.GetType().Name; }
        }

        // ---------- event handlers: measure, never act ----------

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            var t0 = Stopwatch.GetTimestamp();
            try
            {
                var eventTime = e.Time;                       // NT8's timestamp for the event
                var now = DateTime.Now;
                var lagMs = (now - eventTime).TotalMilliseconds;
                var handlerMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

                lock (_gate)
                {
                    _ordersSeen++;
                    _orderEventLatencyMs.Add(lagMs);
                }

                Trace("ORDER_OBSERVED", new Dictionary<string, string>
                {
                    { "orderId", Safe(() => e.Order?.OrderId) },
                    { "state", Safe(() => e.OrderState.ToString()) },
                    { "instrument", Safe(() => e.Order?.Instrument?.FullName) },
                    { "eventTimeLocal", eventTime.ToString("O", CultureInfo.InvariantCulture) },
                    { "observedLocal", now.ToString("O", CultureInfo.InvariantCulture) },
                    { "eventToObservedMs", lagMs.ToString("F3", CultureInfo.InvariantCulture) },
                    { "handlerEntryMs", handlerMs.ToString("F3", CultureInfo.InvariantCulture) },
                });
            }
            catch (Exception ex) { Note("OnOrderUpdate FAILED: " + ex.Message); }
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                var now = DateTime.Now;
                var lagMs = (now - e.Execution.Time).TotalMilliseconds;
                lock (_gate) { _execsSeen++; _execEventLatencyMs.Add(lagMs); }

                // The point value question of SPEC 5.7: is the metadata actually there?
                Trace("EXECUTION_OBSERVED", new Dictionary<string, string>
                {
                    { "instrument", Safe(() => e.Execution.Instrument.FullName) },
                    { "pointValue", Safe(() => e.Execution.Instrument.MasterInstrument.PointValue
                                                 .ToString(CultureInfo.InvariantCulture)) },
                    { "tickSize", Safe(() => e.Execution.Instrument.MasterInstrument.TickSize
                                                 .ToString(CultureInfo.InvariantCulture)) },
                    { "commission", Safe(() => e.Execution.Commission.ToString(CultureInfo.InvariantCulture)) },
                    { "quantity", Safe(() => e.Execution.Quantity.ToString(CultureInfo.InvariantCulture)) },
                    { "price", Safe(() => e.Execution.Price.ToString(CultureInfo.InvariantCulture)) },
                    { "eventToObservedMs", lagMs.ToString("F3", CultureInfo.InvariantCulture) },
                });
            }
            catch (Exception ex) { Note("OnExecutionUpdate FAILED: " + ex.Message); }
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            Trace("POSITION_OBSERVED", new Dictionary<string, string>
            {
                { "instrument", Safe(() => e.Position?.Instrument?.FullName) },
                { "quantity", Safe(() => e.Quantity.ToString(CultureInfo.InvariantCulture)) },
                { "marketPosition", Safe(() => e.MarketPosition.ToString()) },
            });
        }

        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            Trace("ACCOUNT_ITEM", new Dictionary<string, string>
            {
                { "item", Safe(() => e.AccountItem.ToString()) },
                { "value", Safe(() => e.Value.ToString(CultureInfo.InvariantCulture)) },
                { "currency", Safe(() => e.Currency.ToString()) },
            });
        }

        // ---------- periodic clock sampling ----------

        private void Tick()
        {
            try
            {
                _ticks++;
                var wall = DateTime.UtcNow;
                var sw = Stopwatch.GetTimestamp();
                var tc = GetTickCount64();

                var wallMs = (wall - _wallAtStart).TotalMilliseconds;
                var swMs = (sw - _swAtStart) * 1000.0 / Stopwatch.Frequency;
                var tcMs = (double)(tc - _tickCountAtStart);

                Trace("CLOCK_SAMPLE", new Dictionary<string, string>
                {
                    { "tick", _ticks.ToString(CultureInfo.InvariantCulture) },
                    { "wallElapsedMs", wallMs.ToString("F0", CultureInfo.InvariantCulture) },
                    { "stopwatchElapsedMs", swMs.ToString("F0", CultureInfo.InvariantCulture) },
                    { "getTickCount64ElapsedMs", tcMs.ToString("F0", CultureInfo.InvariantCulture) },
                    { "wallMinusStopwatchMs", (wallMs - swMs).ToString("F0", CultureInfo.InvariantCulture) },
                    { "wallMinusTickCountMs", (wallMs - tcMs).ToString("F0", CultureInfo.InvariantCulture) },
                });

                if (_ticks % 10 == 0) WriteReport("tick " + _ticks);
            }
            catch (Exception ex) { Note("Tick FAILED: " + ex.Message); }
        }

        // ---------- the report ----------

        private void WriteReport(string reason)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# deadman-guardian — Step 3 platform probe report");
                sb.AppendLine();
                sb.AppendLine("Written from inside the NinjaTrader process. Read-only probe: it places no orders,");
                sb.AppendLine("cancels nothing and opens no socket.");
                sb.AppendLine();
                sb.AppendLine("- Written because: **" + reason + "**");
                sb.AppendLine("- Local time: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                sb.AppendLine("- UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                sb.AppendLine("- Process: " + Process.GetCurrentProcess().ProcessName +
                              " pid " + Process.GetCurrentProcess().Id);
                sb.AppendLine("- Runtime: " + RuntimeInformation.FrameworkDescription);
                sb.AppendLine("- CLR: " + Environment.Version);
                sb.AppendLine("- OS: " + RuntimeInformation.OSDescription);
                sb.AppendLine("- NinjaTrader.Core: " + SafeAssemblyVersion("NinjaTrader.Core"));
                sb.AppendLine("- Machine time zone: " + TimeZoneInfo.Local.Id + " (" + TimeZoneInfo.Local.DisplayName + ")");
                sb.AppendLine();

                sb.AppendLine("## 1. Time zone resolution inside NT8 (SPEC §5.1)");
                sb.AppendLine();
                sb.AppendLine("| id tried | result |");
                sb.AppendLine("|---|---|");
                foreach (var id in new[] { "America/Chicago", "America/New_York", "UTC",
                                           "Central Standard Time", "Eastern Standard Time" })
                    sb.AppendLine("| `" + id + "` | " + TryZone(id) + " |");
                sb.AppendLine();
                sb.AppendLine("DST check with the Windows id, the two dates SPEC §5.1 pins:");
                sb.AppendLine();
                sb.AppendLine("| local 17:00 CT | UTC | daylight? |");
                sb.AppendLine("|---|---|---|");
                foreach (var d in new[] { new DateTime(2026, 3, 9, 17, 0, 0), new DateTime(2026, 11, 2, 17, 0, 0) })
                    sb.AppendLine("| " + d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " | " + ToUtcVia(d) + " |");
                sb.AppendLine();

                sb.AppendLine("## 2. Clock sources (SPEC §6.4, §17.2)");
                sb.AppendLine();
                sb.AppendLine("- `Environment.TickCount64` present on this runtime: **" + HasTickCount64() + "**");
                sb.AppendLine("- `Stopwatch.IsHighResolution`: " + Stopwatch.IsHighResolution +
                              ", Frequency " + Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("- samples taken: " + _ticks + " (every 30 s; see `probe_trace.jsonl`)");
                var wallMsNow = (DateTime.UtcNow - _wallAtStart).TotalMilliseconds;
                var swMsNow = (Stopwatch.GetTimestamp() - _swAtStart) * 1000.0 / Stopwatch.Frequency;
                var tcMsNow = (double)(GetTickCount64() - _tickCountAtStart);
                sb.AppendLine("- since Configure: wall " + wallMsNow.ToString("F0", CultureInfo.InvariantCulture) +
                              " ms, Stopwatch " + swMsNow.ToString("F0", CultureInfo.InvariantCulture) +
                              " ms, GetTickCount64 " + tcMsNow.ToString("F0", CultureInfo.InvariantCulture) + " ms");
                sb.AppendLine("- wall − Stopwatch: " + (wallMsNow - swMsNow).ToString("F0", CultureInfo.InvariantCulture) +
                              " ms · wall − GetTickCount64: " + (wallMsNow - tcMsNow).ToString("F0", CultureInfo.InvariantCulture) + " ms");
                sb.AppendLine();
                sb.AppendLine("**Suspend test (needs a human):** hibernate or sleep the machine with NT8 running,");
                sb.AppendLine("resume, and read the next `CLOCK_SAMPLE` rows. A source that keeps counting through");
                sb.AppendLine("suspend leaves `wallMinus…Ms` near zero; one that stops shows a jump equal to the");
                sb.AppendLine("suspended duration. SPEC §7.5 is correct either way — only the size of the logged");
                sb.AppendLine("divergence changes — but the number belongs in the record.");
                sb.AppendLine();

                sb.AppendLine("## 3. AddOn lifecycle (SPEC §3.3)");
                sb.AppendLine();
                sb.AppendLine("```");
                lock (_gate) foreach (var l in _lifecycle) sb.AppendLine(l);
                sb.AppendLine("```");
                sb.AppendLine();

                sb.AppendLine("## 4. Pre-submit interception (SPEC §3.3, §9.5)");
                sb.AppendLine();
                sb.AppendLine(PreSubmitScan());
                sb.AppendLine();

                sb.AppendLine("## 5. Observed event latency");
                sb.AppendLine();
                sb.AppendLine("Time from the timestamp NT8 puts on the event to the moment this handler could act.");
                sb.AppendLine("It is the *detect* half of detect-and-cancel; the cancel round-trip is measured in");
                sb.AppendLine("Stage B, when the guardian is wired and allowed to cancel on Sim101.");
                sb.AppendLine();
                lock (_gate)
                {
                    sb.AppendLine("- order events seen: **" + _ordersSeen + "**" + Stats(_orderEventLatencyMs));
                    sb.AppendLine("- execution events seen: **" + _execsSeen + "**" + Stats(_execEventLatencyMs));
                }
                if (_ordersSeen == 0)
                    sb.AppendLine("- _no order has been placed on " + GuardedAccount + " while this probe was running._");
                sb.AppendLine();

                File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(OutDir, "probe_error.txt"),
                        DateTime.UtcNow.ToString("O") + " " + ex + Environment.NewLine); } catch { }
            }
        }

        private static string Stats(List<double> xs)
        {
            if (xs.Count == 0) return "";
            var sorted = xs.OrderBy(x => x).ToList();
            double P(double q) => sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];
            return " — min " + sorted.First().ToString("F1", CultureInfo.InvariantCulture) +
                   " ms, median " + P(0.5).ToString("F1", CultureInfo.InvariantCulture) +
                   " ms, p95 " + P(0.95).ToString("F1", CultureInfo.InvariantCulture) +
                   " ms, max " + sorted.Last().ToString("F1", CultureInfo.InvariantCulture) + " ms";
        }

        private static string PreSubmitScan()
        {
            try
            {
                var asm = typeof(Account).Assembly;
                var hits = new List<string>();
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                {
                    EventInfo[] evs;
                    try { evs = t.GetEvents(); } catch { continue; }
                    foreach (var e in evs)
                    {
                        var n = e.Name;
                        if (n.IndexOf("Submit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Validat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Approv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Intercept", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.StartsWith("Before", StringComparison.OrdinalIgnoreCase))
                            hits.Add(t.FullName + "." + n);
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("Scanned **" + types.Length + "** types in `" + asm.GetName().Name +
                              " " + asm.GetName().Version + "` at runtime for an event that could veto an order");
                sb.AppendLine("before submission (`Submit*`, `Validat*`, `Approv*`, `Intercept*`, `Before*`).");
                sb.AppendLine();
                sb.AppendLine(hits.Count == 0
                    ? "**Result: none.** Enforcement stays detect-and-cancel, as SPEC §9.5 specifies."
                    : "**Result: " + hits.Count + " candidate(s):** " + string.Join(", ", hits.Distinct()));
                return sb.ToString();
            }
            catch (Exception ex) { return "scan failed: " + ex.Message; }
        }

        private static string TryZone(string id)
        {
            try
            {
                var z = TimeZoneInfo.FindSystemTimeZoneById(id);
                return "OK → `" + z.Id + "` (offset now " +
                       z.GetUtcOffset(DateTime.UtcNow).ToString() + ")";
            }
            catch (TimeZoneNotFoundException) { return "**TimeZoneNotFoundException**"; }
            catch (Exception ex) { return "**" + ex.GetType().Name + "**: " + ex.Message; }
        }

        private static string ToUtcVia(DateTime local)
        {
            try
            {
                var z = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), z);
                return utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z | " +
                       z.IsDaylightSavingTime(local);
            }
            catch (Exception ex) { return "failed: " + ex.Message + " | -"; }
        }

        private static string HasTickCount64()
        {
            var p = typeof(Environment).GetProperty("TickCount64",
                        BindingFlags.Public | BindingFlags.Static);
            return p == null ? "NO (only TickCount:Int32, wraps at 24.9 days)" : "yes";
        }

        private static string SafeAssemblyVersion(string name)
        {
            try
            {
                var a = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(x => x.GetName().Name == name);
                return a == null ? "not loaded" : a.GetName().Version.ToString();
            }
            catch { return "?"; }
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch (Exception ex) { return "err:" + ex.GetType().Name; }
        }

        private void Note(string line)
        {
            var stamped = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line;
            lock (_gate) _lifecycle.Add(stamped);
            Trace("NOTE", new Dictionary<string, string> { { "line", line } });
        }

        private void Trace(string ev, Dictionary<string, string> fields)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                var sb = new StringBuilder();
                sb.Append("{\"tsUtc\":\"").Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                  .Append("\",\"event\":\"").Append(ev).Append("\"");
                foreach (var kv in fields)
                    sb.Append(",\"").Append(kv.Key).Append("\":\"")
                      .Append((kv.Value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"");
                sb.Append("}");
                File.AppendAllText(TracePath, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { /* the probe must never take NT8 down */ }
        }
    }
}
