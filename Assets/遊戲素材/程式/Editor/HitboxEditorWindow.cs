#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 攻擊判定編輯器：選武器／招式／第幾下，預覽角色會停在那一刻的動作（含手上的武器模型），
    /// 直接在 Scene 視窗拖拉判定的位置、旋轉、大小。
    ///
    /// 判定形狀的優先順序（遊戲中也是這樣決定）：這一下自訂 → 這一招自訂 → 武器預設 → 舊的球形判定。
    /// 預覽角色是暫時生成的（不會存進場景），關掉視窗就消失；改動直接寫進 WeaponActions_*.asset。
    /// </summary>
    public class HitboxEditorWindow : EditorWindow
    {
        private const string PlayerPrefabPath = "Assets/遊戲素材/預製物/Player.prefab";
        private const string SetFolder = "Assets/遊戲素材/SO/主角";
        private const string HumanoidFolder = "Assets/遊戲素材/角色/謬爾/animation/Update/Humanoid";
        // 放在場景上空的空地：原點常常有場景物件（例如貪的關卡原點有方塊），會跟預覽角色重疊
        private static readonly Vector3 PreviewOrigin = new Vector3(0f, 200f, 0f);

        private static readonly string[] WeaponNames = { "鉛筆", "水彩筆", "畫刀", "調色盤" };
        private static readonly string[] WeaponFiles = { "Pencil", "Brush", "PaintKnife", "Palette" };

        private enum Slot { WeaponDefault, N1, N2, N3, N4, C1, C2, C3, C4, Pause, Hold, Special }
        private static readonly string[] SlotNames = { "武器預設", "N1", "N2", "N3", "N4", "C1", "C2", "C3", "C4", "停頓連打", "蓄力", "秘劍" };

        private enum EditTool { Move, Rotate, Size }
        private static readonly string[] ToolNames = { "移動", "旋轉", "大小" };

        private int _weapon;
        private Slot _slot = Slot.WeaponDefault;
        private int _window = -1;       // -1 = 整招
        private float _time = 0.5f;     // 預覽時間（動畫長度比例）
        private EditTool _tool;
        private Vector2 _scroll;

        private readonly WeaponActionSet[] _sets = new WeaponActionSet[4];
        private GameObject _preview;
        private Transform _boneR, _boneL;
        private Transform _weaponR, _weaponL;
        private Transform _wristR, _wristL;
        private readonly List<GameObject> _weaponModels = new List<GameObject>();
        private int _modelWeapon = -1;
        private readonly List<Pose> _frames = new List<Pose>(2);

        private readonly SphereBoundsHandle _sphereHandle = new SphereBoundsHandle();
        private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle();
        private readonly CapsuleBoundsHandle _capsuleHandle = new CapsuleBoundsHandle { heightAxis = CapsuleBoundsHandle.HeightAxis.Z };

        [MenuItem("PilgrimOfSin/主角/攻擊判定編輯器")]
        public static void Open() => GetWindow<HitboxEditorWindow>("攻擊判定編輯器");

        // ════════════════════════════════════════════════════════════
        //  生命週期
        // ════════════════════════════════════════════════════════════
        private void OnEnable()
        {
            for (int i = 0; i < 4; i++)
                _sets[i] = AssetDatabase.LoadAssetAtPath<WeaponActionSet>($"{SetFolder}/WeaponActions_{WeaponFiles[i]}.asset");
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            DestroyPreview();
            foreach (var s in _sets) if (s != null) AssetDatabase.SaveAssetIfDirty(s);
        }

        private void OnUndoRedo() { RefreshPose(); Repaint(); SceneView.RepaintAll(); }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) DestroyPreview();
            Repaint();
        }

        private WeaponActionSet Set => _sets[_weapon];
        private ComboMoveSet Combo => Set != null ? Set.combo : null;

        // ════════════════════════════════════════════════════════════
        //  目前選到的東西
        // ════════════════════════════════════════════════════════════

        /// <summary>這一招（或武器預設／秘劍）的資料。</summary>
        private class Target
        {
            public string label;
            public AnimationClip clip;
            public List<HitWindow> windows;
            public Func<bool> getCustom;      // 這一招有沒有自訂形狀（武器預設 = useWeaponHitbox）
            public Action<bool> setCustom;
            public HitboxShape shape;         // 這一招自己的形狀
            public bool isWeaponDefault;
        }

        private Target GetTarget()
        {
            var c = Combo;
            if (c == null) return null;
            switch (_slot)
            {
                case Slot.WeaponDefault:
                    return new Target
                    {
                        label = "武器預設", clip = c.n1 != null ? c.n1.clip : null, windows = null,
                        getCustom = () => c.useWeaponHitbox, setCustom = v => c.useWeaponHitbox = v,
                        shape = c.weaponHitbox, isWeaponDefault = true,
                    };
                case Slot.Special:
                    return new Target
                    {
                        label = "秘劍", clip = SpecialClip(), windows = c.specialHitWindows,
                        getCustom = () => c.specialCustomHitbox, setCustom = v => c.specialCustomHitbox = v,
                        shape = c.specialHitbox,
                    };
                default:
                    var m = MoveOf(c, _slot);
                    if (m == null) return null;
                    return new Target
                    {
                        label = SlotNames[(int)_slot], clip = m.clip, windows = m.hitWindows,
                        getCustom = () => m.customHitbox, setCustom = v => m.customHitbox = v,
                        shape = m.hitbox,
                    };
            }
        }

        private static ComboMoveEntry MoveOf(ComboMoveSet c, Slot s) => s switch
        {
            Slot.N1 => c.n1, Slot.N2 => c.n2, Slot.N3 => c.n3, Slot.N4 => c.n4,
            Slot.C1 => c.c1, Slot.C2 => c.c2, Slot.C3 => c.c3, Slot.C4 => c.c4,
            Slot.Pause => c.pauseCombo, Slot.Hold => c.chargeHold, _ => null,
        };

        private AnimationClip SpecialClip()
        {
            if (Set == null) return null;
            foreach (var e in Set.actions)
            {
                if (e.action != PlayerActionId.SpecialSkill || e.clip == null) continue;
                if (e.clip.humanMotion) return e.clip;
                // Generic 動畫放在 Humanoid 預覽角色上姿勢不對：改用轉好的同名 Humanoid 版（時間軸相同）
                var h = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{HumanoidFolder}/{e.clip.name}_Humanoid.anim");
                return h != null ? h : e.clip;
            }
            return null;
        }

        private HitWindow SelectedWindow(Target t)
            => t != null && t.windows != null && _window >= 0 && _window < t.windows.Count ? t.windows[_window] : null;

        /// <summary>正在編輯的那一層：可以拖拉的形狀（沒自訂時 = null）與實際生效的形狀。</summary>
        private void GetShapes(Target t, out HitboxShape editable, out HitboxShape effective, out string source)
        {
            editable = null; effective = null; source = "";
            if (t == null) return;
            var c = Combo;
            if (t.isWeaponDefault)
            {
                editable = t.getCustom() ? t.shape : null;
                effective = editable;
                source = editable != null ? "武器預設" : "（沒啟用武器預設：用舊的球形判定）";
                return;
            }
            var w = SelectedWindow(t);
            if (w != null)
            {
                editable = w.customHitbox ? w.hitbox : null;
                effective = c.ResolveHitbox(w, t.getCustom(), t.shape);
                source = w.customHitbox ? $"第 {_window + 1} 下自訂" : t.getCustom() ? $"沿用：{t.label} 整招的形狀" : c.useWeaponHitbox ? "沿用：武器預設" : "沿用：舊的球形判定";
            }
            else
            {
                editable = t.getCustom() ? t.shape : null;
                effective = c.ResolveHitbox(null, t.getCustom(), t.shape);
                source = t.getCustom() ? $"{t.label} 整招自訂" : c.useWeaponHitbox ? "沿用：武器預設" : "沿用：舊的球形判定";
            }
        }

        // ════════════════════════════════════════════════════════════
        //  視窗 UI
        // ════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play 中無法編輯。請停止 Play 後再調整；Play 中可以用 F1 看實際的判定。", MessageType.Info);
                return;
            }
            if (Set == null)
            {
                EditorGUILayout.HelpBox($"找不到 {SetFolder}/WeaponActions_{WeaponFiles[_weapon]}.asset", MessageType.Error);
                return;
            }
            EnsurePreview();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUI.BeginChangeCheck();
            _weapon = GUILayout.Toolbar(_weapon, WeaponNames);
            if (EditorGUI.EndChangeCheck()) { _window = -1; RefreshPose(); }

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _slot = (Slot)GUILayout.SelectionGrid((int)_slot, SlotLabels(), 4);
            if (EditorGUI.EndChangeCheck()) { _window = -1; _time = 0.5f; RefreshPose(); }

            var t = GetTarget();
            if (t == null) { EditorGUILayout.EndScrollView(); return; }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(t.clip != null ? $"預覽動畫：{t.clip.name}（{t.clip.length:F2} 秒）" : "這一招沒有放動畫", EditorStyles.miniLabel);

            // 第幾下
            if (t.windows != null && t.windows.Count > 0 && t.clip != null)
            {
                var labels = new string[t.windows.Count + 1];
                labels[0] = "整招";
                for (int i = 0; i < t.windows.Count; i++)
                {
                    var w = t.windows[i];
                    labels[i + 1] = $"第{i + 1}下 {w.start * t.clip.length:F2}~{w.end * t.clip.length:F2}s{(w.customHitbox ? " ★" : "")}";
                }
                EditorGUI.BeginChangeCheck();
                int sel = GUILayout.SelectionGrid(_window + 1, labels, 2) - 1;
                if (EditorGUI.EndChangeCheck())
                {
                    _window = sel;
                    var w = SelectedWindow(t);
                    if (w != null) _time = (w.start + w.end) * 0.5f;
                    RefreshPose();
                }
            }
            else if (!t.isWeaponDefault)
            {
                EditorGUILayout.HelpBox("這一招沒有命中區間（整招一下）。", MessageType.None);
            }

            // 預覽時間
            if (t.clip != null)
            {
                EditorGUI.BeginChangeCheck();
                _time = EditorGUILayout.Slider($"預覽時間 {_time * t.clip.length:F2}s", _time, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) RefreshPose();
            }

            EditorGUILayout.Space(8);
            DrawLevelEditor(t);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Scene 視窗工具", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _tool = (EditTool)GUILayout.Toolbar((int)_tool, ToolNames);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            if (GUILayout.Button("Scene 視窗對準預覽角色")) FrameSceneView();

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "紅色 = 可以拖拉的判定；灰色 = 沿用上一層的形狀（按「改成自訂」才能改）。\n" +
                "優先順序：這一下自訂 → 這一招自訂 → 武器預設 → 舊的球形判定。\n" +
                "「手」掛點以手上的武器模型為準（沿著武器的方向）。雙手 = 左右各一個判定，用同一組數值。\n" +
                "往前飛：命中區間內從起點往主角正前方飛完設定的距離（黃色線是飛行路線）。\n" +
                "改動直接存進 WeaponActions，可以 Ctrl+Z 復原。Play 中按 F1 看實際判定。",
                MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private string[] SlotLabels()
        {
            var c = Combo;
            var labels = new string[SlotNames.Length];
            for (int i = 0; i < SlotNames.Length; i++)
            {
                string mark = "";
                if (c != null)
                {
                    var s = (Slot)i;
                    if (s == Slot.WeaponDefault) mark = c.useWeaponHitbox ? " ★" : "";
                    else if (s == Slot.Special) mark = c.specialCustomHitbox || AnyCustom(c.specialHitWindows) ? " ★" : "";
                    else
                    {
                        var m = MoveOf(c, s);
                        if (m == null || !m.IsValid) mark = "（空）";
                        else if (m.customHitbox || AnyCustom(m.hitWindows)) mark = " ★";
                    }
                }
                labels[i] = SlotNames[i] + mark;
            }
            return labels;
        }

        private static bool AnyCustom(List<HitWindow> ws)
        {
            if (ws == null) return false;
            foreach (var w in ws) if (w.customHitbox) return true;
            return false;
        }

        private void DrawLevelEditor(Target t)
        {
            var w = SelectedWindow(t);
            string level = t.isWeaponDefault ? $"{WeaponNames[_weapon]} 的預設判定" : w != null ? $"{t.label} 第 {_window + 1} 下" : $"{t.label} 整招";
            EditorGUILayout.LabelField($"正在編輯：{level}", EditorStyles.boldLabel);

            GetShapes(t, out var editable, out var effective, out string source);
            EditorGUILayout.LabelField(source, EditorStyles.miniLabel);

            if (editable == null)
            {
                string btn = t.isWeaponDefault ? "啟用武器預設判定" : w != null ? "這一下改成自訂形狀" : "這一招改成自訂形狀";
                if (GUILayout.Button(btn + "（從目前生效的形狀複製）"))
                {
                    Undo.RecordObject(Set, btn);
                    var copy = effective != null ? effective.Clone() : new HitboxShape();
                    if (t.isWeaponDefault) { t.setCustom(true); }
                    else if (w != null) { w.customHitbox = true; w.hitbox = copy; }
                    else { t.setCustom(true); CopyInto(copy, t.shape); }
                    EditorUtility.SetDirty(Set);
                    SceneView.RepaintAll();
                }
                return;
            }

            string off = t.isWeaponDefault ? "停用武器預設（改回舊的球形判定）" : "取消自訂（改回沿用上一層）";
            if (GUILayout.Button(off))
            {
                Undo.RecordObject(Set, off);
                if (w != null) w.customHitbox = false; else t.setCustom(false);
                EditorUtility.SetDirty(Set);
                SceneView.RepaintAll();
                return;
            }

            EditorGUI.BeginChangeCheck();
            var shape = (HitboxShapeType)EditorGUILayout.EnumPopup("形狀", editable.shape);
            var anchor = (HitboxAnchor)EditorGUILayout.EnumPopup("掛點", editable.anchor);
            var offset = EditorGUILayout.Vector3Field("位置", editable.offset);
            var rotation = EditorGUILayout.Vector3Field("旋轉", editable.rotation);
            Vector3 size = editable.size;
            switch (editable.shape)
            {
                case HitboxShapeType.Sphere:
                    float d = EditorGUILayout.FloatField("直徑（公尺）", editable.size.x);
                    size = new Vector3(d, d, d);
                    break;
                case HitboxShapeType.Box:
                    size = EditorGUILayout.Vector3Field("長寬高（公尺）", editable.size);
                    break;
                default:
                    float thick = EditorGUILayout.FloatField("粗細／直徑（公尺）", editable.size.x);
                    float len = EditorGUILayout.FloatField("長度（公尺）", editable.size.z);
                    size = new Vector3(thick, thick, len);
                    break;
            }
            var motion = (HitboxMotion)EditorGUILayout.EnumPopup("動作", editable.motion);
            float dist = editable.projectileDistance;
            if (motion == HitboxMotion.Projectile) dist = EditorGUILayout.FloatField("飛行距離（公尺）", editable.projectileDistance);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Set, "調整攻擊判定");
                editable.shape = shape; editable.anchor = anchor; editable.offset = offset; editable.rotation = rotation;
                editable.size = new Vector3(Mathf.Max(0.02f, size.x), Mathf.Max(0.02f, size.y), Mathf.Max(0.02f, size.z));
                editable.motion = motion; editable.projectileDistance = Mathf.Max(0f, dist);
                EditorUtility.SetDirty(Set);
                SceneView.RepaintAll();
            }
        }

        private static void CopyInto(HitboxShape from, HitboxShape to)
        {
            to.shape = from.shape; to.anchor = from.anchor; to.offset = from.offset; to.rotation = from.rotation;
            to.size = from.size; to.motion = from.motion; to.projectileDistance = from.projectileDistance;
        }

        // ════════════════════════════════════════════════════════════
        //  預覽角色
        // ════════════════════════════════════════════════════════════
        private void EnsurePreview()
        {
            if (_preview == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                if (prefab == null) return;
                _preview = Instantiate(prefab, PreviewOrigin, Quaternion.identity);
                _preview.name = "[攻擊判定編輯器預覽]（不會存檔）";
                // Player.prefab 本身的縮放是 (1, 1.7, 1)，各關卡場景裡都覆寫回 (1, 1, 1)：預覽跟遊戲一樣用 1，
                // 不然角色會被拉長 1.7 倍，判定位置也會跟遊戲對不上
                _preview.transform.localScale = Vector3.one;
                // Prefab 根物件上有一個佔位用的方塊 MeshRenderer，預覽時藏起來（不然角色像站在箱子上）
                var placeholder = _preview.GetComponent<MeshRenderer>();
                if (placeholder != null) placeholder.enabled = false;
                SetHideFlags(_preview, HideFlags.DontSave);
                foreach (var mb in _preview.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                var hb = _preview.GetComponentInChildren<PlayerAttackHitbox>(true);
                if (hb != null) hb.gameObject.SetActive(false);
                _boneR = HitboxBones.Find(_preview.transform, HitboxBones.RightHandAttach);
                _boneL = HitboxBones.Find(_preview.transform, HitboxBones.LeftHandAttach);
                // 手腕錨點要在還沒播動畫（綁定姿勢）時建立，跟遊戲中 PlayerWeaponVisual.Awake 一樣
                var previewAnim = _preview.GetComponent<Animator>();
                _wristR = PlayerWeaponVisual.CreateWristAnchor(previewAnim, _boneR, true);
                _wristL = PlayerWeaponVisual.CreateWristAnchor(previewAnim, _boneL, false);
                _modelWeapon = -1;
                RefreshPose();
                FrameSceneView();
            }
            if (_modelWeapon != _weapon) SpawnWeaponModels();
        }

        private static void SetHideFlags(GameObject go, HideFlags flags)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = flags;
        }

        private void DestroyPreview()
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = null; _weaponModels.Clear(); _weaponR = _weaponL = null; _wristR = _wristL = null; _modelWeapon = -1;
        }

        /// <summary>照 PlayerWeaponVisual 的設定把武器模型掛到預覽角色手上（跟遊戲中一樣的位置與方向）。</summary>
        private void SpawnWeaponModels()
        {
            foreach (var m in _weaponModels) if (m != null) DestroyImmediate(m);
            _weaponModels.Clear();
            _weaponR = _weaponL = null;
            _modelWeapon = _weapon;
            if (_preview == null) return;
            var visual = _preview.GetComponent<PlayerWeaponVisual>();
            if (visual == null) return;
            var so = new SerializedObject(visual);
            GameObject P(string n) => so.FindProperty(n)?.objectReferenceValue as GameObject;
            Vector3 R(string n) => so.FindProperty(n)?.vector3Value ?? Vector3.zero;
            bool F(string n) => so.FindProperty(n)?.boolValue ?? false;
            Transform MountR(string follow) => F(follow) && _wristR != null ? _wristR : _boneR;
            Transform MountL(string follow) => F(follow) && _wristL != null ? _wristL : _boneL;
            switch (_weapon)
            {
                case 0: _weaponR = Spawn(MountR("_pencilFollowWrist"), P("_pencilPrefab"), R("_pencilRotationOffset"), R("_pencilGripPoint")); break;
                case 1: _weaponR = Spawn(MountR("_brushFollowWrist"), P("_brushPrefab"), R("_brushRotationOffset"), R("_brushGripPoint")); break;
                case 2:
                    _weaponR = Spawn(MountR("_paintKnifeFollowWrist"), P("_paintKnifePrefab"), R("_paintKnifeRotationOffsetRight"), R("_paintKnifeGripPoint"));
                    _weaponL = Spawn(MountL("_paintKnifeFollowWrist"), P("_paintKnifePrefab"), R("_paintKnifeRotationOffsetLeft"), R("_paintKnifeGripPoint"));
                    break;
                case 3: _weaponL = Spawn(MountL("_paletteFollowWrist"), P("_palettePrefab"), R("_paletteRotationOffset"), R("_paletteGripPoint")); break;
            }
        }

        private Transform Spawn(Transform bone, GameObject prefab, Vector3 rotOffset, Vector3 grip)
        {
            // 跟遊戲中同一個掛法（PlayerWeaponVisual.AttachModel）
            var inst = PlayerWeaponVisual.AttachModel(bone, prefab, rotOffset, grip);
            if (inst == null) return null;
            SetHideFlags(inst, HideFlags.DontSave);
            _weaponModels.Add(inst);
            return inst.transform;
        }

        private Transform HandR => _weaponR != null ? _weaponR : _boneR;
        private Transform HandL => _weaponL != null ? _weaponL : _boneL;

        private void RefreshPose()
        {
            if (_preview == null) return;
            var t = GetTarget();
            var clip = t?.clip;
            if (clip != null) clip.SampleAnimation(_preview, Mathf.Clamp01(_time) * clip.length);
            // 根位移歸零：遊戲中「主角」掛點也是跟著根物件走，這樣預覽跟遊戲一致
            _preview.transform.SetPositionAndRotation(PreviewOrigin, Quaternion.identity);
            SceneView.RepaintAll();
        }

        private void FrameSceneView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || _preview == null) return;
            sv.Frame(new Bounds(PreviewOrigin + Vector3.up * 1f, new Vector3(4f, 3f, 4f)), false);
        }

        // ════════════════════════════════════════════════════════════
        //  Scene 視窗：畫判定、拖拉
        // ════════════════════════════════════════════════════════════
        private void OnSceneGUI(SceneView sv)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || _preview == null) return;
            var t = GetTarget();
            GetShapes(t, out var editable, out var effective, out _);
            if (effective == null)
            {
                DrawLegacySphere();
                return;
            }

            int n = HitboxShape.GetAnchorFrames(effective.anchor, _preview.transform, HandR, HandL, _frames);
            if (n == 0) return;
            var color = editable != null ? new Color(1f, 0.25f, 0.25f, 1f) : new Color(0.7f, 0.7f, 0.7f, 0.8f);

            for (int i = 0; i < n; i++)
            {
                var pose = effective.ToWorld(_frames[i]);
                DrawShape(effective, pose, color);
                if (effective.motion == HitboxMotion.Projectile)
                {
                    var end = new Pose(pose.position + ProjectileDir() * effective.projectileDistance, pose.rotation);
                    Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                    Handles.DrawDottedLine(pose.position, end.position, 4f);
                    DrawShape(effective, end, new Color(1f, 0.85f, 0.2f, 0.5f));
                }
            }

            if (editable == null) return;
            var frame = _frames[0];
            var p = editable.ToWorld(frame);
            EditorGUI.BeginChangeCheck();
            switch (_tool)
            {
                case EditTool.Move:
                {
                    var np = Handles.PositionHandle(p.position, p.rotation);
                    if (EditorGUI.EndChangeCheck()) Apply("移動攻擊判定", () => editable.FromWorld(frame, np, p.rotation));
                    break;
                }
                case EditTool.Rotate:
                {
                    var nr = Handles.RotationHandle(p.rotation, p.position);
                    if (EditorGUI.EndChangeCheck()) Apply("旋轉攻擊判定", () => editable.FromWorld(frame, p.position, nr));
                    break;
                }
                default:
                    EditorGUI.EndChangeCheck();
                    DrawSizeHandle(editable, frame, p);
                    break;
            }

            // 往前飛：拖黃點調整飛行距離
            if (editable.motion == HitboxMotion.Projectile)
            {
                var dir = ProjectileDir();
                var endPos = p.position + dir * editable.projectileDistance;
                Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
                EditorGUI.BeginChangeCheck();
                var moved = Handles.Slider(endPos, dir, HandleUtility.GetHandleSize(endPos) * 0.12f, Handles.SphereHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                    Apply("調整飛行距離", () => editable.projectileDistance = Mathf.Max(0f, Vector3.Dot(moved - p.position, dir)));
            }
        }

        private Vector3 ProjectileDir()
        {
            var f = _preview.transform.forward; f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        private void DrawSizeHandle(HitboxShape s, Pose frame, Pose p)
        {
            using (new Handles.DrawingScope(new Color(1f, 0.4f, 0.4f, 1f), Matrix4x4.TRS(p.position, p.rotation, Vector3.one)))
            {
                EditorGUI.BeginChangeCheck();
                switch (s.shape)
                {
                    case HitboxShapeType.Sphere:
                        _sphereHandle.center = Vector3.zero;
                        _sphereHandle.radius = s.Radius;
                        _sphereHandle.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            float d = _sphereHandle.radius * 2f;
                            Apply("調整判定大小", () => s.size = new Vector3(d, d, d));
                        }
                        break;
                    case HitboxShapeType.Box:
                        _boxHandle.center = Vector3.zero;
                        _boxHandle.size = s.size;
                        _boxHandle.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            // 拖單一面時中心會跟著偏移：把偏移加回位置，另一面保持不動
                            var c = _boxHandle.center; var size = _boxHandle.size;
                            Apply("調整判定大小", () =>
                            {
                                s.size = size;
                                s.FromWorld(frame, p.position + p.rotation * c, p.rotation);
                            });
                        }
                        break;
                    default:
                        _capsuleHandle.center = Vector3.zero;
                        _capsuleHandle.radius = s.Radius;
                        _capsuleHandle.height = s.CapsuleLength;
                        _capsuleHandle.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            var c = _capsuleHandle.center; float r = _capsuleHandle.radius, h = _capsuleHandle.height;
                            Apply("調整判定大小", () =>
                            {
                                s.size = new Vector3(r * 2f, r * 2f, h);
                                s.FromWorld(frame, p.position + p.rotation * c, p.rotation);
                            });
                        }
                        break;
                }
            }
        }

        private void Apply(string undoName, Action change)
        {
            Undo.RecordObject(Set, undoName);
            change();
            EditorUtility.SetDirty(Set);
            Repaint();
        }

        private static void DrawShape(HitboxShape s, Pose p, Color color)
        {
            using (new Handles.DrawingScope(color, Matrix4x4.TRS(p.position, p.rotation, Vector3.one)))
            {
                switch (s.shape)
                {
                    case HitboxShapeType.Sphere:
                        DrawWireSphere(Vector3.zero, s.Radius);
                        break;
                    case HitboxShapeType.Box:
                        Handles.DrawWireCube(Vector3.zero, s.size);
                        break;
                    default:
                        float r = s.Radius, half = Mathf.Max(0f, s.CapsuleLength * 0.5f - r);
                        var a = Vector3.forward * half; var b = -Vector3.forward * half;
                        Handles.DrawWireDisc(a, Vector3.forward, r, 2f);
                        Handles.DrawWireDisc(b, Vector3.forward, r, 2f);
                        foreach (var side in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down })
                            Handles.DrawLine(a + side * r, b + side * r, 2f);
                        Handles.DrawWireArc(a, Vector3.up, Vector3.left, -180f, r, 2f);
                        Handles.DrawWireArc(a, Vector3.right, Vector3.up, 180f, r, 2f);
                        Handles.DrawWireArc(b, Vector3.up, Vector3.left, 180f, r, 2f);
                        Handles.DrawWireArc(b, Vector3.right, Vector3.up, -180f, r, 2f);
                        break;
                }
            }
        }

        private static void DrawWireSphere(Vector3 c, float r)
        {
            Handles.DrawWireDisc(c, Vector3.up, r, 2f);
            Handles.DrawWireDisc(c, Vector3.right, r, 2f);
            Handles.DrawWireDisc(c, Vector3.forward, r, 2f);
        }

        private void DrawLegacySphere()
        {
            var hb = _preview.GetComponentInChildren<PlayerAttackHitbox>(true);
            var sc = hb != null ? hb.GetComponent<SphereCollider>() : null;
            if (sc == null) return;
            var ls = sc.transform.lossyScale;
            float r = sc.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            using (new Handles.DrawingScope(new Color(0.7f, 0.7f, 0.7f, 0.8f)))
                DrawWireSphere(sc.transform.TransformPoint(sc.center), r);
        }

        // ════════════════════════════════════════════════════════════
        //  各武器預設形狀（依武器模型長度）
        // ════════════════════════════════════════════════════════════
        [MenuItem("PilgrimOfSin/主角/攻擊判定/套用各武器預設形狀（依武器長度，會蓋掉武器預設）")]
        private static void ApplyWeaponDefaultsMenu()
        {
            if (!EditorUtility.DisplayDialog("套用各武器預設形狀",
                    "依武器模型的長度，重設四把武器的「武器預設判定」並啟用。招式／每一下的自訂形狀不受影響。確定嗎？", "套用", "取消"))
                return;
            Debug.Log(ApplyWeaponDefaults());
        }

        /// <summary>鉛筆／水彩筆／畫刀 = 沿著武器的膠囊；調色盤 = 包住調色盤的球。回傳報告文字。</summary>
        public static string ApplyWeaponDefaults()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var visual = prefab != null ? prefab.GetComponent<PlayerWeaponVisual>() : null;
            if (visual == null) return "[HitboxEditor] 找不到 Player.prefab 的 PlayerWeaponVisual";
            var so = new SerializedObject(visual);
            var report = new System.Text.StringBuilder("[HitboxEditor] 套用各武器預設形狀：\n");
            string[] prefabFields = { "_pencilPrefab", "_brushPrefab", "_paintKnifePrefab", "_palettePrefab" };
            for (int i = 0; i < 4; i++)
            {
                var set = AssetDatabase.LoadAssetAtPath<WeaponActionSet>($"{SetFolder}/WeaponActions_{WeaponFiles[i]}.asset");
                var model = so.FindProperty(prefabFields[i]).objectReferenceValue as GameObject;
                if (set == null || model == null) { report.AppendLine($"✗ {WeaponNames[i]}：缺資料"); continue; }

                // 武器模型自己座標系裡的範圍（模型根物件 = 握把位置，掛在手上時就是「手」掛點）
                var b = LocalBounds(model);
                var shape = new HitboxShape();
                if (i == 3)
                {
                    // 調色盤：包住整個調色盤的球（浮游砲／投擲那幾下請在編輯器裡改成柱狀或往前飛）
                    float d = Mathf.Max(b.size.x, b.size.y, b.size.z) + 0.4f;
                    shape.shape = HitboxShapeType.Sphere;
                    shape.anchor = HitboxAnchor.LeftHand;
                    shape.offset = b.center;
                    shape.rotation = Vector3.zero;
                    shape.size = new Vector3(d, d, d);
                }
                else
                {
                    // 沿著武器最長的那一軸放膠囊，比模型粗一點、長一點，揮擊時比較打得到
                    int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
                    var dir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    float length = b.size[axis] + 0.3f;
                    shape.shape = HitboxShapeType.Capsule;
                    shape.anchor = i == 2 ? HitboxAnchor.BothHands : HitboxAnchor.RightHand;
                    shape.offset = b.center;
                    shape.rotation = Quaternion.FromToRotation(Vector3.forward, dir).eulerAngles;
                    shape.size = new Vector3(0.5f, 0.5f, length);
                }
                Undo.RecordObject(set, "套用武器預設判定");
                set.combo.weaponHitbox = shape;
                set.combo.useWeaponHitbox = true;
                EditorUtility.SetDirty(set);
                report.AppendLine($"✓ {WeaponNames[i]}：{shape.shape} 掛點 {shape.anchor} 尺寸 {shape.size} 位置 {shape.offset}");
            }
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        private static Bounds LocalBounds(GameObject model)
        {
            // Prefab 資源本身在原點、沒有旋轉，Renderer.bounds 就等於模型自己座標系的範圍
            var bounds = new Bounds(); bool init = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!init) { bounds = r.bounds; init = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }
    }
}
#endif
