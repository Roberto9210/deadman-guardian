// Whether the guardian's own alert channel is alive - the decision, extracted so it can be
// interrogated without NinjaTrader and without a speaker.
//
// THIS FILE MUST NEVER REFERENCE NINJATRADER. Same move as GuardedAccountRule, BotAccountRule and
// PanelPlacement, for the same reason: the code that decides and the code that consults the world
// must not be the same code.
//
// WHY IT EXISTS. On 2026-08-26 the guardian asked for help 165 times and the person it was asking
// answered, five days later, "no me di cuenta" - Announce writes to a Log tab he does not read. Of
// the product's three messages only the second ever reached him, and it reached him through the
// panel. So a sound was added; and a sound has the same hole as a log line unless somebody checks
// whether the channel is even switched on.
//
// WHAT CAN BE CHECKED, verified by out-of-process reflection over NinjaTrader.Core.dll on 2026-08-31:
//
//     NinjaTrader.Core.Globals.GeneralOptions          public static
//         .SoundVolume        Double
//         .SoundAnnouncement  String   <- the file SoundType.Announcement plays
//
// So the guardian CAN know that its own channel is muted or points at a file that is not there.
//
// AND WHAT CANNOT, WHICH IS ALMOST EVERYTHING THAT DECIDES AUDIBILITY: the Windows master volume,
// whether NinjaTrader is muted in the per-application mixer, whether speakers are connected, which
// device the audio leaves by, and whether there is anyone in the room. AUDIBILITY IS UNOBSERVABLE
// FROM INSIDE, under either answer to the unresolved question of how SystemSounds is routed.
//
// That is the whole reason the ACKNOWLEDGEMENT is not a channel improvement but the only mechanism
// that closes the gap: it is the only one that produces a signal FROM the person instead of TOWARD
// them. This file makes the attempt better; it cannot make it verifiable.

using System;
using GuardianCore;

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    public static class SoundChannel
    {
        /// <summary>What the guardian can establish about NinjaTrader's own sound channel, from
        /// NinjaTrader's own settings. Never about whether a human heard anything.
        ///
        /// THE FILE IS CHECKED, NOT ONLY THE SETTING. SoundAnnouncement is a String, and a non-empty
        /// path pointing at a file that does not exist is a plausible default lying - WORSE than an
        /// empty string, because it looks configured. That is why FileMissing and NotConfigured are
        /// separate answers rather than one "bad path".
        ///
        /// Unknown is a real answer and is never collapsed into Healthy: failing to read a setting is
        /// not evidence that the setting is fine, and the optimistic default is the one this project
        /// refuses everywhere.</summary>
        public static SoundChannelHealth Assess(double? volume, string announcementPath,
                                                Func<string, bool> fileExists)
        {
            if (!volume.HasValue || fileExists == null) return SoundChannelHealth.Unknown;
            if (volume.Value <= 0d) return SoundChannelHealth.Muted;
            if (string.IsNullOrWhiteSpace(announcementPath)) return SoundChannelHealth.NotConfigured;

            bool exists;
            try { exists = fileExists(announcementPath); }
            catch { return SoundChannelHealth.Unknown; }

            return exists ? SoundChannelHealth.Healthy : SoundChannelHealth.FileMissing;
        }

        /// <summary>Which route to take. The health check does not only REPORT - it CHOOSES, and that
        /// is what turns it from a status line into a backup.
        ///
        /// NinjaTrader's own PlaySound respects the trader's configuration, and can therefore be
        /// broken by it. SystemSounds ignores that configuration, and therefore almost always makes a
        /// noise. Picking one in advance means accepting its defect; the health check is exactly the
        /// datum that allows deciding in the moment - respect what the trader configured while it can
        /// be trusted, and have a way out when it cannot, instead of respecting it into silence.
        ///
        /// Unknown falls back too: an unreadable setting is not a working channel.</summary>
        public static bool UseFallback(SoundChannelHealth health)
        {
            return health != SoundChannelHealth.Healthy;
        }

        /// <summary>Immediate, then every five minutes, flat, for as long as the condition holds.
        ///
        /// THE CADENCE IS WHAT KEEPS AN ACKNOWLEDGEMENT HONEST. A sound every minute is an alarm, and
        /// the button that silences an alarm is pressed by reflex - so the ack would degrade into
        /// "make it stop" and mean nothing. At five minutes each repetition arrives after a return to
        /// baseline and is processed as new information, so a deliberate acknowledgement is possible.
        ///
        /// The first one is IMMEDIATE: the condition has just arisen and the first alert has no
        /// reason to wait.
        ///
        /// Indefinite rather than bounded, because the sound's real job is to reach someone WHO IS
        /// NOT THERE - away three hours, it catches them on return. A sound that switches itself off
        /// loses exactly the case it was built for. Its natural ceiling is the session: the condition
        /// resolves or the seal expires.
        ///
        /// FLAT, never escalating. "Do not escalate" is the simplest ceiling there is, an unbounded
        /// escalation rebuilds the 165-message storm with a loudspeaker, and escalating into a dead
        /// channel fixes nothing - what helps there is SAYING SO.</summary>
        public const long RepeatEveryMs = 300000;

        public static bool ShouldSoundNow(bool everSounded, long lastSoundedMonotonicMs,
                                          long nowMonotonicMs)
        {
            if (!everSounded) return true;
            return nowMonotonicMs - lastSoundedMonotonicMs >= RepeatEveryMs;
        }

        /// <summary>Whether the "already sounded" latch survives into the next pass. IT DOES NOT
        /// SURVIVE THE CONDITION CLEARING: a new episode starts loud instead of serving out the
        /// interval left over from the previous one. Without this, a second call for help arriving
        /// two minutes after the first stays silent for three - and the second one is the one nobody
        /// is expecting.
        ///
        /// It lives here rather than as an assignment inside the adapter because it is a DECISION,
        /// and a decision sitting in code no test can execute is part of the defect rather than an
        /// excuse for having no test.</summary>
        public static bool KeepSoundedLatch(bool conditionHolds, bool everSounded)
        {
            return conditionHolds && everSounded;
        }
    }
}
