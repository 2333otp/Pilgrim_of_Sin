using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin
{
    /// <summary>
    /// 音效資料（一個「需求」一個資產，例如「撿起錢袋」）。
    ///
    /// 【使用方式】
    ///   1. 在 Project 視窗按右鍵 → Create → PilgrimOfSin → 音效 (Sound Effect) 建立新的音效資產。
    ///   2. 在「音檔變體」把音檔拖進去，要幾個就幾個（Size 直接改數字，或按 + / -）。
    ///      每次播放會從中隨機抽一個；只放一個就永遠播那個。
    ///   3. 程式端只需要拿到這個資產，呼叫 Play() 即可，完全不用管有幾個變體。
    ///
    /// 音檔還沒拖進去（空的）時 Play() 什麼都不會發生，也不會報錯，所以可以先掛好、之後再補音檔。
    /// </summary>
    [CreateAssetMenu(fileName = "SFX_新音效", menuName = "PilgrimOfSin/音效 (Sound Effect)")]
    public class SoundEffectData : ScriptableObject
    {
        [Header("音檔變體（可任意增減，每次播放隨機抽一個）")]
        [SerializeField] private AudioClip[] _variants = new AudioClip[0];

        [Tooltip("勾選：有 2 個以上變體時，不會連續兩次抽到同一個，聽起來比較不重複。")]
        [SerializeField] private bool _avoidImmediateRepeat = true;

        [Header("播放設定")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 1f;

        [Tooltip("每次播放的音高隨機範圍（X = 最低、Y = 最高，1 = 原音高）。\n" +
                 "例如 0.95 ~ 1.05 可以讓重複聽到的同一個音效不死板。兩個都填 1 就不變音高。")]
        [SerializeField] private Vector2 _pitchRange = Vector2.one;

        [Tooltip("播放前延遲幾秒。用來對齊動畫的出手/落下那一幀（例如天秤翻倒的撞擊聲）。")]
        [SerializeField, Min(0f)] private float _delay = 0f;

        [Tooltip("兩次播放之間的最短間隔（秒）。避免同一個動作在同一瞬間觸發多次、疊成爆音。0 = 不限制。")]
        [SerializeField, Min(0f)] private float _minInterval = 0.05f;

        [Header("輸出")]
        [Tooltip("要輸出到哪個 Mixer 群組（預設已指到 SFX，會跟著遊戲設定裡的音效音量走）。")]
        [SerializeField] private AudioMixerGroup _mixerGroup;

        [Tooltip("0 = 2D（不管位置，所有地方一樣大聲）；1 = 3D（依距離變小聲，需用 PlayAt 帶位置）。")]
        [Range(0f, 1f)]
        [SerializeField] private float _spatialBlend = 0f;

        // 執行期狀態（不存檔）
        private int _lastIndex = -1;
        private float _lastPlayTime = float.NegativeInfinity;

        // ── 對外資訊 ──────────────────────────────────────────────────
        public AudioMixerGroup MixerGroup => _mixerGroup;
        public float SpatialBlend => _spatialBlend;
        public float Delay => _delay;

        /// <summary>有放進去（非空）的音檔數量。</summary>
        public int VariantCount
        {
            get
            {
                int n = 0;
                if (_variants != null)
                    foreach (var c in _variants) if (c != null) n++;
                return n;
            }
        }

        // ── 播放 ──────────────────────────────────────────────────────

        /// <summary>播放（2D）。沒有音檔時什麼都不做。</summary>
        public void Play() => SfxPlayer.Play(this, null);

        /// <summary>在指定位置播放（搭配 Spatial Blend 才有距離衰減）。</summary>
        public void PlayAt(Vector3 position) => SfxPlayer.Play(this, position);

        /// <summary>
        /// 抽出這次要播的音檔與音量、音高。受「最短間隔」限制時回傳 false（這次不播）。
        /// 由 SfxPlayer 呼叫，一般不需要直接用。
        /// </summary>
        public bool TryPick(out AudioClip clip, out float volume, out float pitch)
        {
            clip = null;
            volume = _volume;
            pitch = 1f;

            float now = Time.realtimeSinceStartup;
            if (_minInterval > 0f && now - _lastPlayTime < _minInterval) return false;

            int count = VariantCount;
            if (count == 0) return false;

            int pick = Random.Range(0, count);
            if (_avoidImmediateRepeat && count > 1 && pick == _lastIndex)
                pick = (pick + 1 + Random.Range(0, count - 1)) % count;   // 從「其餘的」裡挑，不會重複
            _lastIndex = pick;

            // 第 pick 個「非空」的音檔（中間有空格的話要跳過）
            int seen = 0;
            foreach (var c in _variants)
            {
                if (c == null) continue;
                if (seen == pick) { clip = c; break; }
                seen++;
            }
            if (clip == null) return false;

            float lo = Mathf.Min(_pitchRange.x, _pitchRange.y);
            float hi = Mathf.Max(_pitchRange.x, _pitchRange.y);
            pitch = (lo <= 0f && hi <= 0f) ? 1f : Random.Range(Mathf.Max(0.01f, lo), Mathf.Max(0.01f, hi));

            _lastPlayTime = now;
            return true;
        }
    }
}
