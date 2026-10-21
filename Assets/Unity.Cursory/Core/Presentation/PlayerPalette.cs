namespace Unity.Cursory.Core
{
    /// <summary>
    /// Player colours on screen. A player's own colour comes from their connection approval payload (the library's
    /// <c>PlayerIdentity.Color</c>, kept on their seat) and the server copies it onto their cursor for every peer;
    /// this palette is the fallback when that colour is missing or plain white (the library's default), picked by
    /// seat so neighbouring seats differ. Cursory's own accent colours.
    /// </summary>
    public static class PlayerPalette
    {
        /// <summary>The fallback colours as #RRGGBB, in seat order.</summary>
        public static readonly string[] Hex =
        {
            "#E24B4A", // red
            "#378ADD", // blue
            "#1D9E75", // green
            "#EF9F27", // amber
            "#7F77DD", // violet
            "#2BB5B0", // teal
            "#D4537E", // pink
            "#8DB33A", // olive
        };

        /// <summary>The fallback colour for seat (or client id) <paramref name="seat"/>, as RGB bytes.</summary>
        public static (byte R, byte G, byte B) ForSeat(long seat)
        {
            int i = (int)(((seat % Hex.Length) + Hex.Length) % Hex.Length);
            HexColor.TryParse(Hex[i], out var r, out var g, out var b);
            return (r, g, b);
        }

        /// <summary>
        /// The colour to draw a player in: <paramref name="chosen"/> when the player picked one, else the seat's fallback.
        /// A colour counts as unpicked when it is pure white (<c>PlayerColor.White</c>, the library's default) or fully transparent.
        /// </summary>
        public static (byte R, byte G, byte B) Resolve(byte r, byte g, byte b, byte a, long seat)
        {
            bool unpicked = a == 0 || (r == 255 && g == 255 && b == 255);
            return unpicked ? ForSeat(seat) : (r, g, b);
        }
    }
}
