using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G20: a lockout has no manual exit before expiry.
    /// G21: money never touches double anywhere on Core's public surface.
    /// G22: Core references no NinjaTrader assembly.</summary>
    public class G20_G21_G22_MetaTests : Harness
    {
        [Fact]
        public void G20_no_public_entry_point_can_end_a_lockout_early()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);

            // Everything a determined trader can reach from outside.
            Assert.False(Guardian.Arm(Config("600.00")).Ok);
            Assert.False(Guardian.Arm(Config("100.00")).Ok);
            Assert.False(Guardian.TryChangeConfig(Config("100.00")).Ok);
            Guardian.OnConfigFileObserved(Config("600.00"));
            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o1", Instrument, "Buy"));
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, PointValue, "x1"));
            for (int i = 0; i < 50; i++) Guardian.Tick();

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);
        }

        [Fact]
        public void G20_restarting_the_process_does_not_end_a_lockout()
        {
            Armed("600.00");
            LoseExactly(600.00m);

            for (int i = 2; i <= 5; i++)
            {
                var restarted = NewGuardian("run-" + i);
                Assert.Equal(StateKind.Locked, restarted.Status.Kind);
            }
        }

        [Fact]
        public void G20_the_lockout_ends_only_when_the_seal_expires_and_then_needs_a_deliberate_re_arm()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);

            Clock.Advance(TimeSpan.FromHours(2));   // honest time to 17:00 CT
            Guardian.Tick();

            Assert.Equal(StateKind.Disarmed, Guardian.Status.Kind);
            Assert.True(HasEvent(Ev.SealExpired));
            Assert.True(HasEvent(Ev.LockoutCleared));
            Assert.Null(Guardian.Status.SealHash);

            // Disarmed is not protected: arming again is an act the trader has to take.
            Assert.True(Guardian.Arm(Config("600.00")).Ok);
            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
        }

        [Fact]
        public void G21_no_double_or_float_appears_anywhere_on_the_public_surface_of_core()
        {
            var assembly = typeof(Guardian).Assembly;
            var offenders = new List<string>();

            foreach (var type in assembly.GetExportedTypes())
            {
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (IsFloating(p.PropertyType)) offenders.Add(type.Name + "." + p.Name + " : " + p.PropertyType.Name);

                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (IsFloating(f.FieldType)) offenders.Add(type.Name + "." + f.Name + " : " + f.FieldType.Name);

                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (IsFloating(m.ReturnType)) offenders.Add(type.Name + "." + m.Name + " returns " + m.ReturnType.Name);
                    foreach (var arg in m.GetParameters())
                        if (IsFloating(arg.ParameterType)) offenders.Add(type.Name + "." + m.Name + "(" + arg.Name + " : " + arg.ParameterType.Name + ")");
                }

                foreach (var c in type.GetConstructors())
                    foreach (var arg in c.GetParameters())
                        if (IsFloating(arg.ParameterType)) offenders.Add(type.Name + " ctor(" + arg.Name + " : " + arg.ParameterType.Name + ")");
            }

            Assert.True(offenders.Count == 0,
                "money must be decimal everywhere (SPEC 4 rule 7); found: " + string.Join(", ", offenders));
        }

        private static bool IsFloating(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u == typeof(double) || u == typeof(float);
        }

        [Fact]
        public void G22_core_references_no_ninjatrader_assembly()
        {
            var assembly = typeof(Guardian).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

            Assert.DoesNotContain(referenced, name => name.IndexOf("NinjaTrader", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(referenced, name => name.IndexOf("NinjaScript", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void G22_core_takes_no_package_dependencies_beyond_the_base_class_library()
        {
            var allowed = new[] { "netstandard", "mscorlib", "System", "System.Core", "System.Runtime" };
            var referenced = typeof(Guardian).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

            var unexpected = referenced
                .Where(r => !allowed.Contains(r, StringComparer.OrdinalIgnoreCase) && !r.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(unexpected.Count == 0, "unexpected dependency: " + string.Join(", ", unexpected));
        }

        [Fact]
        public void G22_core_opens_no_network_types_on_its_surface()
        {
            // SPEC 13: no telemetry, no cloud, no licence check. The add-on opens no socket.
            var assembly = typeof(Guardian).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.DoesNotContain(referenced, n => n.IndexOf("System.Net", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(referenced, n => n.IndexOf("Http", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Ledger_verifies_after_every_scenario_in_this_suite()
        {
            // The chain is not a decoration: it has to survive the messy paths too.
            Armed("600.00");
            Guardian.TryChangeConfig(Config("900.00"));
            Clock.Advance(TimeSpan.FromMinutes(5));
            Guardian.Tick();
            LoseExactly(600.00m);
            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o1", Instrument, "Buy"));
            var restarted = NewGuardian("run-2");
            restarted.Tick();

            Assert.True(new Ledger(Store, LedgerPath).Verify().Ok);
            Assert.True(restarted.VerifyLedger().Ok);
        }

        [Fact]
        public void The_whole_day_reads_as_a_story_in_the_ledger()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Clock.Advance(TimeSpan.FromHours(2));
            Guardian.Tick();

            var events = Events();
            var expectedOrder = new[]
            {
                Ev.GuardianStarted, Ev.ConfigLoaded, Ev.Armed, Ev.SealCreated, Ev.DayOpened,
                Ev.LimitBreached, Ev.OrdersCancelled, Ev.FlattenRequested, Ev.FlattenVerified,
                Ev.SealExpired, Ev.LockoutCleared, Ev.DayClosed, Ev.Disarmed
            };

            var positions = expectedOrder.Select(e => events.IndexOf(e)).ToList();
            Assert.All(positions.Select((p, i) => new { p, name = expectedOrder[i] }),
                       x => Assert.True(x.p >= 0, "missing event: " + x.name));
            Assert.Equal(positions.OrderBy(p => p).ToList(), positions);
        }
    }
}
