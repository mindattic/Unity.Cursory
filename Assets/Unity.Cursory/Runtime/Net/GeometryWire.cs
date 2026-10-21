using Unity.Cursory.Core;
using Unity.Netcode;

namespace Unity.Cursory
{
    /// <summary>One <see cref="Wall"/>, as an RPC carries it (BIBLE UC-LAW-4: static geometry, sent once per level).</summary>
    public struct WallSync : INetworkSerializable
    {
        public string Id;
        public float X;
        public float Y;
        public float W;
        public float H;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref W);
            serializer.SerializeValue(ref H);
        }
    }

    /// <summary>One <see cref="WorldLabel"/>, as an RPC carries it (BIBLE UC-LAW-4).</summary>
    public struct LabelSync : INetworkSerializable
    {
        public string Id;
        public float X;
        public float Y;
        public string Title;
        public string Subtitle;

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref Title);
            serializer.SerializeValue(ref Subtitle);
        }
    }

    /// <summary>Converts a <see cref="LevelGeometry"/> to and from the wire shapes <see cref="CursorAvatar.ReceiveGeometry"/> carries.</summary>
    public static class GeometryWire
    {
        public static WallSync[] Walls(LevelGeometry geometry)
        {
            var walls = new WallSync[geometry.Walls.Count];
            for (int i = 0; i < walls.Length; i++)
            {
                Wall w = geometry.Walls[i];
                walls[i] = new WallSync { Id = w.Id, X = w.X, Y = w.Y, W = w.W, H = w.H };
            }

            return walls;
        }

        public static LabelSync[] Labels(LevelGeometry geometry)
        {
            var labels = new LabelSync[geometry.Labels.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                WorldLabel l = geometry.Labels[i];
                labels[i] = new LabelSync { Id = l.Id, X = l.X, Y = l.Y, Title = l.Title, Subtitle = l.Subtitle };
            }

            return labels;
        }
    }
}
