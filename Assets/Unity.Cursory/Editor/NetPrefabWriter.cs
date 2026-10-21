using System.IO;
using Unity.Cursory;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Unity.Cursory.EditorTools
{
    /// <summary>
    /// Writes the two prefabs <see cref="NetPrefabs"/> loads at runtime: the <see cref="CursorAvatar"/> player
    /// object and the <see cref="NgoBodySync"/> networked-body template. An existing prefab is overwritten in
    /// place (same file and object id), so a rerun with nothing changed leaves no diff.
    ///
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod Unity.Cursory.EditorTools.NetPrefabWriter.WriteAll
    /// </summary>
    public static class NetPrefabWriter
    {
        public const string Folder = "Assets/Unity.Cursory/Resources/Unity.Cursory";
        public const string CursorAvatarPath = Folder + "/CursorAvatar.prefab";
        public const string NetworkedBodyPath = Folder + "/NetworkedBody.prefab";

        [MenuItem("Unity.Cursory/Write Networked Prefabs")]
        public static void WriteAll()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            WritePrefab(CursorAvatarPath, BuildCursorAvatar);
            WritePrefab(NetworkedBodyPath, () => NgoBodySync.BuildTemplate());

            AssetDatabase.SaveAssets();
            Debug.Log("[Unity.Cursory] Wrote " + CursorAvatarPath + " and " + NetworkedBodyPath);
        }

        private static GameObject BuildCursorAvatar()
        {
            var go = new GameObject("CursorAvatar");
            go.AddComponent<NetworkObject>();
            go.AddComponent<CursorAvatar>();
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
