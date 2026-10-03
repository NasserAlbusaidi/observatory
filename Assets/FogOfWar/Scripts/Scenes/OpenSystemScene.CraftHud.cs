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
            var c = career.Board.Find(x => x.Kind == kind) ?? career.Active.Find(x => x.Kind == kind);
            var b = c != null ? career.TargetOf(c) : null;
            if (b == null) return false;
            SetTarget(b);
            return true;
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
