using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 玩家攻擊碰撞體。
    /// 掛在 Player 的子物件上，由 PlayerCombat 啟用/停用。
    /// 同一次攻擊每個目標只打一次。
    ///
    /// 兩種模式：
    ///   · 舊模式（Activate 沒給形狀）：用這個物件自己身上的 SphereCollider（Prefab 上設定的球）。
    ///   · 形狀模式（出招表研究）：依 HitboxShape 在子物件「判定槽」上開球／方塊／膠囊碰撞體，
    ///     掛在主角或武器手上跟著動，或往前飛（投擲／射擊）。雙手武器會開兩個槽，共用同一份命中記錄。
    /// 判定一律是真的觸發碰撞體（不是程式算範圍），天秤等用 OnTriggerEnter 偵測攻擊的物件照常運作。
    /// </summary>
    public class PlayerAttackHitbox : MonoBehaviour
    {
        private float _damage;
        private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();

        /// <summary>
        /// 這一次揮擊是否已經打到 Boss（IBossHealth）。給天秤判斷用：同一次揮擊如果已經打到 Boss，
        /// 就不該同時算成「攻擊天秤」，否則站在天秤旁邊打 Boss 會誤觸天秤重製。
        /// </summary>
        public bool HitBossThisSwing { get; private set; }

        /// <summary>每次揮擊（Activate）加 1。讓「一次揮擊碰到多個碰撞體」的對象（例如天秤）能辨認同一擊、只處理一次。</summary>
        public int SwingId { get; private set; }

        /// <summary>這一下打中時的打擊感檔位（鏡頭晃動／頓幀／手把震動／閃白）。</summary>
        public HitImpactLevel Impact { get; private set; } = HitImpactLevel.Light;

        /// <summary>累計打中幾次（開發者面板用來閃一下命中提示）。</summary>
        public int TotalHits { get; private set; }

        // ── 形狀模式 ───────────────────────────────────────────────────
        private const int MaxSlots = 2;
        private Collider _legacyCollider;
        private Transform _player, _boneR, _boneL;
        private PlayerWeaponVisual _weaponVisual;
        // 「手」掛點 = 手上的武器模型（判定沿著武器的方向擺）；沒有武器模型時用手骨
        private Transform _handR => _weaponVisual != null && _weaponVisual.RightWeapon != null ? _weaponVisual.RightWeapon : _boneR;
        private Transform _handL => _weaponVisual != null && _weaponVisual.LeftWeapon != null ? _weaponVisual.LeftWeapon : _boneL;
        private readonly Slot[] _slots = new Slot[MaxSlots];
        private readonly List<Pose> _frames = new List<Pose>(2);
        private readonly Pose[] _projectileStart = new Pose[MaxSlots];
        private Vector3 _projectileDir;
        private float _activeTime, _activeDuration;

        /// <summary>目前開著的判定形狀（null = 舊模式的球）。</summary>
        public HitboxShape ActiveShape { get; private set; }
        /// <summary>目前用到幾個判定槽（形狀模式）。</summary>
        public int ActiveSlotCount { get; private set; }
        public Transform PlayerRoot => _player;
        public Transform RightHand => _handR;
        public Transform LeftHand => _handL;
        public Pose GetSlotPose(int i) => new Pose(_slots[i].go.transform.position, _slots[i].go.transform.rotation);

        private class Slot
        {
            public GameObject go;
            public SphereCollider sphere;
            public BoxCollider box;
            public CapsuleCollider capsule;
        }

        private void Awake()
        {
            _legacyCollider = GetComponent<Collider>();
            var anim = GetComponentInParent<Animator>();
            _player = anim != null ? anim.transform : transform.parent;
            _boneR = HitboxBones.Find(_player, HitboxBones.RightHandAttach);
            _boneL = HitboxBones.Find(_player, HitboxBones.LeftHandAttach);
            _weaponVisual = _player != null ? _player.GetComponent<PlayerWeaponVisual>() : null;
            gameObject.SetActive(false); // 預設關閉
        }

        /// <summary>啟用前設定傷害值並清空命中記錄（舊模式：用 Prefab 上的球）。</summary>
        public void Activate(float damage) => Activate(damage, null, 0f);

        /// <summary>
        /// 啟用判定。shape = null 用舊的球；有形狀就照形狀開。
        /// duration = 這一下有幾秒（往前飛的判定在這段時間內飛完全程）。
        /// </summary>
        public void Activate(float damage, HitboxShape shape, float duration)
            => Activate(damage, shape, duration, HitImpactLevel.Light);

        /// <summary>同上，並指定打中時的打擊感檔位。</summary>
        public void Activate(float damage, HitboxShape shape, float duration, HitImpactLevel impact)
        {
            _damage = damage;
            Impact = impact;
            _hitTargets.Clear();
            HitBossThisSwing = false;
            SwingId++;
            ActiveShape = shape;
            _activeTime = 0f;
            _activeDuration = Mathf.Max(0f, duration);

            if (_legacyCollider != null) _legacyCollider.enabled = shape == null;
            if (shape == null)
            {
                ActiveSlotCount = 0;
                for (int i = 0; i < MaxSlots; i++) if (_slots[i] != null) _slots[i].go.SetActive(false);
            }
            else
            {
                ActiveSlotCount = Mathf.Min(MaxSlots, HitboxShape.GetAnchorFrames(shape.anchor, _player, _handR, _handL, _frames));
                for (int i = 0; i < MaxSlots; i++)
                {
                    bool on = i < ActiveSlotCount;
                    if (on) EnsureSlot(i);
                    if (_slots[i] != null) _slots[i].go.SetActive(on);
                    if (on)
                    {
                        ConfigureColliders(_slots[i], shape);
                        _projectileStart[i] = shape.ToWorld(_frames[i]);
                    }
                }
                var fwd = _player != null ? _player.forward : transform.forward;
                fwd.y = 0f;
                _projectileDir = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
                UpdateSlotPoses();   // 第一個物理步就在正確位置
            }
            gameObject.SetActive(true);
        }

        public void Deactivate() => gameObject.SetActive(false);

        // 判定位置在動畫跑完之後才更新（手骨的位置要等 Animator 算完）
        private void LateUpdate()
        {
            if (ActiveShape == null) return;
            _activeTime += Time.deltaTime;
            UpdateSlotPoses();
        }

        private void UpdateSlotPoses()
        {
            var shape = ActiveShape;
            if (shape == null) return;
            if (shape.motion == HitboxMotion.Follow)
                HitboxShape.GetAnchorFrames(shape.anchor, _player, _handR, _handL, _frames);

            float t = _activeDuration > 0f ? Mathf.Clamp01(_activeTime / _activeDuration) : 1f;
            for (int i = 0; i < ActiveSlotCount; i++)
            {
                Pose pose;
                if (shape.motion == HitboxMotion.Projectile)
                {
                    var start = _projectileStart[i];
                    pose = new Pose(start.position + _projectileDir * shape.projectileDistance * t, start.rotation);
                }
                else
                {
                    pose = i < _frames.Count ? shape.ToWorld(_frames[i]) : _projectileStart[i];
                }
                var tr = _slots[i].go.transform;
                tr.SetPositionAndRotation(pose.position, pose.rotation);
                // 判定槽的世界縮放固定為 1，尺寸直接用公尺
                var ps = transform.lossyScale;
                tr.localScale = new Vector3(1f / Mathf.Max(0.0001f, ps.x), 1f / Mathf.Max(0.0001f, ps.y), 1f / Mathf.Max(0.0001f, ps.z));
            }
        }

        private void EnsureSlot(int i)
        {
            if (_slots[i] != null) return;
            var go = new GameObject($"HitboxSlot{i}");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var slot = new Slot
            {
                go = go,
                sphere = go.AddComponent<SphereCollider>(),
                box = go.AddComponent<BoxCollider>(),
                capsule = go.AddComponent<CapsuleCollider>(),
            };
            slot.sphere.isTrigger = slot.box.isTrigger = slot.capsule.isTrigger = true;
            slot.capsule.direction = 2;   // 膠囊沿 Z 軸
            go.AddComponent<PlayerAttackHitboxRelay>().Owner = this;
            _slots[i] = slot;
        }

        private static void ConfigureColliders(Slot slot, HitboxShape shape)
        {
            slot.sphere.enabled = shape.shape == HitboxShapeType.Sphere;
            slot.box.enabled = shape.shape == HitboxShapeType.Box;
            slot.capsule.enabled = shape.shape == HitboxShapeType.Capsule;
            slot.sphere.radius = shape.Radius;
            slot.box.size = new Vector3(Mathf.Max(0.01f, shape.size.x), Mathf.Max(0.01f, shape.size.y), Mathf.Max(0.01f, shape.size.z));
            slot.capsule.radius = shape.Radius;
            slot.capsule.height = shape.CapsuleLength;
        }

        private void OnTriggerEnter(Collider other) => TryHit(other);

        // 出招表的連續兩下之間判定可能沒有關掉（區間緊接著，或下一招直接接上）：
        // 碰撞體一直開著、敵人一直在範圍內，就不會再觸發 OnTriggerEnter。
        // Activate 會清空命中記錄，這裡用 Stay 補上「已經在範圍內的敵人」這一下的命中；
        // 同一次揮擊每個目標仍只打一次（_hitTargets 擋掉）。
        private void OnTriggerStay(Collider other) => TryHit(other);

        internal void TryHit(Collider other)
        {
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null) return;
            if (_hitTargets.Contains(target)) return;

            _hitTargets.Add(target);
            TotalHits++;
            if (target is IBossHealth) HitBossThisSwing = true;

            bool wasAlive = !IsBossDead(target);
            target.TakeDamage(_damage);

            // 打倒 Boss 的那一下 = 極重（含慢動作）；其他照這一下的檔位
            var level = Impact;
            if (wasAlive && IsBossDead(target)) level = HitImpactLevel.Extreme;
            HitFeedback.PlayerHit(level, target as Component);
        }

        private static bool IsBossDead(IDamageable target) => target switch
        {
            IBossHealth boss => boss.IsDead,
            WrathBossController wrath => wrath.IsDead,   // 嗔還沒實作 IBossHealth
            _ => false,
        };
    }

    /// <summary>判定槽（子物件）的觸發事件轉給 PlayerAttackHitbox。</summary>
    public class PlayerAttackHitboxRelay : MonoBehaviour
    {
        public PlayerAttackHitbox Owner { get; set; }
        private void OnTriggerEnter(Collider other) { if (Owner != null) Owner.TryHit(other); }
        private void OnTriggerStay(Collider other) { if (Owner != null) Owner.TryHit(other); }
    }
}
