using System.Collections.Generic;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// The ordered list of level assets the game plays, level 1 first. It lives in a Resources folder so the
    /// server (host or headless) finds it with <see cref="Load"/> and every level ships with the build.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelLibrary", menuName = "Unity.Cursory/Level Library")]
    public sealed class LevelLibrary : ScriptableObject
    {
        /// <summary>The library's path under a Resources folder, without the extension.</summary>
        public const string ResourcePath = "Unity.Cursory/LevelLibrary";

        public List<LevelAsset> Levels = new List<LevelAsset>();

        /// <summary>The game's library, from <see cref="ResourcePath"/>; null if it is missing.</summary>
        public static LevelLibrary Load() => Resources.Load<LevelLibrary>(ResourcePath);

        /// <summary>A loader over these levels, on level 1. Throws if a level is missing, out of order or invalid.</summary>
        public LevelLoader CreateLoader()
        {
            var data = new List<LevelData>(Levels.Count);
            foreach (var level in Levels) data.Add(level == null ? null : level.Data);
            return new LevelLoader(data);
        }
    }
}
