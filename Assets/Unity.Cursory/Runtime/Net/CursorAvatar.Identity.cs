using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// UC-T11, on Unity.Multiplayer's player roster since UM-T55: who a cursor belongs to, for every peer to draw. The
    /// library keeps every player's name and colour on every peer (<see cref="NetRoster"/>, filled from the connection
    /// approval payload, with the seat's palette colour for a player who never picked one, and following a rename), so a
    /// cursor only carries its owner's <see cref="SeatIndex"/> (<c>[Synced]</c>, written by the server on spawn) and
    /// looks the player up by seat, which stays the same across a reconnect. <see cref="PlayerColor"/> and
    /// <see cref="PlayerName"/> are what every peer draws (cursor, rope, minimap dot, whistle ripple, name tag).
    /// </summary>
    public partial class CursorAvatar
    {
#pragma warning disable CS0169, CS0414 // declarations the generator reads; the values live in the generated network variables
        /// <summary>The player's seat (stable across a reconnect), as the server wrote it on spawn.</summary>
        [Synced] private int _seatIndex;
#pragma warning restore CS0169, CS0414

        /// <summary>This cursor's player in the session's roster; null before the roster has them.</summary>
        public NetPlayer Player
        {
            get
            {
                NetSession session = Session;
                NetPlayer player;
                return session != null && session.Roster.TryGetBySeat(SeatIndex, out player) ? player : null;
            }
        }

        /// <summary>The colour every peer draws this player in: the roster's, else the seat's palette colour.</summary>
        public Color PlayerColor
        {
            get
            {
                NetPlayer player = Player;
                return player != null ? player.UnityColor() : PlayerColors.ForSeat(SeatIndex).ToColor32();
            }
        }

        /// <summary>The name to show for this player: the roster's, else "Player N".</summary>
        public string PlayerName
        {
            get
            {
                NetPlayer player = Player;
                return player != null && !string.IsNullOrWhiteSpace(player.DisplayName) ? player.DisplayName : "Player " + (SeatIndex + 1);
            }
        }

        /// <summary>Server side: records the owner's seat, by which every peer finds the player in the roster.</summary>
        private void AssignIdentity()
        {
            NetSession session = Session;
            Seat seat;
            SeatIndex = session != null && session.TryGetSeat(OwnerClientId, out seat) ? seat.Index : (int)OwnerClientId;
        }
    }
}
