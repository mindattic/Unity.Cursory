using UnityCursory.Core;
using UnityEngine;

namespace UnityCursory
{
    /// <summary>
    /// Keeps the camera a top-down orthographic view of the XY plane: looking down -Z at the world,
    /// sized so zoom 1 shows one world pixel per screen pixel (<see cref="WorldUnits.OrthographicSize"/>).
    /// Pan and the 0.1-4 zoom range arrive with the client presentation (UC-T11).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public sealed class TopDownCamera : MonoBehaviour
    {
        /// <summary>Distance the camera sits above the world plane, in metres.</summary>
        public const float Height = 10f;

        [Tooltip("1 = one world pixel per screen pixel; 2 shows half as much world.")]
        [Min(0.01f)]
        public float Zoom = 1f;

        Camera _camera;

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            transform.rotation = Quaternion.identity;
        }

        void LateUpdate()
        {
            var p = transform.position;
            if (p.z != -Height) transform.position = new Vector3(p.x, p.y, -Height);
            if (_camera.pixelHeight > 0) _camera.orthographicSize = WorldUnits.OrthographicSize(_camera.pixelHeight, Zoom);
        }
    }
}
