using System.Collections.Generic;
using UnityEngine;

namespace RenderingSystem
{
    [DefaultExecutionOrder(-650)]
    public class VolumetricLightController : MonoBehaviour
    {
        [Header("Volumetric Budget Settings")]
        [Tooltip("Maximum number of concurrent volumetric lights enabled close to the player.")]
        public int maxVolumetricLights = 4;

        [Tooltip("Hard distance threshold (in meters). Beyond this distance, lights will never render volumetrically.")]
        public float maxLightDistance = 150f;

        [Tooltip("Update frequency (seconds) to recount and update nearest lights. Do not do it every frame to save CPU.")]
        [Range(0.05f, 1f)] public float updateInterval = 0.15f;
        [Tooltip("Minimum camera movement before recalculating nearest volumetric lights.")]
        [Min(0f)] public float cameraMoveThreshold = 1f;
        [Tooltip("Minimum camera rotation before recalculating nearest volumetric lights.")]
        [Min(0f)] public float cameraRotationThreshold = 5f;

        private Camera _mainCamera;
        private float _timer;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation = Quaternion.identity;
        private bool _hasCameraSample;
        private List<Light> _allLights = new List<Light>();
        private readonly List<LightDistancePair> _nearestLights = new List<LightDistancePair>(8);
        private readonly HashSet<Light> _selectedNearestLights = new HashSet<Light>();
        private readonly HashSet<Light> _previousSelectedLights = new HashSet<Light>();
        private readonly Dictionary<Light, bool> _volumetricStates = new Dictionary<Light, bool>();
        private float _cachedMaxLightDistance = -1f;
        private float _cachedMaxLightDistanceSqr;
        private float _cachedCameraMoveThreshold = -1f;
        private float _cachedCameraMoveThresholdSqr;
        private float _cachedCameraRotationThreshold = -1f;
        private float _cachedCameraRotationThresholdValue;
        private int _cachedMaxVolumetricLights = int.MinValue;
        private int _cachedLightBudget;
#if AURA_2_PRESENT
        private readonly Dictionary<Light, Aura2API.AuraLight> _auraLightCache = new Dictionary<Light, Aura2API.AuraLight>();
#endif

        private struct LightDistancePair
        {
            public Light light;
#if AURA_2_PRESENT
            public Aura2API.AuraLight auraLight;
#endif
            public float sqrDistance;
        }

        private void Start()
        {
            _mainCamera = Camera.main;
            RebuildLightCache();
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            _timer += Time.deltaTime;
            if (_timer >= updateInterval)
            {
                _timer = 0f;
                if (ShouldRefreshForCamera())
                {
                    UpdateVolumetricLights();
                }
            }
        }

        private bool ShouldRefreshForCamera()
        {
            Transform cameraTransform = _mainCamera.transform;
            if (!_hasCameraSample)
            {
                _lastCameraPosition = cameraTransform.position;
                _lastCameraRotation = cameraTransform.rotation;
                _hasCameraSample = true;
                return true;
            }

            bool moved = (cameraTransform.position - _lastCameraPosition).sqrMagnitude >= GetCameraMoveThresholdSqr();
            bool rotated = Quaternion.Angle(cameraTransform.rotation, _lastCameraRotation) >= GetCameraRotationThreshold();

            if (!moved && !rotated)
                return false;

            _lastCameraPosition = cameraTransform.position;
            _lastCameraRotation = cameraTransform.rotation;
            return true;
        }

        public void RebuildLightCache()
        {
            foreach (Light light in _previousSelectedLights)
            {
                ToggleVolumetric(light, false);
            }

            _allLights.Clear();
            _previousSelectedLights.Clear();
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                // We only care about Point and Spot lights for distance culling. Directional light is global.
                if (l != null && l.type != LightType.Directional)
                {
                    _allLights.Add(l);
                }
            }

            _hasCameraSample = false;
        }

        private void UpdateVolumetricLights()
        {
            if (_allLights.Count == 0) return;

            Vector3 camPos = _mainCamera.transform.position;
            float maxDistSqr = GetMaxLightDistanceSqr();
            int budget = GetLightBudget();
            _nearestLights.Clear();
            _selectedNearestLights.Clear();

            for (int i = _allLights.Count - 1; i >= 0; i--)
            {
                Light l = _allLights[i];
                if (l == null)
                {
                    _allLights.RemoveAt(i);
                    _volumetricStates.Remove(l);
                    continue;
                }

                if (!l.enabled || !l.gameObject.activeInHierarchy)
                {
                    ToggleVolumetric(l, false);
                    continue;
                }

                float sqrDist = (l.transform.position - camPos).sqrMagnitude;

                if (sqrDist > maxDistSqr || budget == 0)
                {
                    continue;
                }

                LightDistancePair pair = new LightDistancePair { light = l, sqrDistance = sqrDist };
#if AURA_2_PRESENT
                pair.auraLight = GetCachedAuraLight(l);
#endif
                AddNearestLight(pair, budget);
            }

            foreach (Light light in _previousSelectedLights)
            {
                if (light != null && !_selectedNearestLights.Contains(light))
                {
                    ToggleVolumetric(light, false);
                }
            }

            foreach (Light light in _selectedNearestLights)
            {
                ToggleVolumetric(light, true);
            }

            _previousSelectedLights.Clear();
            foreach (Light light in _selectedNearestLights)
            {
                _previousSelectedLights.Add(light);
            }
        }

        private void AddNearestLight(LightDistancePair pair, int budget)
        {
            int insertAt = _nearestLights.Count;
            for (int i = 0; i < _nearestLights.Count; i++)
            {
                if (pair.sqrDistance < _nearestLights[i].sqrDistance)
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt >= budget)
            {
                return;
            }

            _nearestLights.Insert(insertAt, pair);
            _selectedNearestLights.Add(pair.light);
            if (_nearestLights.Count <= budget)
            {
                return;
            }

            LightDistancePair removed = _nearestLights[_nearestLights.Count - 1];
            _nearestLights.RemoveAt(_nearestLights.Count - 1);
            _selectedNearestLights.Remove(removed.light);
        }

        private float GetMaxLightDistanceSqr()
        {
            if (!Mathf.Approximately(_cachedMaxLightDistance, maxLightDistance))
            {
                _cachedMaxLightDistance = maxLightDistance;
                _cachedMaxLightDistanceSqr = maxLightDistance * maxLightDistance;
            }

            return _cachedMaxLightDistanceSqr;
        }

        private float GetCameraMoveThresholdSqr()
        {
            if (!Mathf.Approximately(_cachedCameraMoveThreshold, cameraMoveThreshold))
            {
                _cachedCameraMoveThreshold = cameraMoveThreshold;
                float threshold = Mathf.Max(0f, cameraMoveThreshold);
                _cachedCameraMoveThresholdSqr = threshold * threshold;
            }

            return _cachedCameraMoveThresholdSqr;
        }

        private float GetCameraRotationThreshold()
        {
            if (!Mathf.Approximately(_cachedCameraRotationThreshold, cameraRotationThreshold))
            {
                _cachedCameraRotationThreshold = cameraRotationThreshold;
                _cachedCameraRotationThresholdValue = Mathf.Max(0f, cameraRotationThreshold);
            }

            return _cachedCameraRotationThresholdValue;
        }

        private int GetLightBudget()
        {
            if (_cachedMaxVolumetricLights != maxVolumetricLights)
            {
                _cachedMaxVolumetricLights = maxVolumetricLights;
                _cachedLightBudget = Mathf.Max(0, maxVolumetricLights);
            }

            return _cachedLightBudget;
        }

        private void ToggleVolumetric(Light l, bool enable)
        {
            if (l == null)
                return;

            if (_volumetricStates.TryGetValue(l, out bool currentState) && currentState == enable)
                return;

            _volumetricStates[l] = enable;

#if AURA_2_PRESENT
            Aura2API.AuraLight auraL = GetCachedAuraLight(l);

            if (auraL != null)
            {
                auraL.enabled = enable;
                return;
            }
#endif
            // Fallback action if Aura 2 is not present: 
            // We can toggle shadow casting or custom light flares, or simply do nothing.
        }

#if AURA_2_PRESENT
        private Aura2API.AuraLight GetCachedAuraLight(Light light)
        {
            if (light == null)
                return null;

            if (!_auraLightCache.TryGetValue(light, out Aura2API.AuraLight auraL))
            {
                light.TryGetComponent(out auraL);
                _auraLightCache[light] = auraL;
            }

            return auraL;
        }
#endif
    }
}
