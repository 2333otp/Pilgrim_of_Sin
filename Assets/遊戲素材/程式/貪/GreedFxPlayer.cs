using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 播放一組音效 / 配音 / 特效（IBossFx：Boss 的 BossActionEntry、天秤的 GreedFx），貪 Boss 與天秤共用。
    /// 不用計時器排程，而是由呼叫端每幀回報「這段動畫播到哪」（Tick），進度跨過設定的時間點才觸發：
    /// 這樣停格時會正確等待，切到下一段動作時也自然不會再觸發上一段的。
    /// </summary>
    public class GreedFxPlayer
    {
        private readonly Transform _root;
        private readonly System.Func<AudioMixerGroup> _mixer;
        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();
        private readonly Dictionary<AudioClip[], AudioClip> _lastPick = new Dictionary<AudioClip[], AudioClip>();

        private IBossFx _current;
        private bool _sfxDone, _voiceDone;
        private bool[] _vfxDone = new bool[0];

        /// <param name="root">特效生成的基準物件（骨骼也從它底下找），音效也在它的位置播。</param>
        /// <param name="mixer">輸出的 Mixer 群組（每次播放時才讀，SO 改了會立刻生效）。</param>
        public GreedFxPlayer(Transform root, System.Func<AudioMixerGroup> mixer)
        {
            _root = root;
            _mixer = mixer;
        }

        /// <summary>開始一段新動作（取代上一段，上一段還沒觸發的不再觸發）。進度 0 的項目立刻觸發。</summary>
        public void Begin(IBossFx fx)
        {
            _current = fx;
            _sfxDone = _voiceDone = false;
            int n = fx?.Vfx?.Length ?? 0;
            if (_vfxDone.Length != n) _vfxDone = new bool[n];
            else System.Array.Clear(_vfxDone, 0, n);
            Tick(0f);
        }

        /// <summary>回報目前動畫進度（0~1），跨過時間點的項目就觸發。</summary>
        public void Tick(float progress)
        {
            var fx = _current;
            if (fx == null) return;

            if (!_sfxDone && progress >= fx.SfxNormalizedTime)
            {
                _sfxDone = true;
                PlayRandom(fx.Sfx, fx.SfxVolume);
            }
            if (!_voiceDone && progress >= fx.VoiceNormalizedTime)
            {
                _voiceDone = true;
                PlayRandom(fx.GetVoice(GameLanguage.Current), fx.VoiceVolume);
            }
            var vfxList = fx.Vfx; if (vfxList == null) return;
            for (int i = 0; i < vfxList.Length && i < _vfxDone.Length; i++)
            {
                if (_vfxDone[i]) continue;
                var v = vfxList[i];
                if (v == null || v.prefab == null) { _vfxDone[i] = true; continue; }
                if (progress < v.normalizedTime) continue;
                _vfxDone[i] = true;
                SpawnVfx(v);
            }
        }

        /// <summary>結束目前動作（之後的 Tick 不再觸發任何東西）。</summary>
        public void Stop() => _current = null;

        /// <summary>一次觸發整組（不看時間點）。不影響目前動作的排程。</summary>
        public void FireAll(IBossFx fx, bool playVoice = true)
        {
            if (fx == null) return;
            PlayRandom(fx.Sfx, fx.SfxVolume);
            if (playVoice) PlayRandom(fx.GetVoice(GameLanguage.Current), fx.VoiceVolume);
            var vfxList = fx.Vfx; if (vfxList == null) return;
            foreach (var v in vfxList)
                if (v != null && v.prefab != null) SpawnVfx(v);
        }

        // ── 播放 ──────────────────────────────────────────────────────

        /// <summary>從清單隨機播一個（跳過空格；有 2 個以上時避免連續兩次播到同一個）。</summary>
        private void PlayRandom(AudioClip[] list, float volume)
        {
            if (list == null || list.Length == 0) return;
            int count = 0;
            foreach (var c in list) if (c != null) count++;
            if (count == 0) return;

            _lastPick.TryGetValue(list, out var last);
            AudioClip chosen = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int pick = Random.Range(0, count);
                foreach (var c in list)
                {
                    if (c == null) continue;
                    if (pick-- != 0) continue;
                    chosen = c;
                    break;
                }
                if (count == 1 || chosen != last) break;
            }
            _lastPick[list] = chosen;
            SfxPlayer.PlayClip(chosen, volume, 1f, _mixer?.Invoke(), 0f, _root.position);
        }

        private void SpawnVfx(PlayerVfxEntry v)
        {
            Transform bone = FindBone(v.boneName);
            // 偏移用根物件的方向換算（骨架匯入時常帶縮放，骨骼座標軸不直觀）
            Vector3 worldPos = bone.position + _root.TransformDirection(v.positionOffset);
            Quaternion worldRot = _root.rotation * Quaternion.Euler(v.rotationOffset);

            var instance = Object.Instantiate(v.prefab, worldPos, worldRot);
            if (v.followBone) instance.transform.SetParent(bone, true);
            if (v.lifetime > 0f) Object.Destroy(instance, v.lifetime);
        }

        private Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return _root;
            if (_boneCache.TryGetValue(boneName, out var cached) && cached != null) return cached;
            var found = FindDescendant(_root, boneName) ?? _root;
            _boneCache[boneName] = found;
            return found;
        }

        private static Transform FindDescendant(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDescendant(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
