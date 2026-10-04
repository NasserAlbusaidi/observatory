using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // Ships as targets: intercept runners (Body.Path, under thrust) and rescue derelicts (drive dead) get their own
    // kind line and a small icon in the target card and the target bracket, instead of reading as planets or moons.
    public partial class OpenSystemScene
    {
        enum TargetKind { Body, Ship, Wreck }

        static TargetKind KindOf(Body b) =>
            b == null ? TargetKind.Body : Career.IsDerelict(b) ? TargetKind.Wreck : b.Path != null ? TargetKind.Ship : TargetKind.Body;

        static readonly Color ShipCol = new Color(0.41f, 0.82f, 1f), WreckCol = new Color(1f, 0.42f, 0.37f);

        // 18 px icon: a ship is an arrowhead, a wreck is a broken (crossed) hull. Hidden for natural bodies.
        static VisualElement KindIcon(VisualElement parent)
        {
            var box = Ui.E(parent, "kind-icon");
            box.pickingMode = PickingMode.Ignore;
            var ship = Ui.E(box, "ship");
            ship.pickingMode = PickingMode.Ignore;
            ship.style.position = Position.Absolute;
            ship.style.left = 3; ship.style.top = 3; ship.style.width = 12; ship.style.height = 12;
            ship.style.borderTopWidth = 3; ship.style.borderRightWidth = 3;
            ship.style.borderTopColor = ShipCol; ship.style.borderRightColor = ShipCol;
            ship.style.rotate = new Rotate(-45); // points up, like a ship's nose
            ship.style.translate = new Translate(0, 3);
            var wreck = Ui.E(box, "wreck");
            wreck.pickingMode = PickingMode.Ignore;
            wreck.style.position = Position.Absolute;
            wreck.style.left = 0; wreck.style.top = 0; wreck.style.right = 0; wreck.style.bottom = 0;
            foreach (float r in new[] { 45f, -45f })
            {
                var bar = Ui.E(wreck);
                bar.pickingMode = PickingMode.Ignore;
                bar.style.position = Position.Absolute;
                bar.style.left = 1; bar.style.top = 7; bar.style.width = 16; bar.style.height = 3;
                bar.style.backgroundColor = WreckCol;
                bar.style.rotate = new Rotate(r);
            }
            Ui.Show(box, false);
            return box;
        }

        static void SetKindIcon(VisualElement box, TargetKind k)
        {
            Ui.Show(box, k != TargetKind.Body);
            Ui.Show(box.Q(className: "ship"), k == TargetKind.Ship);
            Ui.Show(box.Q(className: "wreck"), k == TargetKind.Wreck);
        }

        // review shots: target the first board (or active) job of a kind; false if none is posted
        public bool TestTargetJob(JobKind kind)
        {
            var c = career.Active.Find(x => x.Kind == kind);
            if (c == null)
            {
                // board previews aren't in the system until accepted, so take the job
                c = career.Board.Find(x => x.Kind == kind && career.CanAccept(x, out _));
                if (c != null) career.Accept(c, jd);
            }
            var b = c != null ? career.TargetOf(c) : null;
            if (b == null) return false;
            SetTarget(b);
            return true;
        }

        // review shots: orbit camera around the ship, turned so the target sits at `frac` of the screen width
        public void TestAimTargetAt(float frac)
        {
            if (ship.Target == null) return;
            SetPivot(Pivot.Ship, 0.16);
            camMode = CamMode.Orbit;
            var d = Frame.U(sys.Pos(ship.Target, jd) - ship.Pos).normalized;
            float hfov = Mathf.Atan(Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2) * Cam.aspect);
            float ang = Mathf.Atan((frac - 0.5f) * 2 * Mathf.Tan(hfov)) * Mathf.Rad2Deg;
            var f = Quaternion.AngleAxis(-ang, Vector3.up) * d;
            var off = -f;
            yaw = Mathf.Atan2(off.x, -off.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(off.y, -1, 1)) * Mathf.Rad2Deg;
        }

        // review shots: does the bracket label overlap the right-hand panels?
        public string TestLabelReport()
        {
            var lab = mkTgt?.Q("label");
            if (lab == null || mkTgt.resolvedStyle.display != DisplayStyle.Flex) return "label hidden";
            var a = lab.worldBound; var b = rightCol.worldBound;
            return $"label x {a.xMin:0}–{a.xMax:0}, panels from x {b.xMin:0}: {(a.Overlaps(b) ? "OVERLAP" : "clear")}";
        }

        string CraftKind(Body b)
        {
            switch (KindOf(b))
            {
                case TargetKind.Wreck: return "Derelict ship · drive dead · orbiting " + (b.Parent?.Name ?? "the Sun");
                case TargetKind.Ship: return "Ship under thrust";
                default: return null;
            }
        }
    }
}
