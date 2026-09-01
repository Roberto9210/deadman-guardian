// The sound channel: what the guardian can establish about its own alert, and what it may say.
//
// WHY THESE TESTS HAVE TO SIMULATE. Roberto's channel is HEALTHY - volume 50, Announcement.wav
// present, all thirteen configured files on disk (read out of Config.xml, 2026-08-31). So the fix
// will work for him from day one AND THE DEVELOPMENT MACHINE WILL NEVER PRODUCE THE BROKEN CASE ON
// ITS OWN. That is an entry in docs/conditions-this-machine-cannot-produce.md, and this file is what
// that entry demands: a test that fabricates the degraded channel, in all three of its shapes.
//
// The third shape is the one worth naming: A NON-EMPTY PATH POINTING AT A FILE THAT IS NOT THERE. It
// is worse than an empty setting, because it LOOKS configured - the plausible default lying, which
// is the family that produced "your limit is $0.00".
//
// WHAT IS NOT TESTED HERE AND CANNOT BE: whether a sound is heard. Not the Windows master volume,
// not a mute in the per-application mixer, not whether speakers are plugged in, not whether anyone
// is in the room. Audibility is unobservable from inside, which is exactly why the acknowledgement -
// still blocked on the extension contract with Ventana B - is the only thing that would close it.

using System;
using GuardianCore;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;
using Xunit;

namespace GuardianCore.Tests
{
    public class Snd1_SoundChannelTests
    {
        private static readonly Func<string, bool> Present = _ => true;
        private static readonly Func<string, bool> Absent = _ => false;
        private const string Path = @"C:\Program Files\NinjaTrader 8\sounds\Announcement.wav";

        // ------------------------------------------------------------------ the assessment

        [Fact]
        public void Snd1a_a_configured_channel_with_its_file_present_is_healthy()
        {
            Assert.Equal(SoundChannelHealth.Healthy, SoundChannel.Assess(50d, Path, Present));
        }

        [Fact]
        public void Snd1b_volume_at_zero_is_muted()
        {
            Assert.Equal(SoundChannelHealth.Muted, SoundChannel.Assess(0d, Path, Present));
        }

        /// <summary>THE PLAUSIBLE DEFAULT LYING. A path that is set and points at nothing looks
        /// configured, which makes it worse than an empty one - the same shape as "$0.00" printed
        /// where a limit was unknown.</summary>
        [Fact]
        public void Snd1c_a_path_that_points_at_nothing_is_not_the_same_as_no_path()
        {
            Assert.Equal(SoundChannelHealth.FileMissing, SoundChannel.Assess(50d, Path, Absent));
            Assert.Equal(SoundChannelHealth.NotConfigured, SoundChannel.Assess(50d, "", Present));
            Assert.Equal(SoundChannelHealth.NotConfigured, SoundChannel.Assess(50d, null, Present));
        }

        /// <summary>Unknown is a real answer and is never collapsed into Healthy. Failing to read a
        /// setting is not evidence that the setting is fine, and the optimistic default is the one
        /// this project refuses everywhere else.</summary>
        [Fact]
        public void Snd1d_an_unreadable_setting_is_unknown_and_never_healthy()
        {
            Assert.Equal(SoundChannelHealth.Unknown, SoundChannel.Assess(null, Path, Present));
            Assert.Equal(SoundChannelHealth.Unknown, SoundChannel.Assess(50d, Path, null));
            Assert.Equal(SoundChannelHealth.Unknown,
                         SoundChannel.Assess(50d, Path, _ => throw new InvalidOperationException("io")));
        }

        // ------------------------------------------------------------------ it CHOOSES, not only reports

        /// <summary>What turns the check from a status line into a backup: NinjaTrader's own
        /// PlaySound respects the trader's configuration and can be broken by it; SystemSounds
        /// ignores that configuration and almost always makes a noise. Respect what was configured
        /// while it can be trusted; have a way out when it cannot - rather than respecting it into
        /// silence.</summary>
        [Fact]
        public void Snd1e_a_healthy_channel_is_used_and_every_other_state_falls_back()
        {
            Assert.False(SoundChannel.UseFallback(SoundChannelHealth.Healthy));

            foreach (var h in new[] { SoundChannelHealth.Muted, SoundChannelHealth.FileMissing,
                                      SoundChannelHealth.NotConfigured, SoundChannelHealth.Unknown })
                Assert.True(SoundChannel.UseFallback(h), h.ToString());
        }

        // ------------------------------------------------------------------ the cadence

        /// <summary>Immediate, then every five minutes, flat. The cadence is what keeps a future
        /// acknowledgement honest: a sound every minute is an alarm, and the button that silences an
        /// alarm is pressed by reflex.</summary>
        [Fact]
        public void Snd1f_the_first_alert_is_immediate_and_the_rest_are_five_minutes_apart()
        {
            Assert.True(SoundChannel.ShouldSoundNow(false, 0, 0));            // nothing waits

            Assert.False(SoundChannel.ShouldSoundNow(true, 1000, 1000 + 60000));    // one minute: no
            Assert.False(SoundChannel.ShouldSoundNow(true, 1000, 1000 + 299999));   // just short: no
            Assert.True(SoundChannel.ShouldSoundNow(true, 1000, 1000 + 300000));    // five minutes: yes
        }

        /// <summary>Flat forever, never escalating and never switching itself off. The sound's real
        /// job is to reach someone who is NOT THERE - away three hours, it catches them on return -
        /// so a bounded alert loses exactly the case it was built for. Its ceiling is the session.</summary>
        [Fact]
        public void Snd1g_it_neither_escalates_nor_gives_up()
        {
            var last = 0L;
            for (var hour = 1; hour <= 8; hour++)
            {
                var now = hour * 3600000L;
                Assert.True(SoundChannel.ShouldSoundNow(true, last, now));
                last = now;
            }
            // and the interval never changes - there is no escalation to inspect
            Assert.Equal(300000, SoundChannel.RepeatEveryMs);
        }

        // ------------------------------------------------------------------ what it may SAY

        /// <summary>THE CONTAINMENT, and it is the whole design: the text says what was CHECKED,
        /// never what was concluded. Reading SoundVolume establishes the configuration, not that
        /// anyone heard - volume at 50 and an inaudible alert are perfectly compatible.
        ///
        /// It would be this house's defect class debuting inside the function built to fix it.</summary>
        [Fact]
        public void Snd1h_the_text_never_claims_delivery_and_never_promises_the_fallback()
        {
            var forbidden = new[]
            {
                "I warned you", "you were notified", "you will hear", "you heard",
                "the audio channel works", "the sound was delivered", "alert delivered",
                "successfully", "notified you",
            };

            foreach (var h in new[] { SoundChannelHealth.Muted, SoundChannelHealth.FileMissing,
                                      SoundChannelHealth.NotConfigured, SoundChannelHealth.Unknown })
            {
                var m = Messages.DetailSoundChannel(h);
                Assert.False(string.IsNullOrEmpty(m), h.ToString());
                foreach (var phrase in forbidden)
                    Assert.False(m.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0,
                                 "'" + phrase + "' in: " + m);

                // and it says the limit out loud rather than leaving it to be assumed
                Assert.Contains("cannot tell whether you hear", m, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>A healthy channel says NOTHING. A line that appears every time is a line nobody
        /// reads, which is the lesson the 165-message storm already paid for.</summary>
        [Fact]
        public void Snd1i_a_healthy_channel_produces_no_line_at_all()
        {
            Assert.Null(Messages.DetailSoundChannel(SoundChannelHealth.Healthy));
        }

        /// <summary>Each degraded state says WHICH check failed, because "something is wrong with
        /// your sound" sends a reader to look in the wrong place.</summary>
        [Fact]
        public void Snd1j_the_text_names_the_check_that_failed()
        {
            Assert.Contains("volume", Messages.DetailSoundChannel(SoundChannelHealth.Muted),
                            StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not on disk", Messages.DetailSoundChannel(SoundChannelHealth.FileMissing),
                            StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no alert sound configured",
                            Messages.DetailSoundChannel(SoundChannelHealth.NotConfigured),
                            StringComparison.OrdinalIgnoreCase);
            Assert.Contains("could not read", Messages.DetailSoundChannel(SoundChannelHealth.Unknown),
                            StringComparison.OrdinalIgnoreCase);
        }
    }
}
