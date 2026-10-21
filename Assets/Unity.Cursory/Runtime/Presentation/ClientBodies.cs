using System.Collections.Generic;
using Unity.Cursory.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Which replicated object is which level body, on any peer. A networked body names nothing on the wire
    /// (<see cref="NgoBodySync"/> syncs only x, y and rotation), but the server spawns a level's bodies in the level's
    /// order, blocks then shapes (<see cref="PhysicsWorld"/>), and network object ids count up in spawn order, so the
    /// live bodies sorted by id are the level's bodies in order. While a level is being swapped (old bodies leaving,
    /// new ones arriving) the count does not match and nothing is mapped.
    /// </summary>
    public static class ClientBodies
    {
        private static readonly List<NetworkObject> s_Scratch = new List<NetworkObject>();

        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with level <paramref name="level"/>'s block and shape ids and the
        /// transforms of their replicated copies under <paramref name="manager"/>. Returns false, leaving it empty, when
        /// the live bodies do not match the level.
        /// </summary>
        public static bool Collect(NetworkManager manager, LevelData level, Dictionary<string, Transform> into)
        {
            into.Clear();
            if (manager == null || manager.SpawnManager == null || level == null) return false;

            s_Scratch.Clear();
            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || spawned.GetComponent<Rigidbody2D>() == null || spawned.GetComponent<CursorAvatar>() != null) continue;
                s_Scratch.Add(spawned);
            }

            if (s_Scratch.Count != level.Blocks.Count + level.Shapes.Count) return false;
            s_Scratch.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
            int i = 0;
            foreach (Block block in level.Blocks) into[block.Id] = s_Scratch[i++].transform;
            foreach (Shape shape in level.Shapes) into[shape.Id] = s_Scratch[i++].transform;
            return true;
        }

        /// <summary>A body's pose in world pixels and radians from its transform (metres, BIBLE UC-LAW-3).</summary>
        public static BodyPose PoseOf(Transform body) =>
            new BodyPose(WorldUnits.ToPixels(body.position.x), WorldUnits.ToPixels(body.position.y), body.eulerAngles.z * Mathf.Deg2Rad);
    }
}
