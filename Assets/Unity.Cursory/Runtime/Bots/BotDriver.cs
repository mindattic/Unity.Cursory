using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>Where a <see cref="BotDriver"/> is in its run.</summary>
    public enum BotState
    {
        Idle = 0,
        WaitingForCode,
        Joining,
        WaitingForLevel,
        Pulling,
        Solved,
        Failed,
    }

    /// <summary>
    /// A headless bot client (UC-T14): joins a dedicated server by join code through the rendezvous service, waits
    /// for its cursor and the level's geometry, then plays its half of a <see cref="BotPlan"/> -- grab its end of
    /// the block's leading edge and pull it along the route onto the goal -- until the goal holds the block. Two of
    /// them, roles A and B, solve level 2 (Mass 2.0, which one cursor cannot move) with no messages between them:
    /// both read the same Core level data and the same replicated block.
    /// </summary>
    /// <remarks>
    /// <para>Client only. A player started with <c>-bot A</c> (or <c>B</c>) becomes one at scene load
    /// (<see cref="Boot"/>): it switches the scene's <see cref="NetBootstrap"/> off before its Start, so a headless
    /// bot does not serve by itself, and joins with the code from <c>-joincode</c> or, once it appears, the file
    /// <c>-joincodefile</c> names (<see cref="JoinCodeFile"/> writes it on the server). <c>-rendezvous</c> is read
    /// as usual by <see cref="NetBootstrap.BuildConfig()"/>. Tests drive one in process with <see cref="Run"/>.</para>
    /// <para>It logs <c>[BotDriver:A] OK: ...</c> once the goal is met and <c>[BotDriver:A] FAIL: ...</c> on any
    /// failure (a player then quits with exit code 1); the nightly compose smoke watches for those lines.
    /// After success it keeps holding the block on the pad and keeps running.</para>
    /// <para>The win is the server's own verdict: UC-T9's <see cref="LevelStateSync.LevelSolved"/>, synced to this
    /// client, for the level the bot is playing. The block's replicated position only steers the pull.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BotDriver : MonoBehaviour
    {
        /// <summary>Makes this player a bot: <c>-bot A</c> or <c>-bot B</c>.</summary>
        public const string BotArg = "-bot";

        /// <summary>The join code to use: <c>-joincode ABC-DEF-GHJ</c>.</summary>
        public const string JoinCodeArg = "-joincode";

        /// <summary>A file to wait for and read the join code from (<see cref="JoinCodeFile"/> writes it on the server).</summary>
        public const string JoinCodeFileArg = "-joincodefile";

        /// <summary>Seconds the whole run may take before it fails: <c>-bottimeout 180</c>.</summary>
        public const string TimeoutArg = "-bottimeout";

        /// <summary>How often the bot sends its cursor position, per second (<see cref="CursorAvatar.Move"/> takes up to 30).</summary>
        public const float MovesPerSecond = 15f;

        private NetSession _session;
        private NetConfig _joinConfig;
        private string _code;
        private string _codeFile;
        private float _deadline;
        private bool _quitOnFailure;

        /// <summary>This bot's half of the plan.</summary>
        public BotRole Role { get; private set; }

        /// <summary>Where it is in its run.</summary>
        public BotState State { get; private set; }

        /// <summary>Why it failed; null unless <see cref="State"/> is <see cref="BotState.Failed"/>.</summary>
        public string FailureReason { get; private set; }

        /// <summary>The plan it follows, once the level is known.</summary>
        public BotPlan Plan { get; private set; }

        /// <summary>The block's centre as this client last saw it, in world pixels.</summary>
        public Vector2 BlockPosition { get; private set; }

        /// <summary>The level number the server sent, once it has.</summary>
        public int LevelNumber { get; private set; }

        /// <summary>Raised once, when the bot reaches <see cref="BotState.Solved"/> or <see cref="BotState.Failed"/>.</summary>
        public event Action<BotDriver> Finished;

        /// <summary>
        /// Starts a bot on <paramref name="host"/>: <paramref name="session"/> (idle, with Cursory's prefabs
        /// registered) joins by <paramref name="code"/> (or, when that is null, by the code in
        /// <paramref name="codeFile"/> once the file exists) with <paramref name="joinConfig"/>, then plays
        /// <paramref name="role"/>. Fails after <paramref name="timeoutSeconds"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="host"/>, <paramref name="session"/> or <paramref name="joinConfig"/> is null.</exception>
        /// <exception cref="ArgumentException">Neither a code nor a code file is given.</exception>
        public static BotDriver Run(GameObject host, NetSession session, BotRole role, string code, string codeFile, NetConfig joinConfig, float timeoutSeconds)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (joinConfig == null) throw new ArgumentNullException(nameof(joinConfig));
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(codeFile))
            {
                throw new ArgumentException("A bot needs a join code or a file to read one from.", nameof(code));
            }

            BotDriver bot = host.AddComponent<BotDriver>();
            bot.Role = role;
            bot._session = session;
            bot._joinConfig = joinConfig;
            bot._code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
            bot._codeFile = codeFile;
            bot._deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeoutSeconds);
            bot.StartCoroutine(bot.Play());
            return bot;
        }

        /// <summary>
        /// Turns a player launched with <see cref="BotArg"/> into a bot. Runs after the scene's Awake and before any
        /// Start, which is what lets it keep the scene's <see cref="NetBootstrap"/> from serving by itself.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string roleText = Arg(args, BotArg);
            if (roleText == null) return;

            BotRole role;
            if (!BotPlan.TryParseRole(roleText, out role))
            {
                Debug.LogError("[BotDriver] FAIL: " + BotArg + " takes A or B, not '" + roleText + "'.");
                QuitWithFailure();
                return;
            }

            NetBootstrap bootstrap = NetBootstrap.Instance;
            if (bootstrap == null || bootstrap.Session == null)
            {
                Debug.LogError("[BotDriver:" + role + "] FAIL: the scene has no NetBootstrap to join with.");
                QuitWithFailure();
                return;
            }

            // A bot is headless (-batchmode -nographics), and a headless NetBootstrap serves by itself from its Start.
            // Disabled before Start, it never starts anything; the bot joins with the session it made in Awake.
            bootstrap.enabled = false;
            NetConfig config = bootstrap.BuildConfig();
            config.JoinCodes = JoinCodeProvider.Rendezvous;
            if (HeadlessBootstrap.IsHeadless) HeadlessBootstrap.Apply(config.TickRate);

            float timeout;
            if (!float.TryParse(Arg(args, TimeoutArg), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out timeout) || timeout <= 0f)
            {
                timeout = 180f;
            }

            string code = Arg(args, JoinCodeArg);
            string codeFile = Arg(args, JoinCodeFileArg);
            if (code == null && codeFile == null)
            {
                Debug.LogError("[BotDriver:" + role + "] FAIL: give " + JoinCodeArg + " or " + JoinCodeFileArg + ".");
                QuitWithFailure();
                return;
            }

            var holder = new GameObject("BotDriver " + role);
            DontDestroyOnLoad(holder);
            BotDriver bot = Run(holder, bootstrap.Session, role, code, codeFile, config, timeout);
            bot._quitOnFailure = !Application.isEditor;
            bot.Log("joining " + (code != null ? "with code " + code : "with the code in " + codeFile) + " via " + config.RendezvousUrl);
        }

        private IEnumerator Play()
        {
            // 1. The code.
            State = BotState.WaitingForCode;
            while (_code == null)
            {
                if (TimedOut()) yield break;
                try
                {
                    if (File.Exists(_codeFile))
                    {
                        string text = File.ReadAllText(_codeFile).Trim();
                        if (text.Length > 0) _code = text;
                    }
                }
                catch (IOException)
                {
                    // The server is still writing it; try again.
                }

                if (_code == null) yield return new WaitForSecondsRealtime(0.25f);
            }

            // 2. Join by it.
            State = BotState.Joining;
            string joinFailure = null;
            Action<string> onFailed = message => joinFailure = message;
            _session.JoinCodeFailed += onFailed;
            Task<bool> join = _session.JoinByCode(_code, _joinConfig);
            while (!join.IsCompleted)
            {
                if (TimedOut()) yield break;
                yield return null;
            }

            _session.JoinCodeFailed -= onFailed;
            if (join.IsFaulted || !join.Result)
            {
                Fail("JoinByCode(" + _code + ") failed: " + (join.IsFaulted ? join.Exception.GetBaseException().Message : joinFailure ?? "refused"));
                yield break;
            }

            // 3. Our cursor and the level it is in.
            State = BotState.WaitingForLevel;
            CursorAvatar me = null;
            while ((me = CursorAvatar.Local(_session)) == null || me.Geometry == null)
            {
                if (TimedOut()) yield break;
                yield return null;
            }

            LevelNumber = me.Geometry.LevelNumber;
            LevelData level = LevelDefinition(LevelNumber);
            BotPlan plan;
            string why;
            if (!BotPlan.TryCreate(level, out plan, out why))
            {
                Fail("no plan for level " + LevelNumber + ": " + why);
                yield break;
            }

            Plan = plan;
            Transform block = null;
            while ((block = FindBody(me.NetworkManager, plan.Block)) == null)
            {
                if (TimedOut()) yield break;
                yield return null;
            }

            // 4. Grab our end of the leading edge, then pull along the route.
            State = BotState.Pulling;
            BlockPosition = Pixels(block.position);
            var (gx, gy) = plan.GrabPoint(Role, BlockPosition.x, BlockPosition.y);
            me.Move(new Vector2(gx, gy));
            me.Grab(plan.Block.Id, gx, gy);
            Log("level " + LevelNumber + ": grabbed " + plan.Block.Id + " at (" + gx.ToString("F0") + ", " + gy.ToString("F0") + "); pulling toward " + plan.Goal.Id + ".");

            float interval = 1f / MovesPerSecond;
            LevelStateSync state = null;
            while (true)
            {
                if (block == null)
                {
                    if (State == BotState.Solved) yield break;
                    Fail("the block's replicated copy went away (level reset or server gone).");
                    yield break;
                }

                BlockPosition = Pixels(block.position);
                var (tx, ty) = plan.CursorTarget(Role, BlockPosition.x, BlockPosition.y);
                me.Move(new Vector2(tx, ty));

                if (State == BotState.Pulling)
                {
                    if (state == null) state = FindLevelState(me.NetworkManager);
                    if (state != null && state.LevelNumber == LevelNumber && state.LevelSolved)
                    {
                        Succeed("level " + LevelNumber + " solved (the server's synced win): " + plan.Block.Id + " at ("
                            + BlockPosition.x.ToString("F0") + ", " + BlockPosition.y.ToString("F0") + ") on " + plan.Goal.Id
                            + ", joined with code " + _code + ".");
                    }
                    else if (TimedOut())
                    {
                        yield break;
                    }
                }

                yield return new WaitForSecondsRealtime(interval);
            }
        }

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

        /// <summary>
        /// This client's replicated copy of <paramref name="block"/>: a body names nothing on the wire, so it is the
        /// networked body (not a cursor) nearest where the level puts that block. Null until one has spawned.
        /// </summary>
        private static Transform FindBody(NetworkManager manager, Block block)
        {
            if (manager == null || manager.SpawnManager == null) return null;
            Vector2 start = new Vector2(WorldUnits.ToMeters(block.X), WorldUnits.ToMeters(block.Y));
            Transform best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || spawned.GetComponent<CursorAvatar>() != null || spawned.GetComponent<Rigidbody2D>() == null) continue;
                float distance = ((Vector2)spawned.transform.position - start).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = spawned.transform;
                }
            }

            return best;
        }

        /// <summary>This client's copy of the server's <see cref="LevelStateSync"/> (UC-T9); null until it has spawned.</summary>
        private static LevelStateSync FindLevelState(NetworkManager manager)
        {
            if (manager == null || manager.SpawnManager == null) return null;
            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                LevelStateSync sync = spawned != null ? spawned.GetComponent<LevelStateSync>() : null;
                if (sync != null) return sync;
            }

            return null;
        }

        private static Vector2 Pixels(Vector3 meters) => new Vector2(WorldUnits.ToPixels(meters.x), WorldUnits.ToPixels(meters.y));

        private bool TimedOut()
        {
            if (Time.realtimeSinceStartup <= _deadline) return false;
            string where = State == BotState.Pulling
                ? "the block is at (" + BlockPosition.x.ToString("F0") + ", " + BlockPosition.y.ToString("F0") + "), waypoint " + Plan.Progress
                : "session " + _session.State;
            Fail("timed out in " + State + " (" + where + ").");
            return true;
        }

        private void Succeed(string message)
        {
            State = BotState.Solved;
            Log("OK: " + message);
            Finished?.Invoke(this);
        }

        private void Fail(string message)
        {
            if (State == BotState.Failed) return;
            State = BotState.Failed;
            FailureReason = message;
            Debug.LogError("[BotDriver:" + Role + "] FAIL: " + message);
            Finished?.Invoke(this);
            if (_quitOnFailure) QuitWithFailure();
        }

        private void Log(string message)
        {
            Debug.Log("[BotDriver:" + Role + "] " + message);
        }

        private static void QuitWithFailure()
        {
            if (!Application.isEditor) Application.Quit(1);
        }

        /// <summary>The value after <paramref name="name"/> (or after <c>name=</c>) in <paramref name="args"/>; null when it is absent.</summary>
        internal static string Arg(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == null) continue;
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return i + 1 < args.Length ? args[i + 1] : null;
                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return arg.Substring(name.Length + 1);
            }

            return null;
        }
    }
}
