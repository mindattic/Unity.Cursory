using System.IO;
using UnityCursory.Core;
using UnityEditor;
using UnityEngine;

namespace UnityCursory.EditorTools
{
    /// <summary>
    /// Writes the 14 level assets and the level library from <see cref="LevelSeeds"/>, so the levels stay a
    /// faithful port of Cursory's <c>SeedLevel1..14</c>. An existing asset is overwritten in place (same file
    /// and object id), so a rerun with unchanged seeds leaves no diff. To change a level, edit
    /// <see cref="LevelSeeds"/>, run the menu item, and commit both; <c>dotnet test Tests/Core.Tests</c>
    /// (<c>LevelAssetTests</c>) fails while the assets and the seeds disagree.
    ///
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod UnityCursory.EditorTools.LevelAssetWriter.WriteAll
    /// </summary>
    public static class LevelAssetWriter
    {
        public const string LevelsFolder = "Assets/UnityCursory/Levels";
        public const string LibraryFolder = "Assets/UnityCursory/Resources/UnityCursory";
        public const string LibraryPath = LibraryFolder + "/LevelLibrary.asset";

        /// <summary>The asset path of level <paramref name="number"/>: L01.asset to L14.asset.</summary>
        public static string LevelPath(int number) => $"{LevelsFolder}/L{number:00}.asset";

        [MenuItem("UnityCursory/Write Level Assets")]
        public static void WriteAll()
        {
            Directory.CreateDirectory(LevelsFolder);
            Directory.CreateDirectory(LibraryFolder);
            AssetDatabase.Refresh();

            var library = AssetDatabase.LoadAssetAtPath<LevelLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<LevelLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Levels.Clear();

            foreach (var seed in LevelSeeds.All())
            {
                var path = LevelPath(seed.Number);
                var asset = AssetDatabase.LoadAssetAtPath<LevelAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<LevelAsset>();
                    asset.Data = seed;
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    asset.Data = seed;
                    EditorUtility.SetDirty(asset);
                }
                library.Levels.Add(asset);
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            library.CreateLoader(); // throws if any written level is invalid
            Debug.Log($"[UnityCursory] Wrote {library.Levels.Count} level assets to {LevelsFolder} and {LibraryPath}");
        }
    }
}
