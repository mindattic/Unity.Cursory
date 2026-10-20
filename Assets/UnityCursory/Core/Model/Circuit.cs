using System;
using System.Collections.Generic;

namespace UnityCursory.Core
{
    /// <summary>What a <see cref="CircuitComponent"/> is (Cursory's <c>Kind</c> string).</summary>
    public enum ComponentKind
    {
        Battery = 0,
        Resistor = 1,
        Bulb = 2,
    }

    /// <summary>A terminal's polarity: set on the battery's two posts, <see cref="None"/> elsewhere.</summary>
    public enum TerminalPolarity
    {
        None = 0,
        Positive = 1,
        Negative = 2,
    }

    /// <summary>
    /// A battery, resistor or bulb: the box drawn around two <see cref="Terminal"/>s. For a resistor or bulb
    /// the two terminals are the component's internal edge (current flows through it); for a battery they
    /// are its + and − posts. Whether a bulb is lit is runtime state, not level data (UC-T9).
    /// </summary>
    [Serializable]
    public sealed class CircuitComponent
    {
        public ComponentKind Kind;
        public string Id = "";
        public float X;
        public float Y;
        public float W;
        public float H;
        public string Label = "";
        public List<string> TerminalIds = new List<string>();

        public CircuitComponent Clone()
        {
            var copy = (CircuitComponent)MemberwiseClone();
            copy.TerminalIds = new List<string>(TerminalIds);
            return copy;
        }
    }

    /// <summary>A connection point on a component that wire ends snap onto.</summary>
    [Serializable]
    public sealed class Terminal
    {
        public string Id = "";
        public float X;
        public float Y;
        public TerminalPolarity Polarity;

        public Terminal Clone() => (Terminal)MemberwiseClone();
    }

    /// <summary>
    /// A wire with two draggable ends, A and B. Releasing an end near a terminal plugs it in. An empty
    /// terminal id means the end is loose (Unity serializes a null string as empty, so empty is the one
    /// loose value).
    /// </summary>
    [Serializable]
    public sealed class Wire
    {
        public string Id = "";
        public string Color = "#caa472";
        public float Ax;
        public float Ay;
        public float Bx;
        public float By;
        public string ATerminalId = "";
        public string BTerminalId = "";

        /// <summary>True when end A is plugged into a terminal.</summary>
        public bool IsAPlugged => !string.IsNullOrEmpty(ATerminalId);

        /// <summary>True when end B is plugged into a terminal.</summary>
        public bool IsBPlugged => !string.IsNullOrEmpty(BTerminalId);

        /// <summary>True when both ends are plugged in, so the wire is an edge of the circuit.</summary>
        public bool IsConnected => IsAPlugged && IsBPlugged;

        public Wire Clone() => (Wire)MemberwiseClone();
    }
}
