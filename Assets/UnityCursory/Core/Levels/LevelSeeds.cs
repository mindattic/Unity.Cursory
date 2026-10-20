using System;
using System.Collections.Generic;

namespace UnityCursory.Core
{
    /// <summary>
    /// Cursory's 14 levels as <see cref="LevelData"/>, ported one for one from
    /// <c>Cursory.Core/Services/RoomState.SeedLevel1..14</c> (positions in world pixels, BIBLE UC-LAW-3).
    /// These are the source the level ScriptableObjects are written from (the editor's
    /// <c>UnityCursory/Write Level Assets</c> menu); the game loads the assets, not this class. Every Mass is
    /// Cursory's own, and each sits clear of the 1.5 and 3.0 cursor-count thresholds (BIBLE UC-LAW-2).
    /// Each call returns a fresh copy, so a caller can change it freely.
    /// </summary>
    public static class LevelSeeds
    {
        /// <summary>How many levels there are (Cursory's <c>RoomState.LevelCount</c>).</summary>
        public const int Count = 14;

        /// <summary>Level <paramref name="number"/> (1-based).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is not 1 to <see cref="Count"/>.</exception>
        public static LevelData Level(int number)
        {
            switch (number)
            {
                case 1: return Level1();
                case 2: return Level2();
                case 3: return Level3();
                case 4: return Level4();
                case 5: return Level5();
                case 6: return Level6();
                case 7: return Level7();
                case 8: return Level8();
                case 9: return Level9();
                case 10: return Level10();
                case 11: return Level11();
                case 12: return Level12();
                case 13: return Level13();
                case 14: return Level14();
                default: throw new ArgumentOutOfRangeException(nameof(number), number, $"levels run from 1 to {Count}");
            }
        }

        /// <summary>Every level, in order, numbered 1 to <see cref="Count"/>.</summary>
        public static List<LevelData> All()
        {
            var levels = new List<LevelData>(Count);
            for (var n = 1; n <= Count; n++) levels.Add(Level(n));
            return levels;
        }

        static WorldLabel Label(int number, float x, float y, string title, string subtitle) => new WorldLabel
        {
            Id = $"L{number}-label",
            X = x,
            Y = y,
            Title = $"Level {number} — {title}",
            Subtitle = subtitle,
        };

        static Shape Bar(string id, float x, float y, float mass, string color, float halfW, float halfH) => new Shape
        {
            Id = id,
            X = x,
            Y = y,
            Mass = mass,
            Color = color,
            Pieces = { new ShapePiece { LocalX = 0, LocalY = 0, HalfW = halfW, HalfH = halfH } },
        };

        /// <summary>"Drop it on the pad": one light block and a generous pad; the solo controls lesson.</summary>
        static LevelData Level1() => new LevelData
        {
            Number = 1,
            Title = "Drop it on the pad",
            Blocks = { new Block { Id = "L1-block", X = 3500, Y = 5000, W = 160, H = 160, Mass = 1f, Color = "#D85A30" } },
            BlockGoals = { new BlockGoal { Id = "L1-goal", X = 6500, Y = 5000, W = 700, H = 700, TargetBlockId = "L1-block" } },
            Labels = { Label(1, 5000, 3500, "Drop it on the pad", "Click the block's edge and drag it into the big square. The number is its mass.") },
        };

        /// <summary>"Too heavy for one": Mass 2 needs two cursors, routed around a wall.</summary>
        static LevelData Level2() => new LevelData
        {
            Number = 2,
            Title = "Too heavy for one",
            Blocks = { new Block { Id = "L2-block", X = 3000, Y = 5000, W = 260, H = 260, Mass = 2f, Color = "#378ADD" } },
            Walls = { new Wall { Id = "L2-wall", X = 5000, Y = 5000, W = 180, H = 1200 } },
            BlockGoals = { new BlockGoal { Id = "L2-goal", X = 6700, Y = 5000, W = 700, H = 700, TargetBlockId = "L2-block" } },
            Labels = { Label(2, 5000, 3500, "Too heavy for one", "Mass 2: one cursor (max ~1.5) can't. Two of you grab edges and pull around the wall.") },
        };

        /// <summary>"Pivot the couch": an L-shaped compound body threaded around a wall.</summary>
        static LevelData Level3() => new LevelData
        {
            Number = 3,
            Title = "Pivot the couch",
            Shapes =
            {
                new Shape
                {
                    Id = "L3-shape", X = 2800, Y = 5000, Mass = 2f, Color = "#7FBF5A",
                    Pieces =
                    {
                        new ShapePiece { LocalX = 0, LocalY = 0, HalfW = 220, HalfH = 55 },       // long arm
                        new ShapePiece { LocalX = -165, LocalY = -165, HalfW = 55, HalfH = 165 }, // the foot of the L
                    },
                },
            },
            Walls = { new Wall { Id = "L3-wall", X = 5000, Y = 5000, W = 180, H = 700 } },
            ShapeGoals = { new ShapeGoal { Id = "L3-goal", X = 7200, Y = 5000, W = 1000, H = 1000, TargetShapeId = "L3-shape" } },
            Labels = { Label(3, 5000, 3300, "Pivot the couch", "Grab the L's edges and route it around the wall onto the pad. Two cursors to turn it.") },
        };

        /// <summary>"Stand together, then heave": Mass 2.2 heaved through a 400-pixel gap.</summary>
        static LevelData Level4() => new LevelData
        {
            Number = 4,
            Title = "Stand together, then heave",
            Blocks = { new Block { Id = "L4-block", X = 2800, Y = 5000, W = 240, H = 240, Mass = 2.2f, Color = "#D4537E" } },
            Walls =
            {
                new Wall { Id = "L4-wt", X = 5000, Y = 4150, W = 220, H = 1300 }, // gap from y 4800
                new Wall { Id = "L4-wb", X = 5000, Y = 5850, W = 220, H = 1300 }, // to y 5200
            },
            BlockGoals = { new BlockGoal { Id = "L4-goal", X = 7200, Y = 5000, W = 800, H = 800, TargetBlockId = "L4-block" } },
            Labels = { Label(4, 5000, 3200, "Stand together, then heave", "Mass 2.2 — one cursor can't. Line up and heave it straight through the gap together.") },
        };

        /// <summary>"Heavy heave": the heaviest straight drag, Mass 2.6, no obstacles.</summary>
        static LevelData Level5() => new LevelData
        {
            Number = 5,
            Title = "Heavy heave",
            Blocks = { new Block { Id = "L5-block", X = 2800, Y = 5000, W = 300, H = 300, Mass = 2.6f, Color = "#3a3a3a" } },
            BlockGoals = { new BlockGoal { Id = "L5-goal", X = 7200, Y = 5000, W = 850, H = 850, TargetBlockId = "L5-block" } },
            Labels = { Label(5, 5000, 3300, "Heavy heave", "Mass 2.6. Both cursors, both pulling hard the same way. Watch your pull numbers add up.") },
        };

        /// <summary>"Mirror match": two light blocks, two pads, a cursor each.</summary>
        static LevelData Level6() => new LevelData
        {
            Number = 6,
            Title = "Mirror match",
            Blocks =
            {
                new Block { Id = "L6-a", X = 3000, Y = 4200, W = 170, H = 170, Mass = 1.1f, Color = "#7F77DD" },
                new Block { Id = "L6-b", X = 3000, Y = 5800, W = 170, H = 170, Mass = 1.1f, Color = "#D85A30" },
            },
            BlockGoals =
            {
                new BlockGoal { Id = "L6-ga", X = 7000, Y = 4200, W = 550, H = 550, TargetBlockId = "L6-a" },
                new BlockGoal { Id = "L6-gb", X = 7000, Y = 5800, W = 550, H = 550, TargetBlockId = "L6-b" },
            },
            Labels = { Label(6, 5000, 3300, "Mirror match", "Two blocks, two pads. Take one each.") },
        };

        /// <summary>"Stand and slide": Mass 2 slid down a 360-pixel corridor.</summary>
        static LevelData Level7() => new LevelData
        {
            Number = 7,
            Title = "Stand and slide",
            Blocks = { new Block { Id = "L7-block", X = 2700, Y = 5000, W = 200, H = 200, Mass = 2f, Color = "#D4537E" } },
            Walls =
            {
                new Wall { Id = "L7-top", X = 4500, Y = 4760, W = 4200, H = 120 }, // corridor inside
                new Wall { Id = "L7-bot", X = 4500, Y = 5240, W = 4200, H = 120 }, // is y 4820 to 5180
            },
            BlockGoals = { new BlockGoal { Id = "L7-goal", X = 7100, Y = 5000, W = 600, H = 600, TargetBlockId = "L7-block" } },
            Labels = { Label(7, 5000, 3300, "Stand and slide", "Keep it in the corridor and slide it to the far pad. Two cursors to move it.") },
        };

        /// <summary>"Block taxi": Mass 2 weaved below one jutting wall and above the next.</summary>
        static LevelData Level8() => new LevelData
        {
            Number = 8,
            Title = "Block taxi",
            Blocks = { new Block { Id = "L8-block", X = 2700, Y = 5000, W = 200, H = 200, Mass = 2f, Color = "#7F77DD" } },
            Walls =
            {
                new Wall { Id = "L8-w1", X = 4800, Y = 4100, W = 200, H = 1700 }, // hangs from the top to y 4950
                new Wall { Id = "L8-w2", X = 5800, Y = 5900, W = 200, H = 1700 }, // rises from the bottom to y 5050
            },
            BlockGoals = { new BlockGoal { Id = "L8-goal", X = 7200, Y = 5000, W = 700, H = 700, TargetBlockId = "L8-block" } },
            Labels = { Label(8, 5000, 3200, "Block taxi", "Weave the block around the walls — below the first, above the second — to the pad.") },
        };

        /// <summary>"Couch corner": a long bar turned through an L-bend hallway.</summary>
        static LevelData Level9() => new LevelData
        {
            Number = 9,
            Title = "Couch corner",
            Shapes = { Bar("L9-bar", 3000, 5400, 2f, "#7FBF5A", 280, 70) },
            Walls =
            {
                new Wall { Id = "L9-h-top", X = 3800, Y = 4600, W = 3200, H = 120 },   // ceiling of the horizontal run
                new Wall { Id = "L9-h-bot", X = 3500, Y = 5800, W = 2600, H = 120 },   // floor of the horizontal run
                new Wall { Id = "L9-v-left", X = 4600, Y = 4000, W = 120, H = 2000 },  // left wall of the vertical run
                new Wall { Id = "L9-v-right", X = 5800, Y = 3700, W = 120, H = 2400 }, // right wall of the vertical run
            },
            ShapeGoals = { new ShapeGoal { Id = "L9-goal", X = 5200, Y = 3500, W = 1000, H = 1000, TargetShapeId = "L9-bar" } },
            Labels = { Label(9, 4000, 2900, "Couch corner", "Pivot the bar around the corner and up the hallway to the pad.") },
        };

        /// <summary>"Tug steady": Mass 2.6, where pulling against each other cancels.</summary>
        static LevelData Level10() => new LevelData
        {
            Number = 10,
            Title = "Tug steady",
            Blocks = { new Block { Id = "L10-block", X = 2800, Y = 5000, W = 280, H = 280, Mass = 2.6f, Color = "#378ADD" } },
            BlockGoals = { new BlockGoal { Id = "L10-goal", X = 7200, Y = 5000, W = 850, H = 850, TargetBlockId = "L10-block" } },
            Labels = { Label(10, 5000, 3300, "Tug steady", "Same direction beats friction; opposite directions cancel. Don't fight your partner.") },
        };

        /// <summary>"Two-key lock": a block per lane, both parked at once.</summary>
        static LevelData Level11() => new LevelData
        {
            Number = 11,
            Title = "Two-key lock",
            Blocks =
            {
                new Block { Id = "L11-a", X = 3000, Y = 4300, W = 180, H = 180, Mass = 1.3f, Color = "#1D9E75" },
                new Block { Id = "L11-b", X = 3000, Y = 5700, W = 180, H = 180, Mass = 1.3f, Color = "#1D9E75" },
            },
            Walls = { new Wall { Id = "L11-div", X = 5000, Y = 5000, W = 5400, H = 140 } }, // lane divider
            BlockGoals =
            {
                new BlockGoal { Id = "L11-ga", X = 7000, Y = 4300, W = 550, H = 550, TargetBlockId = "L11-a" },
                new BlockGoal { Id = "L11-gb", X = 7000, Y = 5700, W = 550, H = 550, TargetBlockId = "L11-b" },
            },
            Labels = { Label(11, 5000, 3200, "Two-key lock", "Both pads, both lanes, at once. A cursor each.") },
        };

        /// <summary>"Spinner": a 700-pixel bar that only threads the 600-pixel slot upright.</summary>
        static LevelData Level12() => new LevelData
        {
            Number = 12,
            Title = "Spinner",
            Shapes = { Bar("L12-bar", 3000, 5000, 2f, "#D85A30", 350, 80) },
            Walls =
            {
                new Wall { Id = "L12-left", X = 4350, Y = 5000, W = 700, H = 1700 },  // gap from x 4700
                new Wall { Id = "L12-right", X = 5650, Y = 5000, W = 700, H = 1700 }, // to x 5300
            },
            ShapeGoals = { new ShapeGoal { Id = "L12-goal", X = 7000, Y = 5000, W = 1100, H = 1100, TargetShapeId = "L12-bar" } },
            Labels = { Label(12, 5000, 3200, "Spinner", "The bar only fits the slot end-on. Rotate it upright and thread it through.") },
        };

        /// <summary>"Door hold": Mass 2.2 eased through a 300-pixel gap.</summary>
        static LevelData Level13() => new LevelData
        {
            Number = 13,
            Title = "Door hold",
            Blocks = { new Block { Id = "L13-block", X = 2800, Y = 5000, W = 220, H = 220, Mass = 2.2f, Color = "#7F77DD" } },
            Walls =
            {
                new Wall { Id = "L13-wt", X = 5000, Y = 4100, W = 240, H = 1500 }, // gap from y 4850
                new Wall { Id = "L13-wb", X = 5000, Y = 5900, W = 240, H = 1500 }, // to y 5150
            },
            BlockGoals = { new BlockGoal { Id = "L13-goal", X = 7200, Y = 5000, W = 700, H = 700, TargetBlockId = "L13-block" } },
            Labels = { Label(13, 5000, 3200, "Door hold", "A tight gap. Control the block together and ease it through without snagging.") },
        };

        /// <summary>
        /// "Light the bulb": wire battery + to resistor to bulb to battery − with three loose wires. Six
        /// terminals and three wires split naturally between two players.
        /// </summary>
        static LevelData Level14() => new LevelData
        {
            Number = 14,
            Title = "Light the bulb",
            Terminals =
            {
                new Terminal { Id = "bat+", X = 3000, Y = 4650, Polarity = TerminalPolarity.Positive },
                new Terminal { Id = "bat-", X = 3000, Y = 5350, Polarity = TerminalPolarity.Negative },
                new Terminal { Id = "r-a", X = 4700, Y = 3900 },
                new Terminal { Id = "r-b", X = 5300, Y = 3900 },
                new Terminal { Id = "b-a", X = 6500, Y = 4400 },
                new Terminal { Id = "b-b", X = 7100, Y = 4400 },
            },
            Components =
            {
                new CircuitComponent { Kind = ComponentKind.Battery, Id = "battery", X = 3000, Y = 5000, W = 360, H = 900, Label = "Battery", TerminalIds = { "bat+", "bat-" } },
                new CircuitComponent { Kind = ComponentKind.Resistor, Id = "resistor", X = 5000, Y = 3900, W = 700, H = 200, Label = "Resistor", TerminalIds = { "r-a", "r-b" } },
                new CircuitComponent { Kind = ComponentKind.Bulb, Id = "bulb", X = 6800, Y = 4400, W = 360, H = 360, Label = "Bulb", TerminalIds = { "b-a", "b-b" } },
            },
            Wires =
            {
                new Wire { Id = "w1", Color = "#d98a3d", Ax = 3400, Ay = 6100, Bx = 3900, By = 6100 },
                new Wire { Id = "w2", Color = "#caa472", Ax = 4600, Ay = 6300, Bx = 5100, By = 6300 },
                new Wire { Id = "w3", Color = "#8fb3d9", Ax = 5800, Ay = 6100, Bx = 6300, By = 6100 },
            },
            Labels = { Label(14, 5000, 2900, "Light the bulb", "Drag the wire ends onto the posts: battery + → resistor → bulb → battery −. Light it up.") },
        };
    }
}
