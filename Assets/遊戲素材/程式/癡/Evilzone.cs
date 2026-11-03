using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 惡區的視覺物件（佔位：暗紫黑的方形平面，正式的黑霧／腐蝕特效之後再換）。
    /// 用四條邊框長條組成：擴散階段長條從場地「邊緣向中心」變厚，推到底就蓋住整個場地。
    /// 傷害判定不在這裡：由 FoolishBossController 依「是否在惡區階段、玩家是否在善區內」計算。
    /// </summary>
    public class EvilZone : MonoBehaviour
    {
        [Header("佔位外觀")]
        [Tooltip("惡區平面的厚度（公尺）。")]
        [SerializeField] private float _thickness = 0.04f;
        [Tooltip("平面中心離地高度。善區會再高一點，避免重疊閃爍。")]
        [SerializeField] private float _height = 0.03f;
        [SerializeField] private Color _color = new Color(0.07f, 0f, 0.12f, 1f);

        private Vector2 _center;
        private float _halfSize = 35f;
        private Transform[] _strips;   // 0 = 北、1 = 南、2 = 西、3 = 東
        private bool _initialized;

        private void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;

            // 判定改由程式計算；原本的球體外觀與 Collider 都不需要了
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            var ownRenderer = GetComponent<Renderer>();
            Material sharedMat = ownRenderer != null ? ownRenderer.sharedMaterial : null;
            if (ownRenderer != null) ownRenderer.enabled = false;

            // 根物件不縮放、不旋轉，長條直接用世界座標擺放
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            var cube = FoolishZoneVisuals.GetMesh(PrimitiveType.Cube);
            _strips = new Transform[4];
            string[] names = { "Strip_N", "Strip_S", "Strip_W", "Strip_E" };
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = go.AddComponent<MeshRenderer>();
                if (sharedMat != null) r.sharedMaterial = sharedMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                FoolishZoneVisuals.SetColor(r, _color);
                _strips[i] = go.transform;
            }
        }

        /// <summary>設定場地中心（世界 XZ）與一半邊長，惡區要蓋住整個方形場地。</summary>
        public void Initialize(Vector2 center, float halfSize)
        {
            EnsureInit();
            _center = center;
            _halfSize = Mathf.Max(0.1f, halfSize);
        }

        /// <summary>顯示惡區；spread01 = 0 ~ 1 為擴散進度：從場地邊緣向中心推進，1 = 完全覆蓋。</summary>
        public void Show(float spread01 = 1f)
        {
            EnsureInit();
            gameObject.SetActive(true);

            float s = Mathf.Clamp01(spread01);
            float t = _halfSize * s;               // 邊框長條的厚度（從邊緣往內）
            float full = _halfSize * 2f;
            float cx = _center.x, cz = _center.y;

            // 北 / 南：整個寬度，厚度 t
            SetStrip(_strips[0], new Vector3(cx, _height, cz + _halfSize - t * 0.5f), new Vector3(full, _thickness, t));
            SetStrip(_strips[1], new Vector3(cx, _height, cz - _halfSize + t * 0.5f), new Vector3(full, _thickness, t));

            // 西 / 東：夾在北南之間，高度 = 剩下的中間段
            float midLength = Mathf.Max(0f, full - 2f * t);
            SetStrip(_strips[2], new Vector3(cx - _halfSize + t * 0.5f, _height, cz), new Vector3(t, _thickness, midLength));
            SetStrip(_strips[3], new Vector3(cx + _halfSize - t * 0.5f, _height, cz), new Vector3(t, _thickness, midLength));
        }

        public void Hide()
        {
            EnsureInit();
            gameObject.SetActive(false);
        }

        private static void SetStrip(Transform strip, Vector3 position, Vector3 scale)
        {
            // 太薄就不顯示，避免出現零尺寸的閃爍
            bool visible = scale.x > 0.01f && scale.z > 0.01f;
            strip.gameObject.SetActive(visible);
            if (!visible) return;
            strip.position = position;
            strip.localScale = scale;
        }
    }
}
