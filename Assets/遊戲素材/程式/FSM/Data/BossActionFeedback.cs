using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss 版的素材播放器（對應主角的 PlayerActionFeedback），三隻 Boss 共用：
    ///   1. 把素材總表裡換掉的 AnimationClip 套進 Animator（AnimatorOverrideController）。
    ///   2. 動作開始時依時間點播放音效、配音、特效（欄位為空就什麼都不做，不報錯）。
    ///   3. 攻擊動作在命中時間點呼叫命中判定。
    /// 由 Boss 的 Controller 在 Awake 呼叫 Initialize，Boss 本身不用掛別的東西。
    /// </summary>
    [DisallowMultipleComponent]
    public class BossActionFeedback : MonoBehaviour
    {
        private AudioMixerGroup _mixer;
        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();
        private readonly List<Coroutine> _pending = new List<Coroutine>();
        private readonly Dictionary<AudioClip[], int> _lastPick = new Dictionary<AudioClip[], int>();

        public void Initialize(Animator animator, IEnumerable<BossActionEntry> entries, AudioMixerGroup mixer)
        {
            _mixer = mixer;
            ApplyAnimationOverrides(animator, entries);
        }

        /// <summary>只有真的有 Clip 被換掉時才建立 OverrideController；全部維持原樣就完全不碰 Animator。</summary>
        private static void ApplyAnimationOverrides(Animator animator, IEnumerable<BossActionEntry> entries)
        {
            if (animator == null || animator.runtimeAnimatorController == null || entries == null) return;

            var swaps = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var e in entries)
            {
                if (e == null || e.clip == null) continue;
                if (e.baseClip == null)
                {
                    Debug.LogWarning($"[BossActionFeedback] 「{e.stateName}」放了 Clip 但沒有 baseClip（Animator 裡可能沒有這個 state），動畫不會被替換。");
                    continue;
                }
                if (e.clip != e.baseClip) swaps[e.baseClip] = e.clip;
            }
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

        /// <summary>
        /// 播放一個動作的音效、配音、特效。
        /// duration：這次動作的長度（各時間點用它換算秒數）。onHit：攻擊動作的命中判定，在 hitNormalizedTime 呼叫。
        /// interrupt：true = 取消上一個動作還沒觸發的音效/特效/命中（換動作時用）；受擊這種疊加的回饋請傳 false。
        /// </summary>
        public void Play(BossActionEntry entry, float duration, Action onHit = null, bool interrupt = true, bool playVoice = true)
        {
            if (interrupt) CancelPending();
            if (entry == null) return;

            Schedule(entry.sfxNormalizedTime * duration, () => PlayRandom(entry.sfx, entry.sfxVolume));
            // 配音在觸發的當下才依目前語言取清單（中途切換語言也會立刻生效）
            if (playVoice)
                Schedule(entry.voiceNormalizedTime * duration, () => PlayRandom(entry.GetVoice(GameLanguage.Current), entry.voiceVolume));

            if (entry.vfx != null)
                foreach (var v in entry.vfx)
                {
                    if (v == null || v.prefab == null) continue;
                    var vfx = v;
                    Schedule(vfx.normalizedTime * duration, () => SpawnVfx(vfx));
                }

            if (onHit != null) Schedule(entry.hitNormalizedTime * duration, onHit);
        }

        public void CancelPending()
        {
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i] != null) StopCoroutine(_pending[i]);
            _pending.Clear();
        }

        private void Schedule(float delay, Action action)
        {
            if (delay <= 0.0001f) action();
            else _pending.Add(StartCoroutine(RunDelayed(delay, action)));
        }

        private static IEnumerator RunDelayed(float delay, Action action)
        {
            yield return new WaitForSeconds(delay); // 受 timeScale 影響，暫停選單開著時會跟著停
            action();
        }

        /// <summary>從清單隨機播一個音檔（跳過空格；有 2 個以上時避免連續兩次播到同一個）。</summary>
        private void PlayRandom(AudioClip[] list, float volume)
        {
            if (list == null || list.Length == 0) return;
            int count = 0;
            foreach (var c in list) if (c != null) count++;
            if (count == 0) return;

            int pick = UnityEngine.Random.Range(0, count);
            if (count > 1 && _lastPick.TryGetValue(list, out int last) && pick == last)
                pick = (pick + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
            _lastPick[list] = pick;

            foreach (var c in list)
            {
                if (c == null) continue;
                if (pick-- != 0) continue;
                SfxPlayer.PlayClip(c, volume, 1f, _mixer, 0f, transform.position);
                return;
            }
        }

        private void SpawnVfx(PlayerVfxEntry v)
        {
            Transform bone = FindBone(v.boneName);
            // 偏移用 Boss 根物件的方向換算，不用骨骼的（骨架匯入時常帶 100 倍縮放，骨骼座標軸也不直觀）
            Vector3 worldPos = bone.position + transform.TransformVector(v.positionOffset);
            Quaternion worldRot = transform.rotation * Quaternion.Euler(v.rotationOffset);

            var instance = Instantiate(v.prefab, worldPos, worldRot);
            if (v.followBone) instance.transform.SetParent(bone, true);
            if (v.lifetime > 0f) Destroy(instance, v.lifetime);
        }

        private Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return transform;
            if (_boneCache.TryGetValue(boneName, out var cached) && cached != null) return cached;
            var found = FindDescendant(transform, boneName) ?? transform;
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
    }
}
