using System.IO;
using Unity.Cursory;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Unity.Cursory.EditorTools
{
    /// <summary>
    /// Writes the four prefabs <see cref="NetPrefabs"/> loads at runtime: the <see cref="CursorAvatar"/> player
    /// object, the <see cref="NgoBodySync"/> networked-body template, the <see cref="LevelStateSync"/>
    /// level-state entity and the <see cref="VoteBoard"/>. An existing prefab is overwritten in place (same file and object id), so a rerun with
    /// nothing changed leaves no diff.
    ///
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod Unity.Cursory.EditorTools.NetPrefabWriter.WriteAll
    /// </summary>
    public static class NetPrefabWriter
    {
        public const string Folder = "Assets/Unity.Cursory/Resources/Unity.Cursory";
        public const string CursorAvatarPath = Folder + "/CursorAvatar.prefab";
        public const string NetworkedBodyPath = Folder + "/NetworkedBody.prefab";
        public const string LevelStatePath = Folder + "/LevelState.prefab";
        public const string VoteBoardPath = Folder + "/VoteBoard.prefab";

        [MenuItem("Unity.Cursory/Write Networked Prefabs")]
        public static void WriteAll()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            WritePrefab(CursorAvatarPath, BuildCursorAvatar);
            WritePrefab(NetworkedBodyPath, () => NgoBodySync.BuildTemplate());
            WritePrefab(LevelStatePath, BuildLevelState);
            WritePrefab(VoteBoardPath, BuildVoteBoard);

            AssetDatabase.SaveAssets();
            Debug.Log("[Unity.Cursory] Wrote " + CursorAvatarPath + ", " + NetworkedBodyPath + ", " + LevelStatePath + " and " + VoteBoardPath);
        }

        private static GameObject BuildCursorAvatar()
        {
            var go = new GameObject("CursorAvatar");
            go.AddComponent<NetworkObject>();
            go.AddComponent<CursorAvatar>();
            return go;
        }

        private static GameObject BuildVoteBoard()
        {
            var go = new GameObject("VoteBoard");
            go.AddComponent<NetworkObject>();
            go.AddComponent<VoteBoard>();
            return go;
        }

        private static GameObject BuildLevelState()
        {
            var go = new GameObject("LevelState");
            go.AddComponent<NetworkObject>();
            go.AddComponent<LevelStateSync>();
            return go;
        }

        private static void WritePrefab(string path, System.Func<GameObject> build)
        {
            GameObject source = build();
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, path);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }
    }
}
