using System;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The room's whistles, networked (Cursory's <c>Whistle</c> hub call, the game's only communication). One per scene,
    /// spawned and owned by the server (<see cref="LevelServer"/>), separate from the player cursors. Any player calls
    /// <see cref="Whistle"/>; the server checks the point and broadcasts it with the player's tone
    /// (<see cref="WhistleTone"/>) and colour, and every peer raises <see cref="Whistled"/>. The room's votes are the
    /// library's <see cref="VoteBoard"/> (UM-T54), not this.
    /// </summary>
    public partial class WhistleBoard : NetEntity
    {
        /// <summary>Most whistles a player may send a second.</summary>
        public const int WhistleRateLimit = 8;

        /// <summary>Largest whistle coordinate the server accepts, in world pixels; beyond it a whistle is clamped.</summary>
        public const float WhistleRangePx = 100000f;

        /// <summary>Whistles at <paramref name="worldPx"/> (world pixels, BIBLE UC-LAW-3): every peer sees a ripple and hears this player's tone.</summary>
        [ToServer(RequireOwnership = false, RateLimit = WhistleRateLimit)]
        public partial void Whistle(Vector2 worldPx);

        [ToClients]
        private partial void BroadcastWhistle(ulong player, Vector2 worldPx, int tone, Color32 color);

        /// <summary>Raised on every peer for each whistle, with who, where (world pixels), their tone and colour, and this peer's clock tick.</summary>
        public event Action<Core.Whistle> Whistled;

        private void OnWhistle(Vector2 worldPx, NetSender sender)
        {
            if (float.IsNaN(worldPx.x) || float.IsNaN(worldPx.y) || float.IsInfinity(worldPx.x) || float.IsInfinity(worldPx.y)) return;
            worldPx.x = Mathf.Clamp(worldPx.x, -WhistleRangePx, WhistleRangePx);
            worldPx.y = Mathf.Clamp(worldPx.y, -WhistleRangePx, WhistleRangePx);

            var tone = WhistleTone.For(sender.HasSeat ? (ulong)sender.SeatIndex : sender.ClientId);
            // The colour the roster draws the player in (UM-T55): their own, or their seat's palette colour.
            var color = Session != null && Session.Roster.TryGet(sender.ClientId, out var player)
                ? player.UnityColor()
                : new Color32(255, 255, 255, 255);
            BroadcastWhistle(sender.ClientId, worldPx, tone, color);
        }

        private void OnBroadcastWhistle(ulong player, Vector2 worldPx, int tone, Color32 color)
        {
            var clock = Session?.Clock;
            Whistled?.Invoke(new Core.Whistle
            {
                PlayerId = player,
                X = worldPx.x,
                Y = worldPx.y,
                Tone = tone,
                Color = "#" + color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2"),
                Tick = clock != null ? clock.CurrentTick : 0,
            });
        }
    }
}
