using Unity.Collections;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// UC-T11: who a cursor belongs to, for every peer to draw. The library keeps a player's display name and colour
    /// (from their connection approval payload, <see cref="PlayerIdentity"/>) on their server-side seat only, so the
    /// server copies them onto the player's cursor when it spawns: <see cref="SeatIndex"/>, <see cref="ChosenColor"/>
    /// and <see cref="DisplayName"/> are <c>[Synced]</c>. <see cref="PlayerColor"/> is what every peer draws (cursor,
    /// rope, minimap dot, whistle ripple): the chosen colour, or the seat's <see cref="PlayerPalette"/> fallback when
    /// the player kept the library's default white.
    /// </summary>
    public partial class CursorAvatar
    {
#pragma warning disable CS0169, CS0414 // declarations the generator reads; the values live in the generated network variables
        /// <summary>The player's seat (stable across a reconnect), as the server wrote it on spawn.</summary>
        [Synced] private int _seatIndex;

        /// <summary>The colour the player connected with (RGBA). White is the library's "not chosen".</summary>
        [Synced] private Color32 _chosenColor;

        /// <summary>The name the player connected with.</summary>
        [Synced] private FixedString64Bytes _displayName;
#pragma warning restore CS0169, CS0414

        /// <summary>The colour every peer draws this player in.</summary>
        public Color PlayerColor
        {
            get
            {
                Color32 c = ChosenColor;
                long seat = SeatIndex;
                var (r, g, b) = PlayerPalette.Resolve(c.r, c.g, c.b, c.a, seat);
                return new Color32(r, g, b, 255);
            }
        }

        /// <summary>The name to show for this player: their display name, else "Player N".</summary>
        public string PlayerName
        {
            get
            {
                string name = DisplayName.ToString();
                return string.IsNullOrWhiteSpace(name) ? "Player " + (SeatIndex + 1) : name;
            }
        }

        /// <summary>Server side: copies the owner's seat, name and colour onto the synced fields.</summary>
        private void AssignIdentity()
        {
            NetSession session = Session;
            Seat seat;
            if (session != null && session.TryGetSeat(OwnerClientId, out seat))
            {
                SeatIndex = seat.Index;
                ChosenColor = new Color32(seat.Color.R, seat.Color.G, seat.Color.B, seat.Color.A);
                var name = new FixedString64Bytes();
                name.CopyFromTruncated(seat.DisplayName ?? "");
                DisplayName = name;
            }
            else
            {
                SeatIndex = (int)OwnerClientId;
                ChosenColor = new Color32(255, 255, 255, 255);
            }
        }
    }
}
