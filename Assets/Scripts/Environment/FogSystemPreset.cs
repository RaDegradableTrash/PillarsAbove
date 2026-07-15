using UnityEngine;

namespace EnvironmentSystem
{
    [CreateAssetMenu(fileName = "FogSystemPreset", menuName = "Environment/Fog System Preset")]
    public class FogSystemPreset : ScriptableObject
    {
        [Header("Volumetric Fog (Near/Mid - Aura 2)")]
        [Tooltip("The density / extinction coefficient of the volumetric fog.")]
        [Range(0f, 1f)] public float extinction = 0.15f;
        
        [Tooltip("Light scattering anisotropy (how much light scatters forward towards the camera).")]
        [Range(-1f, 1f)] public float anisotropy = 0.5f;

        [Tooltip("Maximum distance for volumetric fog calculation (in meters).")]
        [Min(10f)] public float maxDistance = 300f;

        [Tooltip("The range before maxDistance where the volumetric fog starts fading out (in meters).")]
        [Min(0f)] public float blendRange = 100f;

        [Header("Global 3D Noise")]
        public bool enableNoise = true;
        [Range(0f, 1f)] public float noiseStrength = 0.5f;
        public float noiseScale = 50f;
        public Vector3 windVelocity = new Vector3(0.5f, 0.1f, 0.2f);

        [Header("Native Height Fog (Far - Unity RenderSettings)")]
        public FogMode nativeFogMode = FogMode.ExponentialSquared;
        [Tooltip("Native height fog density for the far clipping background.")]
        [Range(0f, 0.1f)] public float nativeFogDensity = 0.01f;
        [Tooltip("Color of the far height fog (can be synced to skybox/sky color).")]
        public Color nativeFogColor = new Color(0.54f, 0.6f, 0.68f, 1f);
        [Tooltip("Used if linear fog is selected.")]
        public float nativeFogStartDistance = 200f;
        [Tooltip("Used if linear fog is selected.")]
        public float nativeFogEndDistance = 1000f;
    }
}
