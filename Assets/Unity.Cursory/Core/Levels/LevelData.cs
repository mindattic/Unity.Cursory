using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// One level's starting world, in world pixels: the schema each level ScriptableObject holds (UC-T5),
    /// replacing Cursory's hand-written <c>RoomState.SeedLevelN</c> methods. The server builds its bodies from
    /// a <see cref="Clone"/> so the asset itself is never changed by play. Cursory's never-seeded
    /// <c>SwitchTile</c>, <c>Door</c> and <c>ShapeAttachment</c> have no counterpart (BIBLE UC-§3).
    /// </summary>
    [Serializable]
    public sealed class LevelData
    {
        /// <summary>The level's 1-based number, which the level dropdown and votes use.</summary>
        public int Number = 1;
        public string Title = "";
        public List<Block> Blocks = new List<Block>();
        public List<Shape> Shapes = new List<Shape>();
        public List<Wall> Walls = new List<Wall>();
        public List<BlockGoal> BlockGoals = new List<BlockGoal>();
        public List<ShapeGoal> ShapeGoals = new List<ShapeGoal>();
        public List<CircuitComponent> Components = new List<CircuitComponent>();
        public List<Terminal> Terminals = new List<Terminal>();
        public List<Wire> Wires = new List<Wire>();
        public List<WorldLabel> Labels = new List<WorldLabel>();

        /// <summary>The static part of the level, sent to a client once per level (BIBLE UC-LAW-4).</summary>
        public LevelGeometry Geometry()
        {
            var geometry = new LevelGeometry { LevelNumber = Number };
            foreach (var w in Walls) geometry.Walls.Add(w.Clone());
            foreach (var l in Labels) geometry.Labels.Add(l.Clone());
            return geometry;
        }

        /// <summary>A deep copy, so play never changes the level asset.</summary>
        public LevelData Clone() => new LevelData
        {
            Number = Number,
            Title = Title,
            Blocks = CloneAll(Blocks, b => b.Clone()),
            Shapes = CloneAll(Shapes, s => s.Clone()),
            Walls = CloneAll(Walls, w => w.Clone()),
            BlockGoals = CloneAll(BlockGoals, g => g.Clone()),
            ShapeGoals = CloneAll(ShapeGoals, g => g.Clone()),
            Components = CloneAll(Components, c => c.Clone()),
            Terminals = CloneAll(Terminals, t => t.Clone()),
            Wires = CloneAll(Wires, w => w.Clone()),
            Labels = CloneAll(Labels, l => l.Clone()),
        };

        /// <summary>
        /// Everything wrong with this level, one sentence each; empty when it is playable. Checks ids
        /// (present and unique across the level), sizes, that everything sits inside the world, Mass clear of the
        /// cursor-count thresholds (BIBLE UC-LAW-2), colours, goal targets, the circuit's wiring, and that the
        /// level can be won at all.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);

            if (Number < 1) errors.Add($"Level number {Number} must be 1 or more.");

            void Id(string kind, string id)
            {
                if (string.IsNullOrEmpty(id)) errors.Add($"A {kind} has no id.");
                else if (!ids.Add(id)) errors.Add($"Id '{id}' is used more than once.");
            }

            void Inside(string what, float x, float y)
            {
                if (!(x >= 0f && x <= WorldUnits.WorldSizePx && y >= 0f && y <= WorldUnits.WorldSizePx))
                    errors.Add($"{what} at ({x}, {y}) is outside the {WorldUnits.WorldSizePx}-pixel world.");
            }

            void Size(string what, float w, float h)
            {
                if (!(w > 0f && h > 0f)) errors.Add($"{what} must have a positive size, not {w} x {h}.");
            }

            void Mass(string what, float mass)
            {
                if (!(mass > 0f)) errors.Add($"{what} must have a positive Mass, not {mass}.");
                else if (!Tuning.IsClearOfThresholds(mass))
                    errors.Add($"{what} Mass {mass} is within {Tuning.MassThresholdClearance} of a cursor-count threshold (multiples of {Tuning.SingleGrabMaxMass}).");
            }

            void Colour(string what, string color)
            {
                if (!HexColor.IsValid(color)) errors.Add($"{what} colour '{color}' is not #RRGGBB.");
            }

            foreach (var b in Blocks)
            {
                var what = $"Block '{b.Id}'";
                Id("block", b.Id);
                Inside(what, b.X, b.Y);
                Size(what, b.W, b.H);
                Mass(what, b.Mass);
                Colour(what, b.Color);
            }

            foreach (var s in Shapes)
            {
                var what = $"Shape '{s.Id}'";
                Id("shape", s.Id);
                Inside(what, s.X, s.Y);
                Mass(what, s.Mass);
                Colour(what, s.Color);
                if (s.Pieces.Count == 0) errors.Add($"{what} has no pieces.");
                for (var i = 0; i < s.Pieces.Count; i++)
                    Size($"{what} piece {i}", s.Pieces[i].HalfW, s.Pieces[i].HalfH);
            }

            foreach (var w in Walls)
            {
                var what = $"Wall '{w.Id}'";
                Id("wall", w.Id);
                Inside(what, w.X, w.Y);
                Size(what, w.W, w.H);
            }

            var blockIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in Blocks) blockIds.Add(b.Id);
            foreach (var g in BlockGoals)
            {
                var what = $"Block goal '{g.Id}'";
                Id("block goal", g.Id);
                Inside(what, g.X, g.Y);
                Size(what, g.W, g.H);
                if (!blockIds.Contains(g.TargetBlockId)) errors.Add($"{what} targets '{g.TargetBlockId}', which is not a block in this level.");
            }

            var shapeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in Shapes) shapeIds.Add(s.Id);
            foreach (var g in ShapeGoals)
            {
                var what = $"Shape goal '{g.Id}'";
                Id("shape goal", g.Id);
                Inside(what, g.X, g.Y);
                Size(what, g.W, g.H);
                if (!shapeIds.Contains(g.TargetShapeId)) errors.Add($"{what} targets '{g.TargetShapeId}', which is not a shape in this level.");
            }

            foreach (var l in Labels)
            {
                Id("label", l.Id);
                Inside($"Label '{l.Id}'", l.X, l.Y);
            }

            ValidateCircuit(errors, Id, Inside, Size, Colour);

            if (BlockGoals.Count == 0 && ShapeGoals.Count == 0 && Components.Count == 0)
                errors.Add("The level has no block goal, shape goal or circuit, so it can never be won.");

            return errors;
        }

        private void ValidateCircuit(
            List<string> errors,
            Action<string, string> id,
            Action<string, float, float> inside,
            Action<string, float, float> size,
            Action<string, string> colour)
        {
            var terminals = new Dictionary<string, Terminal>(StringComparer.Ordinal);
            foreach (var t in Terminals)
            {
                id("terminal", t.Id);
                inside($"Terminal '{t.Id}'", t.X, t.Y);
                if (!string.IsNullOrEmpty(t.Id)) terminals[t.Id] = t;
            }

            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var kindCount = new Dictionary<ComponentKind, int>();
            foreach (var c in Components)
            {
                var what = $"{c.Kind} '{c.Id}'";
                id("component", c.Id);
                inside(what, c.X, c.Y);
                size(what, c.W, c.H);
                kindCount.TryGetValue(c.Kind, out var n);
                kindCount[c.Kind] = n + 1;

                if (c.TerminalIds.Count != 2)
                    errors.Add($"{what} needs exactly two terminals, not {c.TerminalIds.Count}.");

                var polarities = new List<TerminalPolarity>();
                foreach (var tid in c.TerminalIds)
                {
                    if (!terminals.TryGetValue(tid ?? "", out var t))
                    {
                        errors.Add($"{what} names terminal '{tid}', which is not in this level.");
                        continue;
                    }
                    if (owner.TryGetValue(t.Id, out var other)) errors.Add($"Terminal '{t.Id}' belongs to both '{other}' and '{c.Id}'.");
                    else owner[t.Id] = c.Id;
                    polarities.Add(t.Polarity);
                }

                if (c.TerminalIds.Count != 2 || polarities.Count != 2) continue;
                if (c.Kind == ComponentKind.Battery)
                {
                    if (!polarities.Contains(TerminalPolarity.Positive) || !polarities.Contains(TerminalPolarity.Negative))
                        errors.Add($"{what} needs one positive and one negative terminal.");
                }
                else if (polarities[0] != TerminalPolarity.None || polarities[1] != TerminalPolarity.None)
                {
                    errors.Add($"{what} terminals must have no polarity; only a battery's posts do.");
                }
            }

            if (Components.Count > 0)
            {
                foreach (ComponentKind kind in Enum.GetValues(typeof(ComponentKind)))
                {
                    kindCount.TryGetValue(kind, out var n);
                    if (n != 1) errors.Add($"A circuit needs exactly one {kind}, not {n}.");
                }
            }

            foreach (var t in Terminals)
                if (!string.IsNullOrEmpty(t.Id) && !owner.ContainsKey(t.Id))
                    errors.Add($"Terminal '{t.Id}' does not belong to any component.");

            foreach (var w in Wires)
            {
                var what = $"Wire '{w.Id}'";
                id("wire", w.Id);
                colour(what, w.Color);
                inside($"{what} end A", w.Ax, w.Ay);
                inside($"{what} end B", w.Bx, w.By);
                if (w.IsAPlugged && !terminals.ContainsKey(w.ATerminalId)) errors.Add($"{what} end A is plugged into '{w.ATerminalId}', which is not a terminal.");
                if (w.IsBPlugged && !terminals.ContainsKey(w.BTerminalId)) errors.Add($"{what} end B is plugged into '{w.BTerminalId}', which is not a terminal.");
            }
        }

        private static List<T> CloneAll<T>(List<T> source, Func<T, T> clone)
        {
            var copy = new List<T>(source.Count);
            foreach (var item in source) copy.Add(clone(item));
            return copy;
        }
    }

    /// <summary>
    /// A level's static geometry: the walls and labels that never move, sent to a client when it joins and
    /// again on a level switch or reset, never on the per-tick sync (BIBLE UC-LAW-4; Cursory's
    /// <c>WorldGeometryMessage</c>). The world is always <see cref="WorldUnits.WorldSizePx"/> square.
    /// </summary>
    public sealed class LevelGeometry
    {
        public int LevelNumber;
        public List<Wall> Walls = new List<Wall>();
        public List<WorldLabel> Labels = new List<WorldLabel>();
    }
}
