#if UNITY_EDITOR || DEVELOPMENT_BUILD

using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 開發者面板（F1）：在遊戲畫面直接畫出主角的攻擊判定。
    ///   · 判定關著：灰色顯示這把武器的預設形狀（讓你看到範圍）
    ///   · 判定開著：紅色顯示實際正在用的形狀（每一下可能不同，往前飛的會跟著飛）
    ///   · 白色閃一下：剛打中東西
    /// 形狀與位置請用選單 PilgrimOfSin → 主角 → 攻擊判定編輯器 調整。
    /// 由 FSMDebugOverlay 建立，跟 F1 面板一起開關。
    /// </summary>
    public class AttackHitboxVisualizer : MonoBehaviour
    {
        public bool Visible { get; set; } = true;
        public PlayerController Player { get; set; }

        private const float FlashSeconds = 0.15f;
        private const int MaxSlots = 2;

        private static readonly Color IdleColor = new Color(0.8f, 0.8f, 0.8f, 0.12f);
        private static readonly Color ActiveColor = new Color(1f, 0.15f, 0.15f, 0.35f);
        private static readonly Color HitColor = new Color(1f, 1f, 1f, 0.55f);

        private PlayerController _boundPlayer;
        private PlayerAttackHitbox _hitbox;
        private SphereCollider _legacySphere;
        private int _lastHits;
        private float _flashUntil;

        private Material _material;
        private readonly VisualSlot[] _visuals = new VisualSlot[MaxSlots];
        private readonly List<Pose> _frames = new List<Pose>(2);
        private string _status = "";
        private GUIStyle _style;

        private class VisualSlot
        {
            public GameObject sphere, box, capsule;
        }

        private void LateUpdate()
        {
            // Play 中重新編譯後，非序列化的視覺物件陣列會被清空、但綁定還在：發現物件不見就重新綁定
            if (Player != _boundPlayer || (Player != null && !VisualsValid())) Bind(Player);
            if (_hitbox == null || !Visible || !VisualsValid()) { HideAll(); return; }

            if (_hitbox.TotalHits != _lastHits)
            {
                _lastHits = _hitbox.TotalHits;
                _flashUntil = Time.unscaledTime + FlashSeconds;
            }

            bool active = _hitbox.gameObject.activeInHierarchy;
            _material.color = Time.unscaledTime < _flashUntil ? HitColor : active ? ActiveColor : IdleColor;

            if (active && _hitbox.ActiveShape != null)
            {
                // 形狀模式：照判定槽實際的位置畫
                var shape = _hitbox.ActiveShape;
                for (int i = 0; i < MaxSlots; i++)
                {
                    if (i < _hitbox.ActiveSlotCount) Show(i, shape, _hitbox.GetSlotPose(i));
                    else Hide(i);
                }
                _status = Describe(shape, "判定開");
            }
            else if (active || CurrentWeaponShape() == null)
            {
                // 舊模式的球（Prefab 上的 SphereCollider）
                DrawLegacy();
                _status = _legacySphere != null ? $"舊的球形判定　半徑 {LegacyWorldRadius():F2} m" + (active ? "（判定開）" : "") : "";
            }
            else
            {
                // 判定關著：預覽這把武器的預設形狀
                var shape = CurrentWeaponShape();
                int n = Mathf.Min(MaxSlots, HitboxShape.GetAnchorFrames(shape.anchor, _hitbox.PlayerRoot, _hitbox.RightHand, _hitbox.LeftHand, _frames));
                for (int i = 0; i < MaxSlots; i++)
                {
                    if (i < n) Show(i, shape, shape.ToWorld(_frames[i]));
                    else Hide(i);
                }
                _status = Describe(shape, "武器預設，判定關");
            }
        }

        private HitboxShape CurrentWeaponShape()
        {
            if (Player == null || Player.Feedback == null) return null;
            int weapon = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            var set = Player.Feedback.GetComboSet(weapon);
            return set != null && set.useWeaponHitbox ? set.weaponHitbox : null;
        }

        private static string Describe(HitboxShape s, string state)
        {
            string shape = s.shape switch
            {
                HitboxShapeType.Sphere => $"球 直徑 {s.size.x:F2} m",
                HitboxShapeType.Box => $"方塊 {s.size.x:F2}×{s.size.y:F2}×{s.size.z:F2} m",
                _ => $"膠囊 粗 {s.size.x:F2} m、長 {s.CapsuleLength:F2} m",
            };
            string anchor = s.anchor switch
            {
                HitboxAnchor.Player => "跟主角",
                HitboxAnchor.RightHand => "跟右手",
                HitboxAnchor.LeftHand => "跟左手",
                _ => "雙手",
            };
            string motion = s.motion == HitboxMotion.Projectile ? $"　往前飛 {s.projectileDistance:F1} m" : "";
            return $"{shape}　{anchor}{motion}（{state}）";
        }

        private bool VisualsValid()
        {
            if (_hitbox == null || _material == null) return false;
            for (int i = 0; i < MaxSlots; i++)
            {
                var v = _visuals[i];
                if (v == null || v.sphere == null || v.box == null || v.capsule == null) return false;
            }
            return true;
        }

        // ── 繪製 ───────────────────────────────────────────────────────
        private void Show(int i, HitboxShape shape, Pose pose)
        {
            var v = _visuals[i];
            v.sphere.SetActive(shape.shape == HitboxShapeType.Sphere);
            v.box.SetActive(shape.shape == HitboxShapeType.Box);
            v.capsule.SetActive(shape.shape == HitboxShapeType.Capsule);
            switch (shape.shape)
            {
                case HitboxShapeType.Sphere:
                    v.sphere.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    v.sphere.transform.localScale = Vector3.one * shape.Radius * 2f;
                    break;
                case HitboxShapeType.Box:
                    v.box.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    v.box.transform.localScale = shape.size;
                    break;
                default:
                    // 內建膠囊沿 Y 軸、高 2、直徑 1 → 轉到沿 Z 軸並縮放成指定粗細與長度
                    v.capsule.transform.SetPositionAndRotation(pose.position, pose.rotation * Quaternion.Euler(90f, 0f, 0f));
                    v.capsule.transform.localScale = new Vector3(shape.Radius * 2f, shape.CapsuleLength * 0.5f, shape.Radius * 2f);
                    break;
            }
        }

        private void DrawLegacy()
        {
            Hide(1);
            if (_legacySphere == null) { Hide(0); return; }
            var v = _visuals[0];
            v.sphere.SetActive(true); v.box.SetActive(false); v.capsule.SetActive(false);
            var t = _legacySphere.transform;
            v.sphere.transform.SetPositionAndRotation(t.TransformPoint(_legacySphere.center), Quaternion.identity);
            v.sphere.transform.localScale = Vector3.one * LegacyWorldRadius() * 2f;
        }

        private float LegacyWorldRadius()
        {
            var s = _legacySphere.transform.lossyScale;
            return _legacySphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }

        private void Hide(int i)
        {
            var v = _visuals[i];
            if (v == null) return;
            if (v.sphere) v.sphere.SetActive(false);
            if (v.box) v.box.SetActive(false);
            if (v.capsule) v.capsule.SetActive(false);
        }

        private void HideAll() { for (int i = 0; i < MaxSlots; i++) Hide(i); _status = ""; }

        // ── 綁定 ───────────────────────────────────────────────────────
        private void Bind(PlayerController player)
        {
            Unbind();
            _boundPlayer = player;
            if (player == null || player.Combat == null) return;
            _hitbox = player.Combat.Hitbox;
            if (_hitbox == null) return;
            _legacySphere = _hitbox.GetComponent<SphereCollider>();
            _lastHits = _hitbox.TotalHits;

            // Sprites/Default：URP 下也能用、本身就是半透明、不做背面剔除
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) { _hitbox = null; return; }   // Build 裡被剝掉時就不畫
            _material = new Material(shader) { color = IdleColor };

            // 視覺物件不放在判定底下：判定關掉時子物件也會消失，就看不到預設範圍了
            for (int i = 0; i < MaxSlots; i++)
            {
                _visuals[i] = new VisualSlot
                {
                    sphere = MakePrimitive(PrimitiveType.Sphere, player.transform, i),
                    box = MakePrimitive(PrimitiveType.Cube, player.transform, i),
                    capsule = MakePrimitive(PrimitiveType.Capsule, player.transform, i),
                };
            }
        }

        private GameObject MakePrimitive(PrimitiveType type, Transform parent, int i)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = $"[Dev] Hitbox Visual {i} {type}";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = _material;
            go.SetActive(false);
            return go;
        }

        private void Unbind()
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                var v = _visuals[i];
                if (v == null) continue;
                if (v.sphere) Destroy(v.sphere);
                if (v.box) Destroy(v.box);
                if (v.capsule) Destroy(v.capsule);
                _visuals[i] = null;
            }
            if (_material != null) Destroy(_material);
            _material = null; _hitbox = null; _legacySphere = null; _boundPlayer = null;
        }

        private void OnDestroy() => Unbind();

        private void OnGUI()
        {
            if (!Visible || _hitbox == null || string.IsNullOrEmpty(_status)) return;
            float s = Mathf.Clamp(Screen.height / 450f, 1f, 4f);
            if (_style == null || _style.fontSize != Mathf.RoundToInt(10f * s))
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = Mathf.RoundToInt(10f * s),
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = Color.white },
                    padding = new RectOffset(6, 6, 4, 4),
                    wordWrap = false,
                };
            }
            string text = "攻擊判定　" + _status + "\n" +
                          "灰 = 判定關　紅 = 判定開　白閃 = 打中\n" +
                          "調整：PilgrimOfSin → 主角 → 攻擊判定編輯器";
            var size = _style.CalcSize(new GUIContent(text));
            // 左側中下方：避開左上的狀態面板（Boss 面板高度依關卡不同）與下方的武器列
            GUI.Box(new Rect(10f * s, Screen.height * 0.62f, size.x, size.y), text, _style);
        }
    }
}

#endif
