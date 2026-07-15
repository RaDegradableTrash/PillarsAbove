using UnityEngine;

namespace EnvironmentSystem
{
    [RequireComponent(typeof(BoxCollider))]
    public class LocalFogVolume : MonoBehaviour
    {
        [Header("Fog Settings")]
        [Range(0f, 1f)] public float targetExtinction = 0.5f;
        public Color volumeColor = new Color(0.4f, 0.45f, 0.5f, 1f);

        [Header("Fade settings")]
        [Tooltip("The margin distance inside the collider bounds where the fog fades to full strength.")]
        public float fadeMargin = 5f;

        [Header("Performance")]
        [SerializeField, Min(0.02f)] private float localFogRefreshInterval = 0.05f;
        [SerializeField, Min(0.05f)] private float outsideVolumeCheckInterval = 0.15f;

        private BoxCollider _boxCollider;
        private Camera _mainCamera;
        private Transform _cameraTransform;
        private AuraFogSystemManager _fogManager;
        private float _nextFogManagerLookupTime;
        private float _nextLocalFogApplyTime;
        private float _nextContainmentCheckTime;
        private bool _cameraInsideVolume;
        private Vector3 _cachedColliderCenter;
        private Vector3 _cachedColliderExtents;
        private Vector3 _cachedWorldCenter;
        private float _cachedBroadPhaseRadiusSq;
        private Vector3 _cachedTransformPosition;
        private Quaternion _cachedTransformRotation = Quaternion.identity;
        private Vector3 _cachedLossyScale;
        private float _cachedFadeMargin = float.NaN;
        private float _cachedLocalMargin = 1f;
        private float _cachedLocalFogRefreshInterval;
        private float _cachedOutsideVolumeCheckInterval;
        private float _lastLocalFogRefreshInterval = float.NaN;
        private float _lastOutsideVolumeCheckInterval = float.NaN;

#if AURA_2_PRESENT
        private Aura2API.AuraVolume _auraVolume;
#endif

        private void Start()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;
            _mainCamera = Camera.main;
            _cameraTransform = _mainCamera != null ? _mainCamera.transform : null;
            _fogManager = FindFirstObjectByType<AuraFogSystemManager>();
            RefreshDerivedSettings();

#if AURA_2_PRESENT
            if (!TryGetComponent(out _auraVolume))
            {
                _auraVolume = gameObject.AddComponent<Aura2API.AuraVolume>();
            }
            // Setup Aura volume properties
            _auraVolume.densityValue = targetExtinction;
            _auraVolume.colorValue = volumeColor;
#endif
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                _cameraTransform = _mainCamera != null ? _mainCamera.transform : null;
                if (_mainCamera == null) return;
            }

            float now = Time.unscaledTime;
            if (!_cameraInsideVolume && now < _nextContainmentCheckTime)
                return;

            if (_cameraTransform == null)
                _cameraTransform = _mainCamera.transform;

            RefreshDerivedSettings();

            Vector3 camPos = _cameraTransform.position;
            bool isInside = IsCameraInsideVolume(camPos);
            _cameraInsideVolume = isInside;
            _nextContainmentCheckTime = now + (isInside
                ? _cachedLocalFogRefreshInterval
                : _cachedOutsideVolumeCheckInterval);

            if (isInside)
            {
                if (now < _nextLocalFogApplyTime)
                    return;

                _nextLocalFogApplyTime = now + _cachedLocalFogRefreshInterval;

                // Calculate fade coefficient based on distance to the closest boundary of the box
                float fadeFactor = CalculateFadeFactor(camPos);
                ApplyLocalFogDensity(fadeFactor);
            }
            else
            {
                // If camera is outside, let the system handle normal preset values
#if !AURA_2_PRESENT
                // Fallback: reset fog settings towards active preset if we modified global fog
#endif
            }
        }

        private float CalculateFadeFactor(Vector3 point)
        {
            if (_boxCollider == null) return 0f;

            // Compute distance to closest face of local space AABB
            Vector3 localPoint = transform.InverseTransformPoint(point);
            Vector3 extents = _cachedColliderExtents;
            Vector3 center = _cachedColliderCenter;

            float distToX = Mathf.Min(Mathf.Abs(localPoint.x - (center.x - extents.x)), Mathf.Abs(localPoint.x - (center.x + extents.x)));
            float distToY = Mathf.Min(Mathf.Abs(localPoint.y - (center.y - extents.y)), Mathf.Abs(localPoint.y - (center.y + extents.y)));
            float distToZ = Mathf.Min(Mathf.Abs(localPoint.z - (center.z - extents.z)), Mathf.Abs(localPoint.z - (center.z + extents.z)));

            float minDist = Mathf.Min(distToX, Mathf.Min(distToY, distToZ));

            // Convert world space margin
            float localMargin = _cachedLocalMargin;
            if (localMargin <= 0.001f) return 1f;

            return Mathf.Clamp01(minDist / localMargin);
        }

        private bool IsCameraInsideVolume(Vector3 camPos)
        {
            Vector3 toCamera = camPos - _cachedWorldCenter;
            if (toCamera.sqrMagnitude > _cachedBroadPhaseRadiusSq)
                return false;

            return _boxCollider.bounds.Contains(camPos);
        }

        private void RefreshDerivedSettings()
        {
            if (_boxCollider == null)
                return;

            Vector3 size = _boxCollider.size;
            Vector3 center = _boxCollider.center;
            Vector3 lossyScale = transform.lossyScale;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            bool shapeChanged = _cachedColliderCenter != center ||
                                _cachedColliderExtents != size * 0.5f ||
                                _cachedTransformPosition != position ||
                                _cachedTransformRotation != rotation ||
                                _cachedLossyScale != lossyScale ||
                                !Mathf.Approximately(_cachedFadeMargin, fadeMargin);

            if (shapeChanged)
            {
                _cachedColliderCenter = center;
                _cachedColliderExtents = size * 0.5f;
                _cachedTransformPosition = position;
                _cachedTransformRotation = rotation;
                _cachedLossyScale = lossyScale;
                _cachedFadeMargin = fadeMargin;
                _cachedLocalMargin = transform.InverseTransformVector(new Vector3(fadeMargin, 0f, 0f)).magnitude;
                _cachedWorldCenter = transform.TransformPoint(center);
                Vector3 scaledExtents = Vector3.Scale(_cachedColliderExtents, Abs(lossyScale));
                float broadPhaseRadius = scaledExtents.magnitude;
                _cachedBroadPhaseRadiusSq = broadPhaseRadius * broadPhaseRadius;
            }

            if (!Mathf.Approximately(_lastLocalFogRefreshInterval, localFogRefreshInterval))
            {
                _lastLocalFogRefreshInterval = localFogRefreshInterval;
                _cachedLocalFogRefreshInterval = Mathf.Max(0.02f, localFogRefreshInterval);
            }

            if (!Mathf.Approximately(_lastOutsideVolumeCheckInterval, outsideVolumeCheckInterval))
            {
                _lastOutsideVolumeCheckInterval = outsideVolumeCheckInterval;
                _cachedOutsideVolumeCheckInterval = Mathf.Max(0.05f, outsideVolumeCheckInterval);
            }
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private void ApplyLocalFogDensity(float factor)
        {
#if AURA_2_PRESENT
            if (_auraVolume != null)
            {
                _auraVolume.densityValue = targetExtinction * factor;
            }
#else
            // Fallback: Boost native fog density dynamically when player walks into low-lying dense fog areas
            AuraFogSystemManager manager = ResolveFogManager();
            if (manager != null && manager.activePreset != null)
            {
                float baseDensity = manager.activePreset.nativeFogDensity;
                float boostedDensity = Mathf.Max(baseDensity, targetExtinction * 0.05f); // Scale target extinction to realistic native fog range
                RenderSettings.fogDensity = Mathf.Lerp(baseDensity, boostedDensity, factor);
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, volumeColor, factor);
            }
#endif
        }

        private AuraFogSystemManager ResolveFogManager()
        {
            if (_fogManager != null)
                return _fogManager;

            float now = Time.unscaledTime;
            if (now < _nextFogManagerLookupTime)
                return null;

            _nextFogManagerLookupTime = now + 1f;
            _fogManager = FindFirstObjectByType<AuraFogSystemManager>();
            return _fogManager;
        }

        private void OnDrawGizmos()
        {
            if (_boxCollider == null) _boxCollider = GetComponent<BoxCollider>();
            if (_boxCollider == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(volumeColor.r, volumeColor.g, volumeColor.b, 0.15f);
            Gizmos.DrawCube(_boxCollider.center, _boxCollider.size);

            Gizmos.color = new Color(volumeColor.r, volumeColor.g, volumeColor.b, 0.6f);
            Gizmos.DrawWireCube(_boxCollider.center, _boxCollider.size);
        }
    }
}
