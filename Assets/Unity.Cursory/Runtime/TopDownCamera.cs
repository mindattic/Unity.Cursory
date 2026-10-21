using Unity.Cursory.Core;
using UnityEngine;

namespace Unity.Cursory
{
    /// <summary>
    /// Keeps the camera a top-down orthographic view of the XY plane: looking down -Z at the world,
    /// sized so zoom 1 shows one world pixel per screen pixel (<see cref="WorldUnits.OrthographicSize"/>).
    /// <see cref="CursoryClient"/> drives the position and <see cref="Zoom"/> from its <see cref="Core.CameraView"/>
    /// (UC-T11: drag empty space to pan, wheel to zoom within 0.1 to 4).
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
            Apply();
        }

        /// <summary>Applies the height and the zoom to the camera now (it also runs every frame).</summary>
        public void Apply()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            var p = transform.position;
            if (p.z != -Height) transform.position = new Vector3(p.x, p.y, -Height);
            if (_camera.pixelHeight > 0) _camera.orthographicSize = WorldUnits.OrthographicSize(_camera.pixelHeight, Zoom);
        }
    }
}
