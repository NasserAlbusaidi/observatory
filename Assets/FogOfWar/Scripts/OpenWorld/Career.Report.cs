using System.Collections.Generic;

namespace FogOfWar.OpenWorld
{
    // What one salvo did, for the after-action card: the range it was fired from, the side it came in on, the PDC mounts
    // that bore on its first and last missile to arrive (a mount hit between them can change the count), the PDC kills, and
    // the plating's and the systems' hit notes in order. Kept on the Encounter (Salvos), so it is not saved either.
    public sealed class SalvoLog
    {
        public int Fired, Arrived, Killed;   // Fired 0: missiles put in flight by hand (QA), with no salvo of their own
        public double Km = double.NaN;       // the range at launch
        public Career.Aspect Aspect;         // the first arrival's
        public int BoreFirst, BoreLast;
        public readonly List<string> Hits = new List<string>();
    }

    // Phase 5 point 9 (docs/design-combat.md): every end of a chase (EndContact) puts up an after-action card: one line a
    // salvo, then the end's cause, its note. It drops no warp and holds no call back (RouteTick): a new call takes its place.
    // A load ends a chase with no card (DropContact, LostContact).
    public partial class Career
    {
        // Strike's record: one arrival of the open salvo (Fire opened it), or of a salvo for missiles put in flight by hand;
        // n0 is the note count before the strike, so the notes it said are the ones past it
        void Record(Encounter e, Aspect a, int bore, bool killed, int n0)
        {
            var s = e.Salvos.Count > 0 ? e.Salvos[e.Salvos.Count - 1] : null;
            if (s == null || s.Fired > 0 && s.Arrived >= s.Fired) e.Salvos.Add(s = new SalvoLog());
            if (s.Arrived++ == 0) { s.Aspect = a; s.BoreFirst = bore; }
            s.BoreLast = bore;
            if (killed) s.Killed++;
            for (int i = n0; i < Notes.Count; i++)
                if (Notes[i].What == Happening.MissileHit || Notes[i].What == Happening.JobFailed) s.Hits.Add(Notes[i].Text);
        }

        void ReportCard(Encounter e, string cause)
        {
            var lines = new List<string>();
            if (e.Salvos.Count == 0) lines.Add($"The {e.Hull} fired no missiles.");
            for (int i = 0; i < e.Salvos.Count; i++) lines.Add($"Salvo {i + 1}: " + SalvoLine(e.Salvos[i]));
            lines.Add(cause + ".");
            Call = new RouteCall
            {
                Kind = RouteKind.Report, Job = e.Job, Title = $"After action: the {e.Hull}", Text = string.Join("\n", lines),
                Answers = new[] { (RouteAnswer.Close, "Close") }, Yes = "Close", No = "Close",
            };
        }

        // "2 missiles from 1,991 km, in at the tail. 2 mounts bore, then 1. PDCs killed 1. Hit: a PDC mount, 1 of 2 working."
        internal static string SalvoLine(SalvoLog s)
        {
            int n = System.Math.Max(s.Fired, s.Arrived);
            string what = $"{n} {(n == 1 ? "missile" : "missiles")}" + (double.IsNaN(s.Km) ? "" : $" from {s.Km:N0} km");
            if (s.Arrived == 0) return what + ": none arrived before the end.";
            var parts = new List<string> { what + ", in at the " + (s.Aspect == Aspect.Fore ? "nose" : s.Aspect == Aspect.Aft ? "tail" : "side") };
            parts.Add(s.BoreFirst == 0 && s.BoreLast == 0 ? "No mount bore"
                : $"{s.BoreFirst} {(s.BoreFirst == 1 ? "mount" : "mounts")} bore" + (s.BoreLast != s.BoreFirst ? $", then {s.BoreLast}" : ""));
            if (s.BoreFirst > 0 || s.BoreLast > 0) parts.Add(s.Killed > 0 ? $"PDCs killed {s.Killed}" : "PDCs killed none");
            parts.AddRange(s.Hits);
            return string.Join(". ", parts) + ".";
        }
    }
}
