using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 善區（圓形安全區）。只負責位置、大小與顯示；玩家在不在裡面、要不要扣血由 FoolishBossController 統一判斷
    /// （多個善區重疊或相鄰時，用 Trigger 進出事件會互相覆蓋，所以不用 Trigger）。
    /// 善區只擋惡區傷害，不擋癡的攻擊。
    ///
    /// 兩種外觀（佔位，正式特效之後再換）：
    ///   · 預告：呼吸閃動的金色細圈，讓玩家提早知道下一輪善區在哪（此時還不受保護）。
    ///   · 生效：貼地的實心亮金色圓盤，站在裡面不受惡區傷害。
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        [Header("佔位外觀")]
        [Tooltip("圓盤中心離地高度。要比惡區平面高，否則會被蓋住。")]
        [SerializeField] private float _height = 0.07f;
        [Tooltip("生效時圓盤的厚度（公尺）。")]
        [SerializeField] private float _thickness = 0.04f;
        [Tooltip("生效時的顏色。")]
        [SerializeField] private Color _color = new Color(1f, 0.86f, 0.4f, 1f);

        [Header("預告外觀")]
        [Tooltip("預告細圈閃動時較暗的那一端顏色（亮的一端用上面的生效顏色）。")]
        [SerializeField] private Color _previewDimColor = new Color(0.55f, 0.42f, 0.12f, 1f);
        [Tooltip("預告細圈呼吸閃動的速度。")]
        [SerializeField] private float _previewPulseSpeed = 5f;
        [Tooltip("預告細圈的內徑占外徑的比例：越小圈越粗。")]
        [SerializeField, Range(0.5f, 0.98f)] private float _previewRingInnerRatio = 0.88f;

        /// <summary>場景裡這個物件原本的半徑（以 XZ 縮放的一半計算）。</summary>
        public float SceneRadius { get; private set; }

        /// <summary>目前半徑。</summary>
        public float Radius { get; private set; }

        public bool IsShown => gameObject.activeSelf;

        /// <summary>目前是預告狀態（還沒生效，不提供保護）。</summary>
        public bool IsPreview { get; private set; }

        private bool _initialized;
        private MeshFilter _filter;
        private Renderer _renderer;
        private Mesh _ringMesh;

        private void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            SceneRadius = Mathf.Max(0.1f, Mathf.Max(transform.localScale.x, transform.localScale.z) * 0.5f);
            Radius = SceneRadius;

            // 判定改由程式用距離計算，Collider 不需要了
            foreach (var c in GetComponents<Collider>()) c.enabled = false;

            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
            {
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                FoolishZoneVisuals.SetColor(_renderer, _color);
            }
            _ringMesh = BuildRingMesh(_previewRingInnerRatio);
        }

        private void OnDestroy()
        {
            if (_ringMesh != null) Destroy(_ringMesh);
        }

        private void Update()
        {
            if (!IsPreview || _renderer == null) return;

            // 預告：在暗金與亮金之間呼吸閃動
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * _previewPulseSpeed);
            FoolishZoneVisuals.SetColor(_renderer, Color.Lerp(_previewDimColor, _color, k));
        }

        /// <summary>取得場景設定的半徑（Awake 前也能呼叫）。</summary>
        public float GetSceneRadius()
        {
            EnsureInit();
            return SceneRadius;
        }

        /// <summary>
        /// 在指定位置（只取 XZ）以指定半徑顯示。
        /// preview = true：預告細圈（不受保護）；false：生效的實心圓盤。
        /// </summary>
        public void Show(Vector3 center, float radius, bool preview = false)
        {
            EnsureInit();
            Radius = Mathf.Max(0.1f, radius);
            IsPreview = preview;

            transform.position = new Vector3(center.x, _height, center.z);

            if (preview)
            {
                // 細圈：扁平的環，Y 縮放無意義
                if (_filter != null) _filter.sharedMesh = _ringMesh;
                transform.localScale = new Vector3(Radius * 2f, 1f, Radius * 2f);
            }
            else
            {
                // 實心圓盤：圓柱基本形狀高 2、直徑 1，Y 縮放 = 厚度的一半
                if (_filter != null) _filter.sharedMesh = FoolishZoneVisuals.GetMesh(PrimitiveType.Cylinder);
                transform.localScale = new Vector3(Radius * 2f, _thickness * 0.5f, Radius * 2f);
                if (_renderer != null) FoolishZoneVisuals.SetColor(_renderer, _color);
            }

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            EnsureInit();
            IsPreview = false;
            gameObject.SetActive(false);
        }

        /// <summary>點在 XZ 平面上是否在「已生效」的善區內。沒顯示或只是預告都不算。</summary>
        public bool Contains(Vector3 worldPoint)
        {
            if (!IsShown || IsPreview) return false;
            float dx = worldPoint.x - transform.position.x;
            float dz = worldPoint.z - transform.position.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }

        /// <summary>建立外徑 0.5（與圓柱基本形狀的直徑 1 一致）的平面圓環，法線朝上。</summary>
        private static Mesh BuildRingMesh(float innerRatio)
        {
            const int segments = 64;
            const float outer = 0.5f;
            float inner = outer * Mathf.Clamp(innerRatio, 0.5f, 0.98f);

            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                vertices[i * 2] = new Vector3(cos * outer, 0f, sin * outer);      // 外圈
                vertices[i * 2 + 1] = new Vector3(cos * inner, 0f, sin * inner);  // 內圈
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            for (int i = 0; i < segments; i++)
            {
                int o0 = i * 2, i0 = o0 + 1;
                int o1 = ((i + 1) % segments) * 2, i1 = o1 + 1;
                int t = i * 6;
                // 由上往下看是順時針，正面才朝上
                triangles[t] = o0; triangles[t + 1] = i0; triangles[t + 2] = o1;
                triangles[t + 3] = i0; triangles[t + 4] = i1; triangles[t + 5] = o1;
            }

            var mesh = new Mesh { name = "SafeZoneRing" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
