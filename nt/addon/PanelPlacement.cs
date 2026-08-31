// Where the status panel sits, and where it goes when it changes size - the decision, extracted so
// it can be interrogated without NinjaTrader, without WPF and without a screen.
//
// THIS FILE MUST NEVER REFERENCE NINJATRADER OR WPF. Same move as GuardedAccountRule and
// BotAccountRule, and for the same reason: deciding and consulting the world must not be the same
// code, or proving the decision needs the platform in the state under test.
//
// WHY IT EXISTS AT ALL, and the miss is worth recording. On 2026-08-31 the panel was taught to GROW
// in the state that needs a human - bigger headline, wider window - and Width was set in Render, so
// Left was recomputed there too, from the corner of the work area. Render runs on every refresh. The
// panel would therefore have snapped back to the top-right corner about once a second, and a trader
// who dragged it somewhere would have watched it walk home.
//
// It was caught by ONE QUESTION NOBODY HAD ASKED ALL DAY: can the user MOVE this thing? We had spent
// the day reviewing what the panel SAYS, word by word, and never what a person can DO with it. A
// message review is not a manipulation review, and the second one has to be asked separately.
//
// And Roberto's answer - "me sale en primer plano y lo muevo para una esquina", in the habitual
// present - carried a second defect in its tense: he does it EVERY TIME, because the constructor
// fixes the position and every F5 rebuilds the window. Fixing the snap without persisting the
// position would only lengthen the interval between re-tidyings from one second to one F5.

using System;
using GuardianCore;   // StateKind only - this file still references no NinjaTrader type

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    /// <summary>A position on screen. Plain doubles: this type crosses no framework boundary.</summary>
    public struct Placement
    {
        public double Left;
        public double Top;

        public Placement(double left, double top) { Left = left; Top = top; }

        public override string ToString()
        {
            return "(" + Left.ToString("0.##") + ", " + Top.ToString("0.##") + ")";
        }
    }

    public static class PanelPlacement
    {
        /// <summary>Where Left goes when the panel changes width. THE RIGHT EDGE STAYS PUT, so the
        /// panel grows leftward from wherever the trader dragged it instead of jumping to a corner.
        ///
        /// Growing rightward from a fixed Left would push a widened panel toward - and past - the
        /// right screen edge, which is exactly where the default position already is.</summary>
        public static double LeftAfterWidthChange(double currentLeft, double oldWidth, double newWidth)
        {
            return currentLeft - (newWidth - oldWidth);
        }

        /// <summary>Keep the panel reachable, and CORRECT ONLY WHEN IT WOULD BE OFF SCREEN. A position
        /// the trader chose is not second-guessed for being unusual - only for being unreachable.
        ///
        /// This is also what makes a saved position safe to restore: a laptop that was undocked, or a
        /// monitor that went away, leaves coordinates that no longer exist. Clamping turns that into a
        /// panel in the corner rather than a panel nobody can find.
        ///
        /// A panel wider or taller than the work area pins to the top-left: partially visible beats
        /// centred-and-clipped, because the headline is at the top.</summary>
        public static Placement Clamp(double left, double top, double width, double height,
                                      double areaLeft, double areaTop, double areaRight, double areaBottom)
        {
            var maxLeft = areaRight - width;
            var maxTop = areaBottom - height;

            var l = maxLeft < areaLeft ? areaLeft : (left < areaLeft ? areaLeft : (left > maxLeft ? maxLeft : left));
            var t = maxTop < areaTop ? areaTop : (top < areaTop ? areaTop : (top > maxTop ? maxTop : top));
            return new Placement(l, t);
        }

        /// <summary>The default corner, used when nothing was ever saved. Same expression the
        /// constructor used before there was anywhere to remember a choice.</summary>
        public static Placement Default(double width, double areaTop, double areaRight)
        {
            return new Placement(areaRight - width - 20, areaTop + 20);
        }
    }

    /// <summary>Whether the panel may be reduced to a strip, and whether that choice is remembered.
    ///
    /// Pure, and here rather than in the window, for the reason every rule in this codebase is
    /// extracted: deciding and consulting the world must not be the same code.
    ///
    /// TWO STATES MAY NOT COLLAPSE, and the second was nearly missed. The one that needs a human is
    /// obvious. FailClosed is the one that hides: the guardian is BLIND, and this panel is the only
    /// sign that the trader is not protected. It is arguably worse than needing a human - there he
    /// knows his day is over; here he believes he has a brake and does not.
    ///
    /// It was missed on the first pass because the review looked at the state that SHOUTS and not at
    /// the one that is QUIET. "THE GUARDIAN NEEDS YOU" is an imperative in capitals; "CANNOT SEE YOUR
    /// ACCOUNT" describes a condition and so reads as informational. URGENCY OF TONE IS NOT THE SAME
    /// AS WHAT IS AT STAKE.</summary>
    public static class PanelCollapse
    {
        public static bool MayCollapse(StateKind kind, bool needsHuman, string reason)
        {
            if (needsHuman) return false;
            if (kind == StateKind.FailClosed) return false;   // blind, or M22: both may stand for hours
            return true;
        }

        /// <summary>Collapsed is remembered - except into DISARMED, and that exception is the whole
        /// product. Collapse on Tuesday, the session close leaves it DISARMED, and on Wednesday
        /// NinjaTrader opens to a strip reading NOT ARMED that asks for nothing. The guardian becomes
        /// furniture. This product depends on ONE VOLUNTARY ACT PER DAY, and hiding its only button
        /// behind a click nobody remembers is how a commitment device stops being used - with nobody
        /// uninstalling it and nobody deciding to abandon it. Every day starts with the product
        /// asking for its own.</summary>
        public static bool RemembersCollapsed(StateKind kind)
        {
            return kind != StateKind.Disarmed;
        }

        /// <summary>What the comfort file is allowed to hold, which is NOT the same as what the panel
        /// is currently showing.
        ///
        /// Added 2026-08-31 after the first real boot of the day's work, found by reading ui.json:
        /// Roberto collapsed the panel while DISARMED, Render forced it open again - the rule worked
        /// ON SCREEN - but SavePrefs had already written "collapsed": true and nothing corrected it.
        /// THE RULE WAS IMPLEMENTED ON THE SCREEN AND NOT IN THE FILE.
        ///
        /// The consequence is the Tuesday-to-Wednesday scenario the rule exists to prevent, arriving
        /// through a door nobody looked at: not by REMAINING collapsed in DISARMED, but by PERSISTING
        /// FROM it. The next boot reads true; if that boot is ARMED, RemembersCollapsed says true and
        /// nobody corrects it, so a collapse made in the state that forgets leaks into the state that
        /// remembers.
        ///
        /// Being remembered is therefore a property of the WRITE, decided here once, rather than
        /// something every call site has to get right.</summary>
        public static bool PersistCollapsed(bool collapsed, StateKind kind)
        {
            return collapsed && RemembersCollapsed(kind);
        }

        /// <summary>Whether the panel must open ITSELF right now. A TRANSITION, never a standing
        /// condition, and the difference was a real defect rather than a nicety.
        ///
        /// Reported by Roberto on the first boot of 2026-08-31: he pressed the collapse button while
        /// DISARMED, the panel collapsed, and a second later it opened by itself. From outside that
        /// is indistinguishable from a button that does nothing - and he read it exactly that way.
        ///
        /// The rule agreed was "on ENTERING Disarmed the panel opens itself", so tomorrow starts with
        /// the product asking for its own. It had been implemented as "force it open WHILE Disarmed",
        /// a standing condition, which silently withdrew a capability the same design had granted:
        /// collapsing in Disarmed for the current session. AN AGREED "YES, BUT NOT REMEMBERED" HAD
        /// BECOME "NO", and nothing in the suite could tell, because the pure rule it was built on -
        /// RemembersCollapsed - was correct the whole time. The defect lived in how the window
        /// applied it, which is why the decision is extracted here now.
        ///
        /// The first render counts as a transition on purpose: booting into Disarmed with a
        /// remembered collapse is precisely the Tuesday-to-Wednesday case the rule exists for.</summary>
        public static bool ShouldOpenItself(bool collapsed, StateKind kind, StateKind previousKind,
                                            bool firstRender)
        {
            if (!collapsed) return false;
            if (RemembersCollapsed(kind)) return false;
            return firstRender || kind != previousKind;
        }
    }

    /// <summary>The panel's remembered comfort settings: WHERE the trader put it, and whether
    /// they collapsed it.
    ///
    /// DELIBERATELY NOT config.json. That file is SEALED and belongs to the commitment; this one
    /// belongs to comfort. Editing config.json under a seal writes CONFIG_TAMPERED and locks the
    /// trader out - a window position must never be able to do that.
    ///
    /// AND THE GUARDIAN MUST BE ABLE TO LOSE THIS FILE WITH NO CONSEQUENCE. Missing, empty, truncated,
    /// corrupt, or written by a future version: every one of those produces defaults and nothing else.
    /// No exception, no fail-closed, no ledger event. Comfort is not a premise the guardian acts on,
    /// so its absence is not an unknown to be defended against - it is just a fresh panel in the
    /// corner. This is the ONE place in this codebase where a plausible default is the right answer,
    /// and it is right precisely because nothing depends on it.</summary>
    public sealed class UiPrefs
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public bool Collapsed { get; set; }

        public bool HasPosition { get { return Left.HasValue && Top.HasValue; } }

        /// <summary>Never throws and never reports failure, by contract. See the type's own note.</summary>
        public static UiPrefs Parse(string text)
        {
            var prefs = new UiPrefs();
            if (string.IsNullOrWhiteSpace(text)) return prefs;
            try
            {
                double l, t;
                if (TryNumber(text, "left", out l) && TryNumber(text, "top", out t))
                {
                    if (!double.IsNaN(l) && !double.IsInfinity(l) &&
                        !double.IsNaN(t) && !double.IsInfinity(t))
                    {
                        prefs.Left = l;
                        prefs.Top = t;
                    }
                }
                prefs.Collapsed = text.IndexOf("\"collapsed\": true", StringComparison.OrdinalIgnoreCase) >= 0
                               || text.IndexOf("\"collapsed\":true", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return new UiPrefs(); }
            return prefs;
        }

        public string Format()
        {
            return "{\"left\": " + (Left ?? 0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                   ", \"top\": " + (Top ?? 0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                   ", \"collapsed\": " + (Collapsed ? "true" : "false") + "}";
        }

        private static bool TryNumber(string text, string key, out double value)
        {
            value = 0;
            var needle = "\"" + key + "\"";
            var at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            at = text.IndexOf(':', at + needle.Length);
            if (at < 0) return false;

            var start = at + 1;
            while (start < text.Length && (text[start] == ' ' || text[start] == '\t')) start++;
            var end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '-' || text[end] == '+' ||
                                         text[end] == '.' || text[end] == 'e' || text[end] == 'E')) end++;
            if (end == start) return false;

            return double.TryParse(text.Substring(start, end - start),
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
