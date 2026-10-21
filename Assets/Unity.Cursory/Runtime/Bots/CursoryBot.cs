using System.Threading.Tasks;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Cursory's bot strategy (UC-T14, on Unity.Multiplayer's bot mode since UM-T57): once the library has joined the
    /// bot by code and handed over the session, it waits for its cursor and the level's geometry, then plays its half of
    /// a <see cref="BotPlan"/> -- grab its end of the block's leading edge and pull it along the route onto the goal --
    /// until the server's synced win says the level is solved. Two of them, roles A and B, solve level 2 (Mass 2.0,
    /// which one cursor cannot move) with no messages between them: both read the same Core level data and the same
    /// replicated block.
    /// </summary>
    /// <remarks>
    /// <para>Everything else is the library's: <c>-bot A|B</c>, joining by <c>-joincodefile</c> through the rendezvous
    /// service, no menus, the timeout, and the <c>[NetBot:A] OK:</c> / <c>FAIL:</c> lines the compose smoke waits for
    /// (docs/hosting.md). After passing it keeps holding the block on the pad.</para>
    /// <para>The win is the server's own verdict: UC-T9's <see cref="LevelStateSync.LevelSolved"/>, synced to this
    /// client, for the level the bot is playing. The block's replicated position only steers the pull.</para>
    /// </remarks>
    [NetBotDriver]
    public sealed class CursoryBot : IBotDriver
    {
        /// <summary>How often the bot sends its cursor position, per second (<see cref="CursorAvatar.Move"/> takes up to 30).</summary>
        public const float MovesPerSecond = 15f;

        /// <summary>The plan it follows, once the level is known.</summary>
        public BotPlan Plan { get; private set; }

        /// <summary>The level number the server sent, once it has.</summary>
        public int LevelNumber { get; private set; }

        /// <summary>The block's centre as this client last saw it, in world pixels.</summary>
        public Vector2 BlockPosition { get; private set; }

        /// <inheritdoc />
        public async Task RunAsync(NetBot bot)
        {
            BotRole role;
            if (!BotPlan.TryParseRole(bot.Role, out role))
            {
                bot.Fail("Cursory's bots play A or B, not '" + bot.Role + "'.");
                return;
            }

            // 1. Our cursor and the level it is in.
            CursorAvatar me = null;
            await bot.Until(() => (me = CursorAvatar.Local(bot.Session)) != null && me.Geometry != null, "this bot's cursor and the level's geometry");
            LevelNumber = me.Geometry.LevelNumber;
            BotPlan plan;
            string why;
            if (!BotPlan.TryCreate(LevelDefinition(LevelNumber), out plan, out why))
            {
                bot.Fail("no plan for level " + LevelNumber + ": " + why);
                return;
            }

            Plan = plan;
            Transform block = null;
            await bot.Until(() => bot.Session.Objects.TryGet(plan.Block.Id, out block) && block != null, "the block " + plan.Block.Id);

            // 2. Grab our end of the leading edge, then pull along the route.
            BlockPosition = Pixels(block.position);
            var (gx, gy) = plan.GrabPoint(role, BlockPosition.x, BlockPosition.y);
            me.Move(new Vector2(gx, gy));
            me.Grab(plan.Block.Id, gx, gy);
            bot.Log("level " + LevelNumber + ": grabbed " + plan.Block.Id + " at (" + gx.ToString("F0") + ", " + gy.ToString("F0") + "); pulling toward " + plan.Goal.Id + ".");

            float interval = 1f / MovesPerSecond;
            while (true)
            {
                if (block == null)
                {
                    if (!bot.HasPassed) bot.Fail("the block's replicated copy went away (level reset or server gone); it was at " + Where() + ".");
                    return;
                }

                BlockPosition = Pixels(block.position);
                var (tx, ty) = plan.CursorTarget(role, BlockPosition.x, BlockPosition.y);
                me.Move(new Vector2(tx, ty));

                if (!bot.HasPassed)
                {
                    LevelStateSync state = bot.FindSpawned<LevelStateSync>();
                    if (state != null && state.LevelNumber == LevelNumber && state.LevelSolved)
                    {
                        bot.Pass("level " + LevelNumber + " solved (the server's synced win): " + plan.Block.Id + " at " + Where()
                            + " on " + plan.Goal.Id + ", joined with code " + bot.JoinCode + ".");
                    }
                    else if (bot.SecondsLeft < interval * 2f)
                    {
                        bot.Log("still pulling: the block is at " + Where() + ", waypoint " + plan.Progress + ".");
                    }
                }

                await bot.Seconds(interval);
            }
        }

        private string Where() => "(" + BlockPosition.x.ToString("F0") + ", " + BlockPosition.y.ToString("F0") + ")";

        /// <summary>The level as the build ships it (the Resources library), falling back to Core's seeds.</summary>
        private static LevelData LevelDefinition(int number)
        {
            LevelLibrary library = LevelLibrary.Load();
            if (library != null)
            {
                LevelLoader loader = library.CreateLoader();
                if (loader.Exists(number)) return loader.Definition(number);
            }

            return number >= 1 && number <= LevelSeeds.Count ? LevelSeeds.Level(number) : null;
        }

        private static Vector2 Pixels(Vector3 meters) => new Vector2(WorldUnits.ToPixels(meters.x), WorldUnits.ToPixels(meters.y));
    }
}
