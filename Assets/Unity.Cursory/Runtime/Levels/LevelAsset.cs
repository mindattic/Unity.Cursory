using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// One level as a ScriptableObject: a <see cref="LevelData"/> in world pixels (BIBLE UC-LAW-3). The 14 assets
    /// under <c>Assets/Unity.Cursory/Levels</c> are written from <see cref="LevelSeeds"/> (Cursory's
    /// <c>SeedLevel1..14</c>) by the <c>Unity.Cursory/Write Level Assets</c> menu. The game never changes an
    /// asset: <see cref="LevelLoader"/> plays a copy.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "Unity.Cursory/Level")]
    public sealed class LevelAsset : ScriptableObject
    {
        public LevelData Data = new LevelData();
    }
}
