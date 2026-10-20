namespace UnityCursory.Core
{
    /// <summary>Cursory's colours are <c>#RRGGBB</c> strings in level data and on the wire; this reads them.</summary>
    public static class HexColor
    {
        /// <summary>Parses <c>#RRGGBB</c> (either case) into its channels; false for anything else.</summary>
        public static bool TryParse(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            if (!TryByte(hex, 1, out r) || !TryByte(hex, 3, out g) || !TryByte(hex, 5, out b))
            {
                r = g = b = 0;
                return false;
            }
            return true;
        }

        /// <summary>True when <paramref name="hex"/> is a <c>#RRGGBB</c> colour.</summary>
        public static bool IsValid(string hex) => TryParse(hex, out _, out _, out _);

        private static bool TryByte(string s, int at, out byte value)
        {
            value = 0;
            var hi = Nibble(s[at]);
            var lo = Nibble(s[at + 1]);
            if (hi < 0 || lo < 0) return false;
            value = (byte)(hi * 16 + lo);
            return true;
        }

        private static int Nibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
