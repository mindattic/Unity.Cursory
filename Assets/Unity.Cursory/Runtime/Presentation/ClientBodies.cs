using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Multiplayer;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Which replicated object is which level body, on any peer. The server spawns each body keyed by its level block or
    /// shape id (<see cref="NgoBodySync"/>, Unity.Multiplayer's <see cref="NetKey"/>, UM-T60), so a peer looks each id up
    /// in its session's <see cref="NetRegistry"/>: spawn order, network object ids and joining late do not matter.
    /// </summary>
    public static class ClientBodies
    {
        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with level <paramref name="level"/>'s block and shape ids and the
        /// transforms of their replicated copies in <paramref name="objects"/>. An id with no copy on this peer yet (still
        /// arriving, or the level is being swapped) is left out.
        /// </summary>
        /// <returns>True when every block and shape of the level has its copy.</returns>
        public static bool Collect(NetRegistry objects, LevelData level, Dictionary<string, Transform> into)
        {
            into.Clear();
            if (objects == null || level == null) return false;

            foreach (Block block in level.Blocks) Add(objects, block.Id, into);
            foreach (Shape shape in level.Shapes) Add(objects, shape.Id, into);
            return into.Count == level.Blocks.Count + level.Shapes.Count;
        }

        /// <summary>A body's pose in world pixels and radians from its transform (metres, BIBLE UC-LAW-3).</summary>
        public static BodyPose PoseOf(Transform body) =>
            new BodyPose(WorldUnits.ToPixels(body.position.x), WorldUnits.ToPixels(body.position.y), body.eulerAngles.z * Mathf.Deg2Rad);

        private static void Add(NetRegistry objects, string id, Dictionary<string, Transform> into)
        {
            if (objects.TryGet(id, out Transform body) && body != null) into[id] = body;
        }
    }
}
