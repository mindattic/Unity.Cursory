using System.Collections.Generic;
using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Presentation (UC-T8): draws a cursor's rope with a <see cref="LineRenderer"/> in the player's colour, from
    /// <see cref="CursorAvatar.TryGetRope"/>: the anchor, every corner a segmented tether has caught, then the
    /// cursor, held within <see cref="Tuning.MaxPullPx"/> of the contact. Hidden while the cursor holds nothing.
    /// </summary>
    /// <remarks>
    /// Every cursor spawned on a peer that shows the game (a host or a client, never a dedicated server) gets one
    /// automatically through <see cref="CursorAvatar.Spawned"/>. It only reads replicated state; nothing here
    /// touches the simulation. <see cref="Color"/> starts from a fixed palette by owner until the player's profile
    /// colour arrives (UC-T11/UC-T12 set it).
    /// </remarks>
    public sealed class TetherRenderer : MonoBehaviour
    {
        /// <summary>The rope's width on screen at zoom 1, in world pixels.</summary>
        public const float WidthPx = 3f;

        private static readonly Color[] Palette =
        {
            new Color(0.95f, 0.36f, 0.30f), // red
            new Color(0.25f, 0.62f, 0.95f), // blue
            new Color(0.40f, 0.80f, 0.35f), // green
            new Color(0.98f, 0.76f, 0.22f), // yellow
            new Color(0.72f, 0.45f, 0.92f), // violet
            new Color(0.25f, 0.82f, 0.80f), // teal
        };

        private readonly List<Vector2> _rope = new List<Vector2>();
        private CursorAvatar _avatar;
        private LineRenderer _line;
        private Color _color;

        /// <summary>The line this draws with (created on first use).</summary>
        public LineRenderer Line
        {
            get
            {
                EnsureLine();
                return _line;
            }
        }

        /// <summary>The rope's colour: the player's colour.</summary>
        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                EnsureLine();
                _line.startColor = value;
                _line.endColor = value;
            }
        }

        /// <summary>The fallback colour for the cursor owned by <paramref name="ownerClientId"/> before a profile colour arrives.</summary>
        public static Color DefaultColorFor(ulong ownerClientId) => Palette[(int)(ownerClientId % (ulong)Palette.Length)];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            CursorAvatar.Spawned -= OnAvatarSpawned;
            CursorAvatar.Spawned += OnAvatarSpawned;
        }

        private static void OnAvatarSpawned(CursorAvatar avatar)
        {
            if (avatar == null || !avatar.IsClient) return;   // a dedicated server draws nothing
            if (avatar.GetComponent<TetherRenderer>() == null) avatar.gameObject.AddComponent<TetherRenderer>();
        }

        private void Awake()
        {
            _avatar = GetComponent<CursorAvatar>();
            EnsureLine();
            Color = _avatar != null ? DefaultColorFor(_avatar.OwnerClientId) : Palette[0];
        }

        private void LateUpdate()
        {
            Redraw();
        }

        /// <summary>Copies the avatar's current rope into the line now (it also runs every frame).</summary>
        public void Redraw()
        {
            EnsureLine();
            if (_avatar == null || !_avatar.IsSpawned || !_avatar.TryGetRope(_rope))
            {
                _line.positionCount = 0;
                _line.enabled = false;
                return;
            }

            _line.enabled = true;
            _line.positionCount = _rope.Count;
            for (var i = 0; i < _rope.Count; i++)
            {
                _line.SetPosition(i, new Vector3(WorldUnits.ToMeters(_rope[i].x), WorldUnits.ToMeters(_rope[i].y), 0f));
            }
        }

        private void EnsureLine()
        {
            if (_line != null) return;
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.widthMultiplier = WorldUnits.ToMeters(WidthPx);
            _line.numCapVertices = 2;
            _line.numCornerVertices = 2;
            _line.positionCount = 0;
            _line.enabled = false;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null) _line.sharedMaterial = new Material(shader);
        }
    }
}
