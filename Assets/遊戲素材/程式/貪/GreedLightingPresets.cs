using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace PilgrimOfSin.Lighting
{
    /// <summary>
    /// 貪場景打光版本切換（給團隊比較用）。
    /// - Inspector 改「目前版本」會立即套用（編輯模式也可以）。
    /// - Play Mode：F6 / F7 / F8 切換版本（只在編輯器與 Development Build 有效）。
    /// 每個版本控制：各燈顏色強度、環境光、霧、後製 Profile、彩繪玻璃與光柱材質。
    /// 定案後保留此元件即可（Start 時會套用目前版本），或把選定數值寫死後移除。
    /// </summary>
    [ExecuteAlways]
    public class GreedLightingPresets : MonoBehaviour
    {
        [Serializable]
        public class LightSetting
        {
            public Color color = Color.white;
            public float intensity = 1f;
        }

        [Serializable]
        public class Preset
        {
            public string name = "新版本";
            [Header("燈光")]
            public LightSetting key = new LightSetting();
            public LightSetting moonFill = new LightSetting();
            public LightSetting rim = new LightSetting();
            public LightSetting scaleFrontFill = new LightSetting();
            public LightSetting warmAccent = new LightSetting();
            [Header("環境光（Trilight）")]
            public Color ambientSky = Color.gray;
            public Color ambientEquator = Color.gray;
            public Color ambientGround = Color.black;
            [Header("霧")]
            public Color fogColor = Color.gray;
            public float fogDensity = 0.01f;
            [Header("後製")]
            public VolumeProfile volumeProfile;
            [Header("彩繪玻璃（暫時材質）")]
            public Color glassBaseColor = Color.white;
            [ColorUsage(false, true)] public Color glassEmission = Color.white;
            [Header("光柱")]
            public Color beamMainColor = Color.white;
            public float beamMainIntensity = 0.3f;
            public Color beamSubColor = Color.white;
            public float beamSubIntensity = 0.1f;
            public Color dustColor = Color.white;
        }

        [Header("參照")]
        [SerializeField] private Light _key;
        [SerializeField] private Light _moonFill;
        [SerializeField] private Light _rim;
        [SerializeField] private Light _scaleFrontFill;
        [SerializeField] private Light[] _warmAccents;
        [SerializeField] private Volume _volume;
        [SerializeField] private Material _glassMaterial;
        [SerializeField] private Material _beamMainMaterial;
        [SerializeField] private Material _beamSubMaterial;
        [SerializeField] private ParticleSystem _dust;

        [Header("版本")]
        [Tooltip("目前套用的版本（0 起算）")]
        [SerializeField] private int _activeIndex;
        [SerializeField] private Preset[] _presets = Array.Empty<Preset>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        public int ActiveIndex => _activeIndex;
        public string ActiveName => IsValidIndex(_activeIndex) ? _presets[_activeIndex].name : "";

        private void Start() => Apply(_activeIndex);

        private void OnValidate()
        {
            if (_presets.Length == 0) return;
            _activeIndex = Mathf.Clamp(_activeIndex, 0, _presets.Length - 1);
            Apply(_activeIndex);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (!Application.isPlaying || Keyboard.current == null) return;
            if (Keyboard.current.f6Key.wasPressedThisFrame) Apply(0);
            else if (Keyboard.current.f7Key.wasPressedThisFrame) Apply(1);
            else if (Keyboard.current.f8Key.wasPressedThisFrame) Apply(2);
        }
#endif

        private bool IsValidIndex(int index) => index >= 0 && index < _presets.Length && _presets[index] != null;

        public void Apply(int index)
        {
            if (!IsValidIndex(index)) return;
            _activeIndex = index;
            var p = _presets[index];

            Set(_key, p.key);
            Set(_moonFill, p.moonFill);
            Set(_rim, p.rim);
            Set(_scaleFrontFill, p.scaleFrontFill);
            if (_warmAccents != null)
                foreach (var l in _warmAccents) Set(l, p.warmAccent);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;

            if (_volume != null && p.volumeProfile != null) _volume.sharedProfile = p.volumeProfile;

            if (_glassMaterial != null)
            {
                _glassMaterial.SetColor(BaseColorId, p.glassBaseColor);
                _glassMaterial.SetColor(EmissionColorId, p.glassEmission);
            }
            if (_beamMainMaterial != null)
            {
                _beamMainMaterial.SetColor(ColorId, p.beamMainColor);
                _beamMainMaterial.SetFloat(IntensityId, p.beamMainIntensity);
            }
            if (_beamSubMaterial != null)
            {
                _beamSubMaterial.SetColor(ColorId, p.beamSubColor);
                _beamSubMaterial.SetFloat(IntensityId, p.beamSubIntensity);
            }
            if (_dust != null)
            {
                var main = _dust.main;
                main.startColor = p.dustColor;
            }

            if (Application.isPlaying) Debug.Log($"[GreedLightingPresets] 切換到版本 {index + 1}：{p.name}");
        }

        private static void Set(Light light, LightSetting s)
        {
            if (light == null || s == null) return;
            light.color = s.color;
            light.intensity = s.intensity;
        }
    }
}
