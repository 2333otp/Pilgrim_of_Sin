using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using PilgrimOfSin.StateMachine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 武器介紹第 2 頁右邊的「小 Muir」動作展示。
    /// 執行期在遠處生成一份展示用 Muir（MovePreviewMuir.prefab，從阿貪關卡的 Muir 複製），
    /// 配專用攝影機與燈光，畫面輸出到 RenderTexture 再顯示在 UI 的 RawImage 上。
    ///   · 專用圖層 MenuPreview：遊戲攝影機看不到小 Muir，展示攝影機也只拍小 Muir。
    ///   · 暫停選單時 timeScale = 0：動畫自己用不受暫停影響的時間推進。
    ///   · 角色固定在原地（不套用 Root Motion），腳部 IK 關閉（同遊戲中 AttackClipPlayer）。
    ///   · 換武器時把這把武器的每一招都快速模擬一遍，依「所有動作＋武器會碰到的最大範圍」取景，任何一招都不會超出框。
    ///   · 地面高度用腳趾骨頭決定（不用模型外框，待機時下垂的武器會把外框拉低），影子跟著腰部移動。
    ///   · 只在第 2 頁開著時才開攝影機，離開就關掉，不浪費效能。
    /// </summary>
    public class MovePreviewStage : MonoBehaviour
    {
        [Header("展示用 Muir（PilgrimOfSin/UI/重建武器介紹版面 會自動產生）")]
        [SerializeField] private GameObject _muirPrefab;
        [SerializeField] private RawImage _target;

        [Header("展示台")]
        [Tooltip("展示台放在世界的哪裡（遠離場景，避免被場景物件擋到或影響）")]
        [SerializeField] private Vector3 _stagePosition = new Vector3(0f, -3000f, 0f);
        [SerializeField] private string _layerName = "MenuPreview";
        [SerializeField] private Vector2Int _textureSize = new Vector2Int(1344, 1080);

        [Header("攝影機（依這把武器所有招式的範圍自動取景）")]
        [Tooltip("攝影機從哪個水平方向看角色（Y 軸角度，0 = 從正前方 +Z 看）")]
        [SerializeField] private float _cameraYaw = 20f;
        [Tooltip("攝影機往下看的角度（度）。稍微俯視，地面比較有立體感。")]
        [SerializeField] private float _cameraPitch = 8f;
        [Tooltip("取景時四周多留的空間（1.15 = 多 15%）")]
        [SerializeField] private float _frameMargin = 1.08f;
        [Tooltip("畫面高度最少是身高的幾倍（避免角色被放太大）")]
        [SerializeField] private float _minViewToBody = 1.7f;
        [Tooltip("畫面高度最多是身高的幾倍（避免長武器讓角色被縮太小）")]
        [SerializeField] private float _maxViewToBody = 2.3f;
        [Tooltip("取景模擬時每一招取樣幾個時間點")]
        [SerializeField] private int _framingSamples = 10;
        [SerializeField] private float _fieldOfView = 30f;
        [Tooltip("角色面向（Y 軸角度）。0 = 面向 +Z。")]
        [SerializeField] private float _modelYaw = 0f;

        [Header("背景與地面")]
        [Tooltip("展示畫面的底色（URP 輸出的畫面不透明，所以底色直接畫在攝影機上；四周的暗角由 UI 疊上去）")]
        [SerializeField] private Color _backgroundColor = new Color32(0x6A, 0x52, 0x3A, 0xFF);
        [Tooltip("腳下的影子（平放在地面的貼圖），跟著腰部移動")]
        [SerializeField] private Sprite _groundShadow;
        [Tooltip("影子寬度 = 身高 × 這個比例")]
        [SerializeField] private float _shadowWidthRatio = 0.42f;
        [Tooltip("地面光（平放在地面、中間亮四周淡的貼圖），讓角色有站在台上的感覺")]
        [SerializeField] private Sprite _floorGlow;
        [SerializeField] private Color _floorGlowColor = new Color(1f, 0.82f, 0.6f, 0.22f);
        [Tooltip("地面光寬度 = 身高 × 這個比例")]
        [SerializeField] private float _floorGlowWidthRatio = 2.2f;

        [Header("燈光")]
        [SerializeField] private Color _keyColor = new Color(1f, 0.9f, 0.75f);
        [SerializeField] private float _keyIntensity = 120f;
        [SerializeField] private Color _rimColor = new Color(1f, 0.78f, 0.5f);
        [SerializeField] private float _rimIntensity = 60f;

        [Header("動畫")]
        [SerializeField] private float _fadeSeconds = 0.1f;

        private Transform _stage;
        private GameObject _muir;
        private Animator _animator;
        private PlayerWeaponVisual _weaponVisual;
        private Camera _camera;
        private RenderTexture _rt;
        private int _layer = -1;
        private int _equipped = -1;
        private Transform _shadow, _floor, _hips;
        private float _groundY;
        private float _scratchTop;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _slots = new AnimationClipPlayable[2];
        private readonly float[] _weight = new float[2];
        private readonly float[] _speed = new float[2];
        private readonly bool[] _loop = new bool[2];
        private int _active = -1;

        private static readonly HumanBodyBones[] FootBones =
            { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

        // ─────────────────────────── 對外 ───────────────────────────

        /// <summary>
        /// 打開展示：換成指定武器（1~4），並依這把武器的招式（framingClips：待機＋所有招式）取景。
        /// </summary>
        public void Show(int weaponIndex, IList<AnimationClip> framingClips)
        {
            EnsureStage();
            if (_stage == null) return;
            _stage.gameObject.SetActive(true);
            _camera.enabled = true;
            if (_target != null) { _target.texture = _rt; _target.enabled = true; }
            if (_equipped != weaponIndex && _weaponVisual != null)
            {
                _weaponVisual.EquipWeapon(weaponIndex);
                SetLayerRecursive(_muir.transform, _layer);
                _equipped = weaponIndex;
            }
            FrameFor(framingClips);
        }

        public void Hide()
        {
            if (_camera != null) _camera.enabled = false;
            if (_stage != null) _stage.gameObject.SetActive(false);
            if (_target != null) _target.enabled = false;
        }

        /// <summary>播一段動畫。loop = 待機動畫一直循環；招式播完停在最後一格。</summary>
        public void Play(AnimationClip clip, float speed = 1f, bool loop = false)
        {
            if (clip == null || !_graph.IsValid()) return;

            int slot = _active < 0 ? 0 : 1 - _active;
            if (_slots[slot].IsValid())
            {
                _graph.Disconnect(_mixer, slot);
                _slots[slot].Destroy();
            }
            var p = AnimationClipPlayable.Create(_graph, clip);
            p.SetApplyFootIK(false);
            p.SetTime(0);
            _graph.Connect(p, 0, _mixer, slot);
            _slots[slot] = p;
            _speed[slot] = Mathf.Max(0.1f, speed);
            _loop[slot] = loop;
            _weight[slot] = _active < 0 ? 1f : 0f;
            _active = slot;
            ResetPose();
        }

        // ─────────────────────────── 每幀 ───────────────────────────

        private void LateUpdate()
        {
            if (!_graph.IsValid() || _active < 0 || _camera == null || !_camera.enabled) return;

            float dt = Time.unscaledDeltaTime;
            float step = _fadeSeconds > 0.0001f ? dt / _fadeSeconds : 1f;
            for (int i = 0; i < 2; i++)
            {
                if (!_slots[i].IsValid()) continue;
                float target = i == _active ? 1f : 0f;
                _weight[i] = Mathf.MoveTowards(_weight[i], target, step);

                var clip = _slots[i].GetAnimationClip();
                double t = _slots[i].GetTime() + dt * _speed[i];
                double len = clip != null ? clip.length : 0d;
                if (len > 0d) t = _loop[i] ? t % len : System.Math.Min(t, len);
                _slots[i].SetTime(t);
                _mixer.SetInputWeight(i, _weight[i]);
            }
            _graph.Evaluate(0f);
            ResetPose();
            KeepFeetOnGround();
            FollowShadow();
        }

        /// <summary>角色固定在展示台中央（招式動畫的位移不套用）。</summary>
        private void ResetPose()
        {
            if (_muir == null) return;
            _muir.transform.localPosition = Vector3.zero;
            _muir.transform.localRotation = Quaternion.Euler(0f, _modelYaw, 0f);
        }

        /// <summary>
        /// 腳低於地面時把整個角色往上推回地面。
        /// 有些動作（例如秘技）在遊戲中靠根運動補償高度，展示台把角色固定在原地後身體會整個沉下去；
        /// 只往上推、不往下拉，所以跳起來的動作照樣看得到。
        /// </summary>
        private void KeepFeetOnGround()
        {
            if (_animator == null) return;
            float lowest = LowestFootY();
            if (lowest < _groundY) _muir.transform.position += Vector3.up * (_groundY - lowest);
        }

        private float LowestFootY()
        {
            float lowest = float.MaxValue;
            foreach (var fb in FootBones)
            {
                var t = _animator.GetBoneTransform(fb);
                if (t != null) lowest = Mathf.Min(lowest, t.position.y);
            }
            return lowest;
        }

        /// <summary>影子跟著腰部的水平位置，永遠貼在地面上。</summary>
        private void FollowShadow()
        {
            if (_shadow == null || _hips == null) return;
            _shadow.position = new Vector3(_hips.position.x, _groundY + 0.005f, _hips.position.z);
        }

        // ─────────────────────────── 取景 ───────────────────────────

        /// <summary>
        /// 把每一招都快速模擬一遍，收集所有骨頭與武器會碰到的位置，算出攝影機要多遠才裝得下全部。
        /// 地面 = 所有取樣裡腳趾／腳掌骨頭的最低點。
        /// </summary>
        private void FrameFor(IList<AnimationClip> clips)
        {
            if (_animator == null || !_graph.IsValid()) return;

            var rot = Quaternion.Euler(_cameraPitch, _cameraYaw + 180f, 0f);   // 從 yaw 方向看向角色
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
            var pts = new List<Vector3>(512);
            float ground = float.MaxValue;
            float headTop = float.MinValue;

            // 暫時用 slot 0 取樣，結束後清掉，由呼叫端重新播待機
            for (int i = 0; i < 2; i++)
                if (_slots[i].IsValid()) { _graph.Disconnect(_mixer, i); _slots[i].Destroy(); }
            _active = -1;

            int n = Mathf.Max(2, _framingSamples);
            if (clips != null)
            {
                // 第一段是待機：地面 = 待機時腳的最低點（其他招式的腳低於這裡時會被推回地面）
                _groundY = float.MaxValue;
                for (int ci = 0; ci < clips.Count; ci++)
                {
                    var clip = clips[ci];
                    if (clip == null) continue;
                    var p = AnimationClipPlayable.Create(_graph, clip);
                    p.SetApplyFootIK(false);
                    _graph.Connect(p, 0, _mixer, 0);
                    _mixer.SetInputWeight(0, 1f);
                    for (int s = 0; s < n; s++)
                    {
                        p.SetTime(clip.length * s / (n - 1));
                        _graph.Evaluate(0f);
                        ResetPose();
                        if (ci == 0) ground = Mathf.Min(ground, LowestFootY());
                        else
                        {
                            if (_groundY == float.MaxValue) _groundY = ground < float.MaxValue ? ground : _stage.position.y;
                            KeepFeetOnGround();
                        }
                        // 身高只用待機姿勢量（有些招式會跳很高，不能算進身高）
                        if (ci == 0) CollectPoints(pts, ref ground, ref headTop);
                        else CollectPoints(pts, ref ground, ref _scratchTop);
                    }
                    _graph.Disconnect(_mixer, 0);
                    p.Destroy();
                }
            }
            if (pts.Count == 0) return;

            if (_groundY == float.MaxValue) _groundY = ground < float.MaxValue ? ground : _stage.position.y;
            float height = Mathf.Max(0.5f, headTop - _groundY);
            // 地面上腳的前後左右也要看得到一點，讓影子不被切掉
            var c0 = new Vector3(_stage.position.x, _groundY, _stage.position.z);
            float pad = height * 0.25f;
            pts.Add(c0 + Vector3.right * pad); pts.Add(c0 - Vector3.right * pad);
            pts.Add(c0 + Vector3.forward * pad); pts.Add(c0 - Vector3.forward * pad);

            // 投影到攝影機的左右／上下軸，找出範圍（攝影機距離遠，近似成正交投影）
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var origin = pts[0];
            foreach (var pt in pts)
            {
                var d = pt - origin;
                float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float aspect = (float)_textureSize.x / _textureSize.y;
            float viewH = Mathf.Max(maxY - minY, (maxX - minX) / aspect) * _frameMargin;
            var look = origin + right * ((minX + maxX) * 0.5f) + up * ((minY + maxY) * 0.5f);

            // 四把武器的角色大小要一致：畫面高度限制在身高的 _minViewToBody ~ _maxViewToBody 倍之間。
            // 被限制時改以角色為中心取景（腳在畫面下方、身體一定在框內；極長的武器尖端偶爾可能碰到框邊）。
            float clamped = Mathf.Clamp(viewH, height * _minViewToBody, height * _maxViewToBody);
            if (!Mathf.Approximately(clamped, viewH))
            {
                viewH = clamped;
                float centerY = _groundY - height * 0.12f + viewH * 0.5f;
                look = new Vector3(_stage.position.x, centerY, _stage.position.z);
            }
            float dist = viewH * 0.5f / Mathf.Tan(_fieldOfView * 0.5f * Mathf.Deg2Rad) + height * 0.2f;

            _camera.transform.rotation = rot;
            _camera.transform.position = look - (rot * Vector3.forward) * dist;
            _camera.farClipPlane = dist + height * 4f;

            if (_shadow != null)
            {
                float size = _groundShadow.bounds.size.x;
                float sc = size > 0f ? height * _shadowWidthRatio / size : 1f;
                _shadow.localScale = new Vector3(sc, sc, sc);
            }
            if (_floor != null)
            {
                _floor.position = new Vector3(_stage.position.x, _groundY + 0.002f, _stage.position.z);
                float size = _floorGlow.bounds.size.x;
                float sc = size > 0f ? height * _floorGlowWidthRatio / size : 1f;
                _floor.localScale = new Vector3(sc, sc, sc);
            }
            FollowShadow();
        }

        private void CollectPoints(List<Vector3> pts, ref float ground, ref float headTop)
        {
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = _animator.GetBoneTransform(b);
                if (t != null) pts.Add(t.position);
            }
            foreach (var fb in FootBones)
            {
                var t = _animator.GetBoneTransform(fb);
                if (t != null) ground = Mathf.Min(ground, t.position.y);
            }
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (head != null)
            {
                // 頭骨在頭的底部：頭頂（含頭髮）≈ 頭骨再往上「腰到頭骨距離」的 0.55 倍
                float torso = hips != null ? Mathf.Max(0.2f, (head.position - hips.position).magnitude) : 0.45f;
                var top = head.position + Vector3.up * torso * 0.55f;
                pts.Add(top);
                headTop = Mathf.Max(headTop, top.y);
            }
            // 武器：用模型外框的 8 個角
            foreach (var w in new[] { _weaponVisual.RightWeapon, _weaponVisual.LeftWeapon })
            {
                if (w == null) continue;
                foreach (var r in w.GetComponentsInChildren<Renderer>())
                {
                    var bb = r.bounds;
                    for (int i = 0; i < 8; i++)
                        pts.Add(new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, (i & 2) == 0 ? bb.min.y : bb.max.y, (i & 4) == 0 ? bb.min.z : bb.max.z));
                }
            }
        }

        // ─────────────────────────── 建立 ───────────────────────────

        private void EnsureStage()
        {
            if (_stage != null) return;
            if (_muirPrefab == null) { Debug.LogWarning("[MovePreviewStage] 沒有指定展示用 Muir", this); return; }

            _layer = LayerMask.NameToLayer(_layerName);
            if (_layer < 0) { Debug.LogWarning($"[MovePreviewStage] 找不到圖層 {_layerName}", this); _layer = 0; }

            // 其他攝影機都不要拍這個圖層
            foreach (var cam in Camera.allCameras) cam.cullingMask &= ~(1 << _layer);

            _stage = new GameObject("MovePreviewStage (Runtime)").transform;
            _stage.position = _stagePosition;
            _groundY = _stagePosition.y;

            _muir = Instantiate(_muirPrefab, _stage);
            _animator = _muir.GetComponent<Animator>();
            _animator.applyRootMotion = false;
            _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _weaponVisual = _muir.GetComponent<PlayerWeaponVisual>();
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            ResetPose();

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.cullingMask = 1 << _layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = _backgroundColor;
            _camera.fieldOfView = _fieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 50f;
            _rt = new RenderTexture(_textureSize.x, _textureSize.y, 24, RenderTextureFormat.ARGB32) { name = "MovePreviewRT", antiAliasing = 4 };
            _camera.targetTexture = _rt;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            // 燈光：主光從攝影機那側打過來，背光從後上方勾邊（都是範圍光，只照得到展示台附近）
            MakeLight("KeyLight", new Vector3(2.5f, 4f, 4f), _keyColor, _keyIntensity, 20f);
            MakeLight("RimLight", new Vector3(-2.5f, 4f, -3.5f), _rimColor, _rimIntensity, 20f);

            if (_floorGlow != null) _floor = MakeFlatSprite("FloorGlow", _floorGlow, _floorGlowColor, 0);
            if (_groundShadow != null) _shadow = MakeFlatSprite("GroundShadow", _groundShadow, Color.white, 1);

            SetLayerRecursive(_stage, _layer);

            // 動畫圖：Animator 不放 Controller，由這裡直接播
            _graph = PlayableGraph.Create("MovePreview");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            var output = AnimationPlayableOutput.Create(_graph, "Preview", _animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        private Transform MakeFlatSprite(string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_stage, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go.transform;
        }

        private void MakeLight(string name, Vector3 offset, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_stage, false);
            go.transform.localPosition = offset;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_stage != null) Destroy(_stage.gameObject);
        }
    }
}
