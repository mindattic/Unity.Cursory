using Unity.Multiplayer;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The networked prefabs every peer ships, from a Resources folder (as <see cref="LevelLibrary"/> does), so the
    /// server (host or headless) finds them with no scene reference. <see cref="NetPrefabWriter"/> writes them.
    /// </summary>
    public static class NetPrefabs
    {
        /// <summary>The player object's resource path, without the extension (<see cref="CursorAvatar"/>).</summary>
        public const string CursorAvatarResourcePath = "Unity.Cursory/CursorAvatar";

        /// <summary>The networked-body template's resource path, without the extension (<see cref="NgoBodySync"/>).</summary>
        public const string NetworkedBodyResourcePath = "Unity.Cursory/NetworkedBody";

        /// <summary>The level-state entity's resource path, without the extension (<see cref="LevelStateSync"/>, UC-T9).</summary>
        public const string LevelStateResourcePath = "Unity.Cursory/LevelState";

        /// <summary>The scene's vote and whistle board's resource path, without the extension (<see cref="VoteBoard"/>, UC-T10).</summary>
        public const string VoteBoardResourcePath = "Unity.Cursory/VoteBoard";

        /// <summary>The vote board prefab <see cref="LevelServer"/> spawns once per session; null if it has not been written yet.</summary>
        public static GameObject LoadVoteBoard() => Resources.Load<GameObject>(VoteBoardResourcePath);

        /// <summary>The cursor player prefab; null if it has not been written yet (run the Unity.Cursory/Write Networked Prefabs menu).</summary>
        public static GameObject LoadCursorAvatar() => Resources.Load<GameObject>(CursorAvatarResourcePath);

        /// <summary>The networked-body template <see cref="NgoBodySync"/> instantiates per block/shape; null if it has not been written yet.</summary>
        public static GameObject LoadNetworkedBody() => Resources.Load<GameObject>(NetworkedBodyResourcePath);

        /// <summary>The server-owned <see cref="LevelStateSync"/> prefab <see cref="LevelServer"/> spawns; null if it has not been written yet.</summary>
        public static GameObject LoadLevelState() => Resources.Load<GameObject>(LevelStateResourcePath);

        /// <summary>
        /// Registers the cursor, networked-body, level-state and vote-board prefabs with <paramref name="session"/> (idempotent) and wraps the networked-body one in
        /// an <see cref="NgoBodySync"/>. Call before the session starts.
        /// </summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="session"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">Either prefab has not been written yet.</exception>
        public static NgoBodySync RegisterAll(NetSession session)
        {
            if (session == null) throw new System.ArgumentNullException(nameof(session));

            GameObject avatar = LoadCursorAvatar();
            if (avatar == null)
            {
                throw new System.InvalidOperationException(
                    "No " + CursorAvatarResourcePath + " prefab; run Unity.Cursory/Write Networked Prefabs first.");
            }

            GameObject body = LoadNetworkedBody();
            if (body == null)
            {
                throw new System.InvalidOperationException(
                    "No " + NetworkedBodyResourcePath + " prefab; run Unity.Cursory/Write Networked Prefabs first.");
            }

            if (!session.Prefabs.Contains(avatar))
            {
                session.Prefabs.Register(avatar);
            }

            GameObject levelState = LoadLevelState();
            if (levelState == null)
            {
                throw new System.InvalidOperationException(
                    "No " + LevelStateResourcePath + " prefab; run Unity.Cursory/Write Networked Prefabs first.");
            }

            if (!session.Prefabs.Contains(levelState))
            {
                session.Prefabs.Register(levelState);
            }

            GameObject board = LoadVoteBoard();
            if (board == null)
            {
                throw new System.InvalidOperationException(
                    "No " + VoteBoardResourcePath + " prefab; run Unity.Cursory/Write Networked Prefabs first.");
            }

            if (!session.Prefabs.Contains(board))
            {
                session.Prefabs.Register(board);
            }

            NgoBodySync.Register(session, body);
            return new NgoBodySync(session, body);
        }
    }
}
