using System;
using System.Collections.Generic; 
using UnityEngine; 
using UnityEngine.Rendering;

/// PBR-friendly day/night lighting controller for URP.
/// </summary>
[DefaultExecutionOrder(-800)]
[ExecuteAlways]
public class DayNightSkyboxController : MonoBehaviour
{
    private const string DefaultSkyboxResourcePath = "Skybox/MinecraftDayNightSkybox";

    [Header("Cycle")]
    [Min(10f)] public float dayLengthSeconds = 200f;
    [Range(0f, 1f)] public float timeOfDay = 0.23f;
    public bool autoAdvance = true;
    public bool useUnscaledTime = false;
    [Range(0f, 360f)] public float sunAzimuth = 165f;

    public Material skyboxTemplate;

    [Header("Sun")]
    public Light sunLight;
    [Min(0f)] public float daySunIntensity = 1.45f;
    [Min(0f)] public float nightSunIntensity = 0.12f;
    public Color daySunColor = new Color(1f, 0.90f, 0.76f, 1f);
    public Color sunriseSunColor = new Color(0.98f, 0.72f, 0.52f, 1f);
    public Color nightSunColor = new Color(0.30f, 0.38f, 0.58f, 1f);

    [Header("World Feedback")]
    [Tooltip("Applies low-cost world readability feedback beyond the skybox.")]
    public bool enableWorldFeedback = true;
    [Tooltip("Creates a soft runtime moon fill in play mode when no moon light is assigned.")]
    public bool autoCreateMoonLight = true;
    public Light moonLight;
    [Range(0f, 2f)] public float moonMaxIntensity = 0.58f;
    [Range(0f, 2f)] public float moonDawnDuskIntensity = 0.16f;
    public Color moonColor = new Color(0.48f, 0.62f, 1f, 1f);
    [Range(0f, 2f)] public float nightVisibilityAmbientBoost = 0.26f;
    [Range(0f, 3f)] public float duskEmissionBoost = 1.18f;
    [Range(0f, 3f)] public float nightEmissionBoost = 1.45f;

    [Header("Sunrise / Sunset Shaping")]
    [Tooltip("Reduce direct sun intensity near the horizon so sunrise/sunset appears as a clearer red disk.")]
    public bool shapeSunIntensityByElevation = true;
    [Range(0.05f, 1f)] public float horizonIntensityFloor = 0.0f;
    [Range(0.5f, 8f)] public float horizonIntensityPower = 2.0f;
    [Range(0f, 1f)] public float sunriseRedBoost = 0.35f;
    public Color sunriseRedColor = new Color(1f, 0.36f, 0.2f, 1f);

    [Header("Performance")]
    [Tooltip("Reduce runtime spikes by throttling expensive updates.")]
    public bool optimizeForStableFrameTime = true;
    [Min(0.02f)]
    [Tooltip("How often runtime lighting, fog, skybox, and shadow settings are refreshed while playing.")]
    public float cycleApplyInterval = 0.1f;

    [Min(0.5f)] public float probeRendererCacheRefreshInterval = 10f;
    [Min(8)] public int probeSyncBatchSize = 128;
    [Tooltip("Dynamically reduce shadow rendering cost when direct sunlight is weak.")]
    public bool useAdaptiveShadowBudget = true;
    [Range(128, 8192)] public int adaptiveDayShadowResolution = 2048;
    [Range(128, 8192)] public int adaptiveNightShadowResolution = 1024;
    [Range(10f, 300f)] public float adaptiveDayShadowDistance = 110f;
    [Range(10f, 300f)] public float adaptiveNightShadowDistance = 60f;
    [Tooltip("Changing shadow distance continuously can cause cascade rings. Keep disabled for stable transitions.")]
    public bool adaptShadowDistanceOverDay = false;
    [Range(1f, 30f)] public float adaptiveShadowDistanceStep = 8f;

    [Header("URP Sky Godray")]
    [Tooltip("Adds a lightweight godray contribution in the custom skybox shader when using URP.")]
    public bool enableUrpSkyboxGodray = true;
    [Range(0f, 1f)] public float urpGodrayMaxStrength = 0.38f;
    [Range(0.5f, 8f)] public float urpGodrayPower = 2.6f;
    [Range(0f, 1f)] public float urpGodrayTwilightBoost = 0.2f;
    public Color urpGodrayTint = new Color(1f, 0.82f, 0.62f, 1f);

    [Header("Shadows")]
    public bool forceRealtimeShadows = true;
    public LightShadows shadowMode = LightShadows.Soft;
    [Range(0f, 1f)] public float dayShadowStrength = 0.42f;
    [Range(0f, 1f)] public float nightShadowStrength = 0.22f;
    [Range(0f, 0.2f)] public float shadowBias = 0.065f;
    [Range(0f, 1f)] public float shadowNormalBias = 0.62f;
    [Range(0.01f, 1f)] public float shadowNearPlane = 0.2f;
    [Range(0, 8192)] public int shadowCustomResolution = 4096;

    public bool enforceQualityShadowProfile = true;
    [Range(10f, 300f)] public float qualityShadowDistance = 120f;
    [Range(0f, 3f)] public float qualityShadowNearPlaneOffset = 0.2f;
    [Range(0, 4)] public int qualityShadowCascades = 4;
    [Tooltip("Clamp runtime shadow settings to reduce cascade rings and surface shadow acne.")]
    public bool enforceShadowAntiBandingProfile = true;
    [Range(20f, 120f)] public float antiBandingShadowDistance = 60f;
    [Range(0, 4)] public int antiBandingMaxCascades = 2;
    [Range(0f, 0.2f)] public float antiBandingMinShadowBias = 0.08f;
    [Range(0f, 1f)] public float antiBandingMinShadowNormalBias = 0.85f;

    [Header("Skybox (Procedural)")]
    public Color daySkyTint = new Color(0.50f, 0.62f, 0.82f, 1f);
    public Color sunsetSkyTint = new Color(0.94f, 0.58f, 0.42f, 1f);
    public Color nightSkyTint = new Color(0.035f, 0.055f, 0.14f, 1f);
    public Color dayGroundColor = new Color(0.40f, 0.39f, 0.36f, 1f);
    public Color nightGroundColor = new Color(0.06f, 0.07f, 0.12f, 1f);

    [Range(0f, 8f)] public float dayExposure = 0.92f;
    [Range(0f, 8f)] public float nightExposure = 0.36f;
    [Range(0f, 5f)] public float dayAtmosphereThickness = 0.8f;
    [Range(0f, 5f)] public float nightAtmosphereThickness = 0.28f;
    [Range(0.001f, 0.2f)] public float sunDiskSize = 4f;
    [Range(0.0005f, 0.1f)] public float sunDiskSoftness = 0.005f;

    [Header("Ambient & Fog")]
    public bool controlAmbient = true;
    [Tooltip("For PBR consistency use Trilight. Flat ambient can remove directional cues.")]
    public bool useTrilightAmbient = false;

    public Color dayAmbientSky = new Color(0.58f, 0.66f, 0.76f, 1f);
    public Color dayAmbientEquator = new Color(0.42f, 0.39f, 0.34f, 1f);
    public Color dayAmbientGround = new Color(0.28f, 0.26f, 0.24f, 1f);

    public Color nightAmbientSky = new Color(0.18f, 0.24f, 0.42f, 1f);
    public Color nightAmbientEquator = new Color(0.14f, 0.17f, 0.28f, 1f);
    public Color nightAmbientGround = new Color(0.10f, 0.10f, 0.16f, 1f);

    [Range(0f, 2f)] public float ambientIntensity = 0.74f;
    public bool controlFog = true;
    public Color dayFog = new Color(0.48f, 0.56f, 0.64f, 1f);
    public Color nightFog = new Color(0.05f, 0.08f, 0.16f, 1f);
    public bool controlFogShape = true;
    [Range(0f, 0.03f)] public float dayFogDensity = 0.00045f;
    [Range(0f, 0.03f)] public float duskFogDensity = 0.0012f;
    [Range(0f, 0.03f)] public float nightFogDensity = 0.0022f;
    [Range(0f, 0.03f)] public float dawnFogDensity = 0.0015f;
    [Min(0f)] public float dayFogStartDistance = 130f;
    [Min(0f)] public float duskFogStartDistance = 18f;
    [Min(0f)] public float nightFogStartDistance = 45f;
    [Min(0f)] public float dawnFogStartDistance = 14f;
    [Min(10f)] public float dayFogEndDistance = 520f;
    [Min(10f)] public float duskFogEndDistance = 170f;
    [Min(10f)] public float nightFogEndDistance = 260f;
    [Min(10f)] public float dawnFogEndDistance = 145f;

    [Header("Reflections")]
    public bool controlReflection = true;
    [Range(0f, 2f)] public float dayReflectionIntensity = 0.92f;
    [Range(0f, 2f)] public float nightReflectionIntensity = 0.58f;
    [Min(1)] public int reflectionBounces = 1;

    public bool updateEnvironmentReflections = false;
    [Min(1.0f)] public float reflectionUpdateInterval = 5f;

    [Header("PBR Probe Integration")]
    [Tooltip("Automatically set dynamic renderers to sample Light Probes and Reflection Probes.")]
    public bool enforceDynamicProbeSampling = false;
    [Tooltip("When true, probe usage sync is repeated so runtime-spawned objects are also covered.")]
    public bool keepSyncingDynamicProbeSampling = true;
    [Min(0.1f)] public float probeSyncInterval = 2f;
    public bool includeInactiveRenderersForProbeSync = false;
    public bool logProbeAutoFixes = false;

    private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
    private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
    private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
    private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
    private static readonly int SunSoftnessId = Shader.PropertyToID("_SunSoftness");
    private static readonly int GodrayStrengthId = Shader.PropertyToID("_GodrayStrength");
    private static readonly int GodrayPowerId = Shader.PropertyToID("_GodrayPower");
    private static readonly int GodrayTintId = Shader.PropertyToID("_GodrayTint");
    private static readonly int AlbedoColorId = Shader.PropertyToID("_Color");
    private static readonly int CloudThresholdId = Shader.PropertyToID("_CloudThreshold");
    private static readonly int CloudDensityScaleId = Shader.PropertyToID("_CloudDensityScale");
    private static readonly int DayNightDaylightId = Shader.PropertyToID("_DayNightDaylight");
    private static readonly int DayNightNightId = Shader.PropertyToID("_DayNightNight");
    private static readonly int DayNightTwilightId = Shader.PropertyToID("_DayNightTwilight");
    private static readonly int DayNightDawnId = Shader.PropertyToID("_DayNightDawn");
    private static readonly int DayNightDuskId = Shader.PropertyToID("_DayNightDusk");
    private static readonly int DayNightVisibilityBoostId = Shader.PropertyToID("_DayNightVisibilityBoost");
    private static readonly int DayNightEmissionBoostId = Shader.PropertyToID("_DayNightEmissionBoost");

    private Material _runtimeSkybox;
    private bool _ownsSkyboxMaterial;
    private bool _skyboxHasSkyTint;
    private bool _skyboxHasGroundColor;
    private bool _skyboxHasSunColor;
    private bool _skyboxHasExposure;
    private bool _skyboxHasAtmosphereThickness;
    private bool _skyboxHasSunSize;
    private bool _skyboxHasSunSoftness;
    private bool _skyboxHasGodrayStrength;
    private bool _skyboxHasGodrayPower;
    private bool _skyboxHasGodrayTint;
    private float _reflectionTimer;
    private float _probeSyncTimer;
    private float _probeCacheRefreshTimer;
    private float _cycleApplyTimer;
    private float _cycleAccumulatedDelta;
    private int _probeSyncCursor;
    private bool _duplicateDirectionalLightsChecked;
    private float _cachedDayLengthSecondsSource = -1f;
    private float _cachedDayLengthSeconds = 10f;
    private float _cachedCycleApplyIntervalSource = -1f;
    private float _cachedCycleApplyInterval = 0.02f;
    private float _cachedProbeSyncIntervalSource = -1f;
    private float _cachedProbeSyncInterval = 0.1f;
    private float _cachedProbeRendererCacheRefreshIntervalSource = -1f;
    private float _cachedProbeRendererCacheRefreshInterval = 0.5f;
    private readonly List<Renderer> _cachedDynamicRenderers = new List<Renderer>(256);
    private readonly HashSet<Renderer> _dynamicRendererScratch = new HashSet<Renderer>();
    private readonly List<Renderer> _rendererChildBuffer = new List<Renderer>(16);
    private GameObject _runtimeMoonLightObject;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        DayNightSkyboxController existing = FindFirstObjectByType<DayNightSkyboxController>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        GameObject runtimeGo = new GameObject("DayNightSkyboxController");
        runtimeGo.AddComponent<DayNightSkyboxController>();
    }

    private void Awake()
    {
        EnsureSunLight();
        EnsureRuntimeSkybox();

        if (enforceDynamicProbeSampling)
            RebuildDynamicRendererCache();

        SyncDynamicProbeSampling(true);
        ApplyCycle(0f, true);
    }

    private void Update()
    {
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (Application.isPlaying)
        {
            if (autoAdvance)
                timeOfDay = Mathf.Repeat(timeOfDay + delta / GetDayLengthSeconds(), 1f);

            if (enforceDynamicProbeSampling && keepSyncingDynamicProbeSampling)
            {
                _probeSyncTimer += delta;
                if (optimizeForStableFrameTime)
                    _probeCacheRefreshTimer += delta;

                if (optimizeForStableFrameTime && _probeCacheRefreshTimer >= GetProbeRendererCacheRefreshInterval())
                {
                    _probeCacheRefreshTimer = 0f;
                    RebuildDynamicRendererCache();
                }

                if (_probeSyncTimer >= GetProbeSyncInterval())
                {
                    _probeSyncTimer = 0f;
                    SyncDynamicProbeSampling();
                }
            }
        }

        if (!Application.isPlaying || !optimizeForStableFrameTime)
        {
            ApplyCycle(delta, false);
            return;
        }

        _cycleApplyTimer += delta;
        _cycleAccumulatedDelta += delta;
        float interval = GetCycleApplyInterval();
        if (_cycleApplyTimer >= interval)
        {
            float elapsed = _cycleAccumulatedDelta;
            _cycleApplyTimer = 0f;
            _cycleAccumulatedDelta = 0f;
            ApplyCycle(elapsed, false);
        }
    }

    private float GetDayLengthSeconds()
    {
        if (!Mathf.Approximately(_cachedDayLengthSecondsSource, dayLengthSeconds))
        {
            _cachedDayLengthSecondsSource = dayLengthSeconds;
            _cachedDayLengthSeconds = Mathf.Max(10f, dayLengthSeconds);
        }

        return _cachedDayLengthSeconds;
    }

    private float GetCycleApplyInterval()
    {
        if (!Mathf.Approximately(_cachedCycleApplyIntervalSource, cycleApplyInterval))
        {
            _cachedCycleApplyIntervalSource = cycleApplyInterval;
            _cachedCycleApplyInterval = Mathf.Max(0.02f, cycleApplyInterval);
        }

        return _cachedCycleApplyInterval;
    }

    private float GetProbeSyncInterval()
    {
        if (!Mathf.Approximately(_cachedProbeSyncIntervalSource, probeSyncInterval))
        {
            _cachedProbeSyncIntervalSource = probeSyncInterval;
            _cachedProbeSyncInterval = Mathf.Max(0.1f, probeSyncInterval);
        }

        return _cachedProbeSyncInterval;
    }

    private float GetProbeRendererCacheRefreshInterval()
    {
        if (!Mathf.Approximately(_cachedProbeRendererCacheRefreshIntervalSource, probeRendererCacheRefreshInterval))
        {
            _cachedProbeRendererCacheRefreshIntervalSource = probeRendererCacheRefreshInterval;
            _cachedProbeRendererCacheRefreshInterval = Mathf.Max(0.5f, probeRendererCacheRefreshInterval);
        }

        return _cachedProbeRendererCacheRefreshInterval;
    }

    private void OnValidate()
    {
        dayLengthSeconds = Mathf.Max(10f, dayLengthSeconds);
        cycleApplyInterval = Mathf.Max(0.02f, cycleApplyInterval);
        reflectionUpdateInterval = Mathf.Max(0.1f, reflectionUpdateInterval);
        probeSyncInterval = Mathf.Max(0.1f, probeSyncInterval);

        dayShadowStrength = Mathf.Clamp01(dayShadowStrength);
        nightShadowStrength = Mathf.Clamp01(nightShadowStrength);
        shadowBias = Mathf.Clamp(shadowBias, 0f, 0.2f);
        shadowNormalBias = Mathf.Clamp(shadowNormalBias, 0f, 1f);
        shadowNearPlane = Mathf.Clamp(shadowNearPlane, 0.01f, 1f);
        shadowCustomResolution = Mathf.Clamp(shadowCustomResolution, 0, 8192);
        moonMaxIntensity = Mathf.Clamp(moonMaxIntensity, 0f, 2f);
        moonDawnDuskIntensity = Mathf.Clamp(moonDawnDuskIntensity, 0f, 2f);
        nightVisibilityAmbientBoost = Mathf.Clamp(nightVisibilityAmbientBoost, 0f, 2f);
        duskEmissionBoost = Mathf.Clamp(duskEmissionBoost, 0f, 3f);
        nightEmissionBoost = Mathf.Clamp(nightEmissionBoost, 0f, 3f);

        qualityShadowDistance = Mathf.Clamp(qualityShadowDistance, 10f, 300f);
        qualityShadowNearPlaneOffset = Mathf.Clamp(qualityShadowNearPlaneOffset, 0f, 3f);
        qualityShadowCascades = Mathf.Clamp(qualityShadowCascades, 0, 4);
        antiBandingShadowDistance = Mathf.Clamp(antiBandingShadowDistance, 20f, 120f);
        antiBandingMaxCascades = Mathf.Clamp(antiBandingMaxCascades, 0, 4);
        antiBandingMinShadowBias = Mathf.Clamp(antiBandingMinShadowBias, 0f, 0.2f);
        antiBandingMinShadowNormalBias = Mathf.Clamp(antiBandingMinShadowNormalBias, 0f, 1f);

        dayExposure = Mathf.Clamp(dayExposure, 0f, 8f);
        nightExposure = Mathf.Clamp(nightExposure, 0f, 8f);
        dayAtmosphereThickness = Mathf.Clamp(dayAtmosphereThickness, 0f, 5f);
        nightAtmosphereThickness = Mathf.Clamp(nightAtmosphereThickness, 0f, 5f);
        sunDiskSize = Mathf.Clamp(sunDiskSize, 0.001f, 0.2f);
        sunDiskSoftness = Mathf.Clamp(sunDiskSoftness, 0.0005f, 0.1f);

        ambientIntensity = Mathf.Clamp(ambientIntensity, 0f, 2f);
        dayFogDensity = Mathf.Clamp(dayFogDensity, 0f, 0.03f);
        duskFogDensity = Mathf.Clamp(duskFogDensity, 0f, 0.03f);
        nightFogDensity = Mathf.Clamp(nightFogDensity, 0f, 0.03f);
        dawnFogDensity = Mathf.Clamp(dawnFogDensity, 0f, 0.03f);
        dayFogStartDistance = Mathf.Max(0f, dayFogStartDistance);
        duskFogStartDistance = Mathf.Max(0f, duskFogStartDistance);
        nightFogStartDistance = Mathf.Max(0f, nightFogStartDistance);
        dawnFogStartDistance = Mathf.Max(0f, dawnFogStartDistance);
        dayFogEndDistance = Mathf.Max(10f, dayFogEndDistance);
        duskFogEndDistance = Mathf.Max(10f, duskFogEndDistance);
        nightFogEndDistance = Mathf.Max(10f, nightFogEndDistance);
        dawnFogEndDistance = Mathf.Max(10f, dawnFogEndDistance);
        dayReflectionIntensity = Mathf.Clamp(dayReflectionIntensity, 0f, 2f);
        nightReflectionIntensity = Mathf.Clamp(nightReflectionIntensity, 0f, 2f);
        reflectionBounces = Mathf.Max(1, reflectionBounces);



        if (Application.isPlaying)
        {
            if (enforceDynamicProbeSampling)
                RebuildDynamicRendererCache();

            SyncDynamicProbeSampling(true);
        }
        
        ApplyCycle(0f, true);
    }

    private void OnDestroy()
    {
        if (_ownsSkyboxMaterial && _runtimeSkybox != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(_runtimeSkybox);
            }
            else
            {
                Destroy(_runtimeSkybox);
            }
#else
            Destroy(_runtimeSkybox);
#endif
            _runtimeSkybox = null;
        }

        if (_runtimeMoonLightObject != null)
        {
            GameObject objectToDestroy = _runtimeMoonLightObject;
            _runtimeMoonLightObject = null;
            if (moonLight != null && moonLight.gameObject == objectToDestroy)
                moonLight = null;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(objectToDestroy);
            else
                Destroy(objectToDestroy);
#else
            Destroy(objectToDestroy);
#endif
        }
    }

    private void EnsureSunLight()
    {
        if (sunLight == null)
        {
            if (RenderSettings.sun != null)
            {
                sunLight = RenderSettings.sun;
            }
            else
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                
                // 1. Prioritize directional lights with "sun" in the name
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] != null && lights[i].type == LightType.Directional && lights[i].name.ToLower().Contains("sun"))
                    {
                        sunLight = lights[i];
                        break;
                    }
                }

                // 2. Fallback to any directional light
                if (sunLight == null)
                {
                    for (int i = 0; i < lights.Length; i++)
                    {
                        if (lights[i] != null && lights[i].type == LightType.Directional)
                        {
                            sunLight = lights[i];
                            break;
                        }
                    }
                }
            }

            if (sunLight != null && RenderSettings.sun == null)
                RenderSettings.sun = sunLight;
        }

        // 3. Auto-disable any other active directional lights at runtime to prevent double-sun conflicts
        if (sunLight != null && Application.isPlaying && !_duplicateDirectionalLightsChecked)
        {
            _duplicateDirectionalLightsChecked = true;
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l != null && l.type == LightType.Directional && l != sunLight && l != moonLight && l.enabled)
                {
                    if (l.name == "Building Tile Soft Fill Light")
                    {
                        continue;
                    }

                    Debug.LogWarning($"[DayNightSkyboxController] Auto-disabled duplicate Directional Light '{l.name}' to prevent lighting conflict.");
                    l.enabled = false;
                }
            }
        }
    }

    private void EnsureRuntimeSkybox()
    {

        if (_runtimeSkybox != null)
            return;

        Material source = skyboxTemplate;
        if (source == null)
            source = Resources.Load<Material>(DefaultSkyboxResourcePath);
        if (source == null)
            source = RenderSettings.skybox;

        if (source == null || !source.HasProperty(SkyTintId))
        {
            Shader procedural = Shader.Find("Skybox/Procedural");
            if (procedural == null)
                return;

            source = new Material(procedural);
        }

        _runtimeSkybox = new Material(source)
        {
            name = "Runtime_DayNightSkybox"
        };
        CacheSkyboxProperties();

        RenderSettings.skybox = _runtimeSkybox;
        _ownsSkyboxMaterial = true;
    }

    private void CacheSkyboxProperties()
    {
        _skyboxHasSkyTint = _runtimeSkybox != null && _runtimeSkybox.HasProperty(SkyTintId);
        _skyboxHasGroundColor = _runtimeSkybox != null && _runtimeSkybox.HasProperty(GroundColorId);
        _skyboxHasSunColor = _runtimeSkybox != null && _runtimeSkybox.HasProperty(SunColorId);
        _skyboxHasExposure = _runtimeSkybox != null && _runtimeSkybox.HasProperty(ExposureId);
        _skyboxHasAtmosphereThickness = _runtimeSkybox != null && _runtimeSkybox.HasProperty(AtmosphereThicknessId);
        _skyboxHasSunSize = _runtimeSkybox != null && _runtimeSkybox.HasProperty(SunSizeId);
        _skyboxHasSunSoftness = _runtimeSkybox != null && _runtimeSkybox.HasProperty(SunSoftnessId);
        _skyboxHasGodrayStrength = _runtimeSkybox != null && _runtimeSkybox.HasProperty(GodrayStrengthId);
        _skyboxHasGodrayPower = _runtimeSkybox != null && _runtimeSkybox.HasProperty(GodrayPowerId);
        _skyboxHasGodrayTint = _runtimeSkybox != null && _runtimeSkybox.HasProperty(GodrayTintId);
    }

    private void ApplyCycle(float deltaTime, bool forceReflectionUpdate)
    {
        EnsureSunLight();
        EnsureRuntimeSkybox();

        float sunElevation = Mathf.Sin((timeOfDay - 0.25f) * Mathf.PI * 2f);
        float daylight = Mathf.Clamp01((sunElevation + 0.08f) / 1.08f);
        daylight = Mathf.SmoothStep(0f, 1f, daylight);
        float twilight = 1f - Mathf.Clamp01(Mathf.Abs(sunElevation) / 0.28f);
        twilight = Mathf.SmoothStep(0f, 1f, twilight);
        float night = 1f - Mathf.Clamp01((sunElevation + 0.12f) / 0.42f);
        night = Mathf.SmoothStep(0f, 1f, night);
        float dawn = timeOfDay < 0.5f ? twilight : 0f;
        float dusk = timeOfDay >= 0.5f ? twilight : 0f;
        float sunAngle = timeOfDay * 360f - 90f;

        if (sunLight != null)
            ApplySunLighting(daylight, twilight, sunAngle, sunElevation);

        if (enableWorldFeedback)
            ApplyWorldFeedback(daylight, night, twilight, dawn, dusk, sunAngle);

        float godrayBlend = EvaluateGodrayBlend(daylight, twilight);

        ApplySkybox(daylight, twilight, godrayBlend);

        if (controlAmbient || controlFog)
            ApplyAmbientAndFog(daylight);

        if (controlReflection)
            ApplyReflections(daylight, deltaTime, forceReflectionUpdate);
    }

    public float GetNightVisibilityFactor()
    {
        float sunElevation = Mathf.Sin((timeOfDay - 0.25f) * Mathf.PI * 2f);
        float night = 1f - Mathf.Clamp01((sunElevation + 0.12f) / 0.42f);
        night = Mathf.SmoothStep(0f, 1f, night);
        float twilight = 1f - Mathf.Clamp01(Mathf.Abs(sunElevation) / 0.28f);
        twilight = Mathf.SmoothStep(0f, 1f, twilight);
        return enableWorldFeedback ? Mathf.Clamp01(night + twilight * 0.35f) : 0f;
    }

    public float GetEmissionBoost()
    {
        if (!enableWorldFeedback)
            return 1f;

        float sunElevation = Mathf.Sin((timeOfDay - 0.25f) * Mathf.PI * 2f);
        float night = 1f - Mathf.Clamp01((sunElevation + 0.12f) / 0.42f);
        night = Mathf.SmoothStep(0f, 1f, night);
        float twilight = 1f - Mathf.Clamp01(Mathf.Abs(sunElevation) / 0.28f);
        twilight = Mathf.SmoothStep(0f, 1f, twilight);
        float phaseBoost = Mathf.Lerp(1f, duskEmissionBoost, twilight);
        return Mathf.Max(phaseBoost, Mathf.Lerp(1f, nightEmissionBoost, night));
    }

    private void ApplyWorldFeedback(float daylight, float night, float twilight, float dawn, float dusk, float sunAngle)
    {
        EnsureMoonLight();
        if (moonLight != null)
        {
            moonLight.transform.rotation = Quaternion.Euler(sunAngle + 180f, sunAzimuth + 180f, 0f);
            moonLight.color = moonColor;
            moonLight.intensity = Mathf.Lerp(0f, moonMaxIntensity, night)
                                  + Mathf.Lerp(0f, moonDawnDuskIntensity, twilight);
            moonLight.shadows = LightShadows.Soft;
            moonLight.shadowStrength = Mathf.Lerp(0.08f, 0.18f, night);
            moonLight.shadowBias = Mathf.Max(shadowBias, 0.07f);
            moonLight.shadowNormalBias = Mathf.Max(shadowNormalBias, 0.68f);
            moonLight.enabled = moonLight.intensity > 0.001f;
        }

        Shader.SetGlobalFloat(DayNightDaylightId, daylight);
        Shader.SetGlobalFloat(DayNightNightId, night);
        Shader.SetGlobalFloat(DayNightTwilightId, twilight);
        Shader.SetGlobalFloat(DayNightDawnId, dawn);
        Shader.SetGlobalFloat(DayNightDuskId, dusk);
        Shader.SetGlobalFloat(DayNightVisibilityBoostId, GetNightVisibilityFactor());
        Shader.SetGlobalFloat(DayNightEmissionBoostId, GetEmissionBoost());
    }

    private void EnsureMoonLight()
    {
        if (moonLight != null || !autoCreateMoonLight || !Application.isPlaying)
            return;

        _runtimeMoonLightObject = new GameObject("Runtime_MoonFillLight");
        _runtimeMoonLightObject.transform.SetParent(transform, false);
        moonLight = _runtimeMoonLightObject.AddComponent<Light>();
        moonLight.type = LightType.Directional;
        moonLight.shadows = LightShadows.Soft;
    }

    private void ApplySunLighting(float daylight, float twilight, float sunAngle, float sunElevation)
    {
        sunLight.transform.rotation = Quaternion.Euler(sunAngle, sunAzimuth, 0f);

        float minIntensity = nightSunIntensity;
        float maxIntensity = daySunIntensity;

        float horizonAttenuation = 1f;
        if (shapeSunIntensityByElevation)
        {
            float sunAboveHorizon01 = Mathf.Clamp01((sunElevation + 0.02f) / 0.75f);
            horizonAttenuation = Mathf.Lerp(horizonIntensityFloor, 1f, Mathf.Pow(sunAboveHorizon01, horizonIntensityPower));
        }

        // --- DYNAMIC CLOUD OCCLUSION DIMMING ---
        // Dynamically queries the globally set cloud variables to evaluate overall coverage.
        // Lower thresholds and higher densities represent overcast conditions, which dims and cools the main light source.
        float cloudLightOcclusion = 1.0f;
        float cloudCoverageFactor = 0.0f;
        
        float globalThreshold = Shader.GetGlobalFloat(CloudThresholdId);
        float globalDensityScale = Shader.GetGlobalFloat(CloudDensityScaleId);
        if (globalDensityScale > 0.001f)
        {
            // High coverage corresponds to low threshold settings (0 = overcast, 1 = clear)
            cloudCoverageFactor = Mathf.Clamp01(1.0f - globalThreshold);
            // Let the sun intensity dim by up to 68% under heavy overcast skies
            cloudLightOcclusion = Mathf.Lerp(1.0f, 0.32f, cloudCoverageFactor * Mathf.Clamp01(globalDensityScale * 0.4f));
        }

        sunLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, daylight) * horizonAttenuation * cloudLightOcclusion;

        Color baseSunColor = Color.Lerp(nightSunColor, daySunColor, daylight);
        Color twilightColor = Color.Lerp(baseSunColor, sunriseSunColor, twilight);
        float redDiskBlend = Mathf.Clamp01(twilight * (1f - Mathf.Clamp01((sunElevation + 0.05f) / 0.9f)) * 1.35f * sunriseRedBoost);
        Color finalSunColor = Color.Lerp(twilightColor, sunriseRedColor, redDiskBlend);

        // Shift color towards cool sky reflection shadows under heavy cloud cover
        if (cloudCoverageFactor > 0.01f)
        {
            Color cloudShadowTint = new Color(0.75f, 0.82f, 0.94f); // Cool ambient skylight refraction
            finalSunColor = Color.Lerp(finalSunColor, finalSunColor * cloudShadowTint, cloudCoverageFactor * 0.45f);
        }

        // 🌟 Twilight Saturation Boost for dramatic Shinkai skies
        if (twilight > 0.01f)
        {
            float h, s, v;
            Color.RGBToHSV(finalSunColor, out h, out s, out v);
            s = Mathf.Clamp01(s * (1.0f + twilight * 0.20f)); // 20% saturation boost
            finalSunColor = Color.HSVToRGB(h, s, v);
        }

        sunLight.color = finalSunColor;

        // 🌟 Saturation & Contrast modifiers for the terrain
        float terrainSaturation = 1.0f;
        float terrainContrast = 1.0f;

        if (twilight > 0.01f)
        {
            terrainSaturation += twilight * 0.20f;
        }

        if (cloudCoverageFactor > 0.01f)
        {
            terrainSaturation -= cloudCoverageFactor * 0.15f;
            terrainContrast += cloudCoverageFactor * 0.25f;
        }

        Shader.SetGlobalFloat("_GlobalSaturation", terrainSaturation);
        Shader.SetGlobalFloat("_GlobalContrast", terrainContrast);

        if (!forceRealtimeShadows)
            return;

        if (shadowMode == LightShadows.Soft)
        {
            if (QualitySettings.shadows != ShadowQuality.All)
                QualitySettings.shadows = ShadowQuality.All;
        }
        else if (QualitySettings.shadows == ShadowQuality.Disable)
        {
            QualitySettings.shadows = ShadowQuality.All;
        }

        if (QualitySettings.shadowProjection != ShadowProjection.StableFit)
            QualitySettings.shadowProjection = ShadowProjection.StableFit;

        int targetShadowResolution = shadowCustomResolution;
        float targetShadowDistance = qualityShadowDistance;
        int targetShadowCascades = qualityShadowCascades;

        if (enforceShadowAntiBandingProfile)
        {
            targetShadowDistance = Mathf.Min(targetShadowDistance, antiBandingShadowDistance);
            targetShadowCascades = Mathf.Min(targetShadowCascades, antiBandingMaxCascades);
        }

        float runtimeShadowBias = shadowBias;
        float runtimeShadowNormalBias = shadowNormalBias;
        if (enforceShadowAntiBandingProfile)
        {
            runtimeShadowBias = Mathf.Max(runtimeShadowBias, antiBandingMinShadowBias);
            runtimeShadowNormalBias = Mathf.Max(runtimeShadowNormalBias, antiBandingMinShadowNormalBias);
        }

        // Keep enough bias to avoid acne while preserving readable soft contact shadows.
        runtimeShadowBias = Mathf.Max(runtimeShadowBias, 0.055f);
        runtimeShadowNormalBias = Mathf.Max(runtimeShadowNormalBias, 0.58f);

        sunLight.shadows = shadowMode;
        sunLight.shadowStrength = Mathf.Lerp(nightShadowStrength, dayShadowStrength, daylight);
        sunLight.shadowBias = runtimeShadowBias;
        sunLight.shadowNormalBias = runtimeShadowNormalBias;
        sunLight.shadowNearPlane = shadowNearPlane;
        if (sunLight.shadowCustomResolution != targetShadowResolution)
            sunLight.shadowCustomResolution = targetShadowResolution;

        if (!enforceQualityShadowProfile && !enforceShadowAntiBandingProfile)
            return;

        if (Mathf.Abs(QualitySettings.shadowDistance - targetShadowDistance) > 0.5f)
            QualitySettings.shadowDistance = targetShadowDistance;
        if (Mathf.Abs(QualitySettings.shadowNearPlaneOffset - qualityShadowNearPlaneOffset) > 0.0005f)
            QualitySettings.shadowNearPlaneOffset = qualityShadowNearPlaneOffset;
        if (QualitySettings.shadowCascades != targetShadowCascades)
            QualitySettings.shadowCascades = targetShadowCascades;
    }

    private float EvaluateGodrayBlend(float daylight, float twilight)
    {
        float referenceIntensity = Mathf.Max(0.0001f, daySunIntensity);

        float normalizedIntensity = daylight;
        if (sunLight != null)
            normalizedIntensity = Mathf.Clamp01(sunLight.intensity / referenceIntensity);

        float godrayBlend = Mathf.SmoothStep(0f, 1f, normalizedIntensity);
        godrayBlend = Mathf.Clamp01(godrayBlend + twilight * urpGodrayTwilightBoost);
        return godrayBlend;
    }

    private void ApplySkybox(float daylight, float twilight, float godrayBlend)
    {

        if (_runtimeSkybox == null)
            return;

        float twilightBlend = Mathf.SmoothStep(0f, 1f, twilight);
        Color baseSkyColor = Color.Lerp(nightSkyTint, daySkyTint, daylight);
        Color skyColor = Color.Lerp(baseSkyColor, sunsetSkyTint, twilightBlend);

        // 🌟 Twilight Sky Saturation Boost for majestic, colorful sunset horizons
        if (twilightBlend > 0.01f)
        {
            float h, s, v;
            Color.RGBToHSV(skyColor, out h, out s, out v);
            s = Mathf.Clamp01(s * (1.0f + twilightBlend * 0.20f)); // 20% saturation boost
            skyColor = Color.HSVToRGB(h, s, v);
        }

        Color groundColor = Color.Lerp(nightGroundColor, dayGroundColor, daylight);

        float exposure = Mathf.Lerp(nightExposure, dayExposure, daylight);
        float atmosphere = Mathf.Lerp(nightAtmosphereThickness, dayAtmosphereThickness, daylight);

        if (_skyboxHasSkyTint)
            _runtimeSkybox.SetColor(SkyTintId, skyColor);
        if (_skyboxHasGroundColor)
            _runtimeSkybox.SetColor(GroundColorId, groundColor);
        if (_skyboxHasSunColor)
            _runtimeSkybox.SetColor(SunColorId, sunLight != null ? sunLight.color : Color.white);
        if (_skyboxHasExposure)
            _runtimeSkybox.SetFloat(ExposureId, exposure);
        if (_skyboxHasAtmosphereThickness)
            _runtimeSkybox.SetFloat(AtmosphereThicknessId, atmosphere);
        if (_skyboxHasSunSize)
            _runtimeSkybox.SetFloat(SunSizeId, sunDiskSize);
        if (_skyboxHasSunSoftness)
            _runtimeSkybox.SetFloat(SunSoftnessId, sunDiskSoftness);
        if (_skyboxHasGodrayStrength)
        {
            float strength = enableUrpSkyboxGodray ? godrayBlend * urpGodrayMaxStrength : 0f;
            _runtimeSkybox.SetFloat(GodrayStrengthId, strength);
        }
        if (_skyboxHasGodrayPower)
            _runtimeSkybox.SetFloat(GodrayPowerId, urpGodrayPower);
        if (_skyboxHasGodrayTint)
            _runtimeSkybox.SetColor(GodrayTintId, urpGodrayTint);
    }

    private void ApplyAmbientAndFog(float daylight)
    {
        float sunElevation = Mathf.Sin((timeOfDay - 0.25f) * Mathf.PI * 2f);
        float night = 1f - Mathf.Clamp01((sunElevation + 0.12f) / 0.42f);
        night = Mathf.SmoothStep(0f, 1f, night);
        float twilight = 1f - Mathf.Clamp01(Mathf.Abs(sunElevation) / 0.28f);
        twilight = Mathf.SmoothStep(0f, 1f, twilight);
        bool isDawn = timeOfDay < 0.5f;

        // Query global cloud parameters for dynamic ambient/shadow adjustments
        float globalThreshold = Shader.GetGlobalFloat(CloudThresholdId);
        float globalDensityScale = Shader.GetGlobalFloat(CloudDensityScaleId);
        float cloudCoverageFactor = 0f;
        if (globalDensityScale > 0.001f)
        {
            cloudCoverageFactor = Mathf.Clamp01(1.0f - globalThreshold);
        }

        if (controlAmbient)
        {
            if (useTrilightAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                Color skyColor = Color.Lerp(nightAmbientSky, dayAmbientSky, daylight);
                Color equatorColor = Color.Lerp(nightAmbientEquator, dayAmbientEquator, daylight);
                Color groundColor = Color.Lerp(nightAmbientGround, dayAmbientGround, daylight);

                // Dynamically tint shadows to rich cyan/blue under heavy cloud cover
                if (cloudCoverageFactor > 0.01f)
                {
                    Color coolBlue = new Color(0.48f, 0.58f, 0.72f, 1.0f);
                    skyColor = Color.Lerp(skyColor, coolBlue * skyColor * 1.5f, cloudCoverageFactor * 0.5f);
                    equatorColor = Color.Lerp(equatorColor, coolBlue * equatorColor * 1.3f, cloudCoverageFactor * 0.5f);
                }

                RenderSettings.ambientSkyColor = skyColor;
                RenderSettings.ambientEquatorColor = equatorColor;
                RenderSettings.ambientGroundColor = groundColor;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                Color flatAmbient = Color.Lerp(nightAmbientEquator, dayAmbientEquator, daylight);
                flatAmbient.a = 1f;

                // Dynamically tint shadows to rich cyan/blue under heavy cloud cover
                if (cloudCoverageFactor > 0.01f)
                {
                    Color coolBlue = new Color(0.48f, 0.58f, 0.72f, 1.0f);
                    flatAmbient = Color.Lerp(flatAmbient, coolBlue * flatAmbient * 1.4f, cloudCoverageFactor * 0.5f);
                }

                RenderSettings.ambientLight = flatAmbient;
            }

            RenderSettings.ambientIntensity = ambientIntensity + (enableWorldFeedback ? night * nightVisibilityAmbientBoost : 0f);
        }

        if (controlFog)
        {
            Color fogCol = Color.Lerp(nightFog, dayFog, daylight);
            Color transitionFog = isDawn ? Color.Lerp(nightFog, dayFog, 0.65f) : Color.Lerp(dayFog, nightFog, 0.45f);
            fogCol = Color.Lerp(fogCol, transitionFog, twilight * 0.35f);
            if (cloudCoverageFactor > 0.01f)
            {
                Color coolFog = new Color(0.55f, 0.62f, 0.75f, 1.0f);
                fogCol = Color.Lerp(fogCol, coolFog * fogCol * 1.2f, cloudCoverageFactor * 0.4f);
            }
            RenderSettings.fogColor = fogCol;

            if (controlFogShape)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = BlendDayNightPhase(dayFogDensity, nightFogDensity, dawnFogDensity, duskFogDensity, daylight, night, twilight, isDawn);
                RenderSettings.fogStartDistance = BlendDayNightPhase(dayFogStartDistance, nightFogStartDistance, dawnFogStartDistance, duskFogStartDistance, daylight, night, twilight, isDawn);
                RenderSettings.fogEndDistance = BlendDayNightPhase(dayFogEndDistance, nightFogEndDistance, dawnFogEndDistance, duskFogEndDistance, daylight, night, twilight, isDawn);
            }
        }
    }

    private static float BlendDayNightPhase(float dayValue, float nightValue, float dawnValue, float duskValue, float daylight, float night, float twilight, bool isDawn)
    {
        float value = Mathf.Lerp(nightValue, dayValue, daylight);
        value = Mathf.Lerp(value, nightValue, night);
        return Mathf.Lerp(value, isDawn ? dawnValue : duskValue, twilight);
    }

    private void ApplyReflections(float daylight, float deltaTime, bool forceReflectionUpdate)
    {

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionBounces = reflectionBounces;
        RenderSettings.reflectionIntensity = Mathf.Lerp(nightReflectionIntensity, dayReflectionIntensity, daylight);

        if (!updateEnvironmentReflections)
            return;

        _reflectionTimer += deltaTime;
        if (forceReflectionUpdate || _reflectionTimer >= reflectionUpdateInterval)
        {
            _reflectionTimer = 0f;
            DynamicGI.UpdateEnvironment();
        }
    }

    private void SyncDynamicProbeSampling(bool forceFullPass = false)
    {
        if (!enforceDynamicProbeSampling)
            return;

        if (!optimizeForStableFrameTime)
        {
            SyncDynamicProbeSamplingFullScan();
            return;
        }

        if (_cachedDynamicRenderers.Count == 0)
            RebuildDynamicRendererCache();

        if (_cachedDynamicRenderers.Count == 0)
            return;

        int processCount = forceFullPass
            ? _cachedDynamicRenderers.Count
            : Mathf.Min(probeSyncBatchSize, _cachedDynamicRenderers.Count);

        int changedCount = 0;
        bool sawNullRenderer = false;
        for (int i = 0; i < processCount; i++)
        {
            int index = (_probeSyncCursor + i) % _cachedDynamicRenderers.Count;
            Renderer renderer = _cachedDynamicRenderers[index];
            if (renderer == null)
            {
                sawNullRenderer = true;
                continue;
            }

            if (ApplyProbeSamplingToRenderer(renderer))
                changedCount++;
        }

        _probeSyncCursor = (_probeSyncCursor + processCount) % _cachedDynamicRenderers.Count;

        if (sawNullRenderer)
            RebuildDynamicRendererCache();

        if (logProbeAutoFixes && changedCount > 0)
            Debug.Log($"[DayNightSkyboxController] Updated probe sampling on {changedCount} dynamic renderer(s).", this);
    }

    private void SyncDynamicProbeSamplingFullScan()
    {
        if (_cachedDynamicRenderers.Count == 0)
            RebuildDynamicRendererCache();

        int changedCount = 0;
        foreach (var renderer in _cachedDynamicRenderers)
        {
            if (renderer == null) continue;

            if (ApplyProbeSamplingToRenderer(renderer))
                changedCount++;
        }

        if (logProbeAutoFixes && changedCount > 0)
            Debug.Log($"[DayNightSkyboxController] Updated probe sampling on {changedCount} dynamic renderer(s).", this);
    }

    private void RebuildDynamicRendererCache()
    {
        _cachedDynamicRenderers.Clear();
        _dynamicRendererScratch.Clear();

        bool includeInactive = includeInactiveRenderersForProbeSync;

        // 1. SkinnedMeshRenderers
        SkinnedMeshRenderer[] smrs = FindObjectsByType<SkinnedMeshRenderer>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var smr in smrs)
        {
            if (smr != null && !smr.gameObject.isStatic)
                _dynamicRendererScratch.Add(smr);
        }

        // 2. Renderers under Rigidbodies
        Rigidbody[] rbs = FindObjectsByType<Rigidbody>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var rb in rbs)
        {
            if (rb == null) continue;
            _rendererChildBuffer.Clear();
            rb.GetComponentsInChildren<Renderer>(includeInactive, _rendererChildBuffer);
            for (int i = 0; i < _rendererChildBuffer.Count; i++)
            {
                Renderer r = _rendererChildBuffer[i];
                if (r != null && !r.gameObject.isStatic)
                    _dynamicRendererScratch.Add(r);
            }
        }

        // 3. Renderers under Animators
        Animator[] anims = FindObjectsByType<Animator>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var anim in anims)
        {
            if (anim == null) continue;
            _rendererChildBuffer.Clear();
            anim.GetComponentsInChildren<Renderer>(includeInactive, _rendererChildBuffer);
            for (int i = 0; i < _rendererChildBuffer.Count; i++)
            {
                Renderer r = _rendererChildBuffer[i];
                if (r != null && !r.gameObject.isStatic)
                    _dynamicRendererScratch.Add(r);
            }
        }

        _cachedDynamicRenderers.AddRange(_dynamicRendererScratch);
        _dynamicRendererScratch.Clear();
        _rendererChildBuffer.Clear();
        _probeSyncCursor = 0;
    }

    private static bool ApplyProbeSamplingToRenderer(Renderer renderer)
    {
        bool changed = false;

        if (renderer.lightProbeUsage != LightProbeUsage.BlendProbes)
        {
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            changed = true;
        }

        if (renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbes)
        {
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            changed = true;
        }

        return changed;
    }
}
