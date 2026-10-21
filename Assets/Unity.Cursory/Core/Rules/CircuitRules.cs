using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>One end of a <see cref="Wire"/>: Cursory's <c>end</c> index, 0 for A and 1 for B.</summary>
    public enum WireEnd
    {
        A = 0,
        B = 1,
    }

    /// <summary>
    /// Cursory's series-circuit rules (RoomState <c>SnapWireEnd</c> and <c>EvaluateCircuit</c>), engine-free:
    /// where a released wire end plugs in, and whether the bulb is lit.
    /// </summary>
    public static class CircuitRules
    {
        /// <summary>
        /// The terminal a wire end released at (<paramref name="x"/>, <paramref name="y"/>) plugs into: the nearest
        /// one within <paramref name="radius"/> world pixels (inclusive; <see cref="Tuning.WireSnapRadius"/> by
        /// default), or null when none is that close and the end stays loose where it was dropped. On a tie the
        /// later terminal in the list wins, as in Cursory.
        /// </summary>
        public static Terminal NearestTerminal(IEnumerable<Terminal> terminals, float x, float y, float radius = Tuning.WireSnapRadius)
        {
            if (terminals == null) throw new ArgumentNullException(nameof(terminals));
            Terminal best = null;
            var bestD = radius * radius;
            foreach (var t in terminals)
            {
                if (t == null) continue;
                var dx = t.X - x;
                var dy = t.Y - y;
                var d = dx * dx + dy * dy;
                if (d <= bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>
        /// Snaps <paramref name="end"/> of <paramref name="wire"/>, just released where it lies, onto the nearest
        /// terminal within <see cref="Tuning.WireSnapRadius"/>: the end takes the terminal's id and moves onto it.
        /// Returns the terminal, or null when the end stays loose.
        /// </summary>
        public static Terminal Snap(Wire wire, WireEnd end, IEnumerable<Terminal> terminals)
        {
            if (wire == null) throw new ArgumentNullException(nameof(wire));
            var (ex, ey) = end == WireEnd.A ? (wire.Ax, wire.Ay) : (wire.Bx, wire.By);
            var best = NearestTerminal(terminals, ex, ey);
            if (best == null) return null;
            if (end == WireEnd.A)
            {
                wire.ATerminalId = best.Id;
                wire.Ax = best.X;
                wire.Ay = best.Y;
            }
            else
            {
                wire.BTerminalId = best.Id;
                wire.Bx = best.X;
                wire.By = best.Y;
            }
            return best;
        }

        /// <summary>
        /// True when the plugged-in wires and the components' internal edges close a loop from the battery's
        /// positive post to its negative one with both the bulb and the resistor in series: the loop connects, and
        /// taking out either the bulb's or the resistor's internal edge breaks it. False when the level has no
        /// bulb, resistor or battery posts.
        /// </summary>
        public static bool IsBulbLit(IEnumerable<CircuitComponent> components, IEnumerable<Terminal> terminals, IEnumerable<Wire> wires)
        {
            if (components == null) throw new ArgumentNullException(nameof(components));
            if (terminals == null) throw new ArgumentNullException(nameof(terminals));
            if (wires == null) throw new ArgumentNullException(nameof(wires));

            CircuitComponent bulb = null, resistor = null;
            foreach (var c in components)
            {
                if (c == null) continue;
                if (c.Kind == ComponentKind.Bulb && bulb == null) bulb = c;
                else if (c.Kind == ComponentKind.Resistor && resistor == null) resistor = c;
            }

            Terminal pos = null, neg = null;
            foreach (var t in terminals)
            {
                if (t == null) continue;
                if (t.Polarity == TerminalPolarity.Positive && pos == null) pos = t;
                else if (t.Polarity == TerminalPolarity.Negative && neg == null) neg = t;
            }

            if (bulb == null || resistor == null || pos == null || neg == null
                || bulb.TerminalIds.Count < 2 || resistor.TerminalIds.Count < 2)
                return false;

            var edges = new List<(string A, string B)>();
            foreach (var w in wires)
                if (w != null && w.IsConnected) edges.Add((w.ATerminalId, w.BTerminalId));
            var bulbEdge = (bulb.TerminalIds[0], bulb.TerminalIds[1]);
            var resistorEdge = (resistor.TerminalIds[0], resistor.TerminalIds[1]);
            edges.Add(bulbEdge);
            edges.Add(resistorEdge);

            if (!Connected(pos.Id, neg.Id, edges, null)) return false;
            var bulbInSeries = !Connected(pos.Id, neg.Id, edges, bulbEdge);
            var resistorInSeries = !Connected(pos.Id, neg.Id, edges, resistorEdge);
            return bulbInSeries && resistorInSeries;
        }

        /// <summary>
        /// Breadth-first search from <paramref name="src"/> to <paramref name="dst"/> over undirected
        /// <paramref name="edges"/>, skipping every edge equal (either way round) to <paramref name="exclude"/>.
        /// </summary>
        private static bool Connected(string src, string dst, List<(string A, string B)> edges, (string A, string B)? exclude)
        {
            if (src == dst) return true;
            var adjacent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            void Link(string a, string b)
            {
                if (!adjacent.TryGetValue(a, out var list))
                {
                    list = new List<string>();
                    adjacent[a] = list;
                }
                list.Add(b);
            }
            foreach (var (a, b) in edges)
            {
                if (exclude is { } ex && ((a == ex.A && b == ex.B) || (a == ex.B && b == ex.A))) continue;
                Link(a, b);
                Link(b, a);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { src };
            var queue = new Queue<string>();
            queue.Enqueue(src);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                if (!adjacent.TryGetValue(at, out var next)) continue;
                foreach (var n in next)
                {
                    if (n == dst) return true;
                    if (seen.Add(n)) queue.Enqueue(n);
                }
            }
            return false;
        }
    }
}
