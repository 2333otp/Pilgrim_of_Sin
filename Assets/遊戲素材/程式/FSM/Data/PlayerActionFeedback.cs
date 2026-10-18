using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 讀取 PlayerProfile，負責三件事：
    ///   1. 把 Profile 裡換掉的 AnimationClip 套進 Animator（AnimatorOverrideController）。
    ///   2. 提供「依動畫 Clip 實際長度」的動作時間給各狀態使用。
    ///   3. 動作開始時播放該動作的音效、配音、特效（欄位為空就什麼都不做，不報錯）。
    /// 沒掛這個元件、或沒指定 Profile 時，主角行為與沒有這套系統時完全一樣。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerActionFeedback : MonoBehaviour
    {
        [SerializeField] private PlayerProfile _profile;

        private PlayerController _player;
        private Dictionary<string, PlayerActionEntry> _byState;
        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();
        private readonly List<Coroutine> _pending = new List<Coroutine>();

        public PlayerProfile Profile => _profile;

        // ── 初始化（由 PlayerController.Awake 在 Animator 參數設定前呼叫）──────
        public void Initialize(Animator animator)
        {
            _player = GetComponent<PlayerController>();
            BuildLookup();
            ApplyAnimationOverrides(animator);
        }

        private void BuildLookup()
        {
            _byState = new Dictionary<string, PlayerActionEntry>();
            if (_profile == null) return;
            foreach (var e in _profile.AllEntries())
                if (!string.IsNullOrEmpty(e.stateName)) _byState.TryAdd(e.stateName, e);
        }

        private PlayerActionEntry Find(string stateName)
        {
            if (_profile == null || string.IsNullOrEmpty(stateName)) return null;
            if (_byState == null) BuildLookup(); // Domain Reload 後字典會被清空
            return _byState.TryGetValue(stateName, out var e) ? e : null;
        }

        /// <summary>
        /// 只有真的有 Clip 被換掉時才建立 OverrideController；全部維持原樣就完全不碰 Animator。
        /// 必須在 Animator 參數設定之前呼叫（換 Controller 會重置參數）。
        /// </summary>
        private void ApplyAnimationOverrides(Animator animator)
        {
            if (_profile == null || animator == null || animator.runtimeAnimatorController == null) return;

            var swaps = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var e in _profile.AllEntries())
                if (e.clip != null && e.baseClip != null && e.clip != e.baseClip)
                    swaps[e.baseClip] = e.clip;
            if (swaps.Count == 0) return;

            var overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                if (swaps.TryGetValue(pairs[i].Key, out var replacement))
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, replacement);
            overrideController.ApplyOverrides(pairs);
            animator.runtimeAnimatorController = overrideController;
        }

        // ── 動作時間 ─────────────────────────────────────────────────
        /// <summary>該 state 有放 Clip 時，回傳 Clip 長度 + 餘裕。</summary>
        public bool TryGetDuration(string stateName, out float duration)
        {
            var e = Find(stateName);
            if (e != null && e.HasClipDuration) { duration = e.ClipDuration; return true; }
            duration = 0f;
            return false;
        }

        /// <summary>同 TryGetDuration，但 state 名稱由「動作前綴 + 目前武器後綴」組成（如 Damaged → Damaged_Pencil）。</summary>
        public bool TryGetCurrentWeaponDuration(string actionPrefix, out float duration)
            => TryGetDuration(actionPrefix + "_" + Suffix(CurrentWeapon), out duration);

        /// <summary>該 state 有自訂保險時間就用它，否則用程式內建的值。</summary>
        public float GetFallback(string stateName, float defaultValue)
        {
            var e = Find(stateName);
            return e != null && e.fallbackDuration > 0f ? e.fallbackDuration : defaultValue;
        }

        // ── 動作通知（由 PlayerState.PlayAnimation / CrossFadeAnimation 呼叫）──
        public void NotifyTrigger(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return;
            string state;
            if (trigger.StartsWith("WeaponSwitch_") && int.TryParse(trigger.Substring("WeaponSwitch_".Length), out int target))
                state = "WeaponSwitch_" + Suffix(target);  // 切換動畫依「目標武器」
            else if (trigger == "Roll" || trigger == "Death")
                state = trigger;
            else
                state = trigger + "_" + Suffix(CurrentWeapon); // LightAttack / HeavyAttack / Combo1~4 / SpecialSkill / Jump / Fall
            NotifyState(state);
        }

        public void NotifyState(string stateName)
        {
            var entry = Find(stateName);
            CancelPending();
            if (entry != null) Fire(entry);
        }

        private void Fire(PlayerActionEntry entry)
        {
            float length = entry.HasClipDuration ? entry.ClipDuration : 0f;

            Schedule(entry.sfxNormalizedTime * length, () => PlayRandom(entry.sfx, transform.position));
            Schedule(entry.voiceNormalizedTime * length, () => PlayRandom(entry.voice, transform.position));

            if (entry.vfx == null) return;
            foreach (var v in entry.vfx)
            {
                if (v == null || v.prefab == null) continue;
                var vfx = v;
                Schedule(vfx.normalizedTime * length, () => SpawnVfx(vfx));
            }
        }

        /// <summary>延遲 0 以內就立刻執行；否則等指定秒數（受 timeScale 影響，暫停時會跟著停）。</summary>
        private void Schedule(float delay, System.Action action)
        {
            if (delay <= 0.0001f) action();
            else _pending.Add(StartCoroutine(RunDelayed(delay, action)));
        }

        private static IEnumerator RunDelayed(float delay, System.Action action)
        {
            yield return new WaitForSeconds(delay);
            action();
        }

        private static void PlayRandom(SoundEffectData[] list, Vector3 pos)
        {
            if (list == null || list.Length == 0) return;
            int count = 0;
            foreach (var s in list) if (s != null) count++;
            if (count == 0) return;
            int pick = Random.Range(0, count);
            foreach (var s in list)
            {
                if (s == null) continue;
                if (pick-- == 0) { s.PlayAt(pos); return; }
            }
        }

        private void CancelPending()
        {
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i] != null) StopCoroutine(_pending[i]);
            _pending.Clear();
        }

        private void SpawnVfx(PlayerVfxEntry v)
        {
            Transform bone = FindBone(v.boneName);
            // 偏移用角色根物件的方向換算，不用骨骼的（骨架匯入時帶 100 倍縮放，骨骼座標軸也不直觀）
            Vector3 worldPos = bone.position + transform.TransformVector(v.positionOffset);
            Quaternion worldRot = transform.rotation * Quaternion.Euler(v.rotationOffset);

            var instance = Instantiate(v.prefab, worldPos, worldRot);
            if (v.followBone) instance.transform.SetParent(bone, true); // true = 保持世界座標與大小，縮放不會被骨架放大
            if (v.lifetime > 0f) Destroy(instance, v.lifetime);
        }

        private Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return transform;
            if (_boneCache.TryGetValue(boneName, out var cached) && cached != null) return cached;
            var found = FindDescendant(transform, boneName);
            if (found == null) found = transform;
            _boneCache[boneName] = found;
            return found;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDescendant(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // ── 武器後綴（與 PlayerState.WeaponSuffix 一致）────────────────
        private int CurrentWeapon
        {
            get
            {
                if (_player == null) _player = GetComponent<PlayerController>();
                return _player != null && _player.Combat != null ? _player.Combat.CurrentWeaponIndex : 1;
            }
        }

        public static string Suffix(int weaponIndex) => weaponIndex switch
        {
            1 => "Pencil",
            2 => "Brush",
            3 => "PaintKnife",
            4 => "Palette",
            _ => "Pencil",
        };
    }
}
