using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin
{
    /// <summary>
    /// 音效播放器（靜態）。第一次播放時自己建一個跨場景存活的物件，裡面有一組 AudioSource 輪流使用，
    /// 所以場景裡不用放任何東西，也不用在每個物件上掛 AudioSource。
    /// 一般不要直接用，請呼叫 SoundEffectData.Play() / PlayAt()。
    /// </summary>
    public static class SfxPlayer
    {
        private const int PoolSize = 12;   // 同時最多幾個音效一起播

        private static GameObject _root;
        private static AudioSource[] _pool;
        private static float[] _busyUntil;   // 每個 AudioSource 預計播完的時間（含延遲），延遲中的也算忙碌
        private static int _next;

        // 關閉 Domain Reload 進 Play Mode 時，靜態變數不會自動清掉，要手動重置。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _root = null;
            _pool = null;
            _busyUntil = null;
            _next = 0;
        }

        public static void Play(SoundEffectData data, Vector3? position)
        {
            if (data == null) return;
            if (!data.TryPick(out AudioClip clip, out float volume, out float pitch)) return;
            PlayClip(clip, volume, pitch, data.MixerGroup, data.SpatialBlend, position, data.Delay);
        }

        /// <summary>直接播一個 AudioClip（給主角素材總表這類「直接拖音檔」的欄位用）。clip 為空就什麼都不做。</summary>
        public static void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f,
            AudioMixerGroup mixerGroup = null, float spatialBlend = 0f, Vector3? position = null, float delay = 0f)
        {
            if (clip == null) return;

            int index = Acquire();
            AudioSource source = _pool[index];
            source.Stop();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.outputAudioMixerGroup = mixerGroup;
            source.spatialBlend = spatialBlend;
            source.transform.position = position ?? Vector3.zero;

            if (delay > 0f) source.PlayDelayed(delay);
            else source.Play();

            _busyUntil[index] = Time.realtimeSinceStartup + delay + clip.length / Mathf.Max(0.01f, pitch);
        }

        private static int Acquire()
        {
            if (_root == null || _pool == null)
            {
                _root = new GameObject("[SfxPlayer]");
                Object.DontDestroyOnLoad(_root);
                _pool = new AudioSource[PoolSize];
                _busyUntil = new float[PoolSize];
                for (int i = 0; i < PoolSize; i++)
                {
                    var go = new GameObject($"Sfx_{i}");
                    go.transform.SetParent(_root.transform, false);
                    var src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    _pool[i] = src;
                }
                _next = 0;
            }

            // 先找沒在播的；全都在播就輪流蓋掉最舊的
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < PoolSize; i++)
            {
                int idx = (_next + i) % PoolSize;
                if (!_pool[idx].isPlaying && now >= _busyUntil[idx])
                {
                    _next = (idx + 1) % PoolSize;
                    return idx;
                }
            }
            int stolen = _next;
            _next = (_next + 1) % PoolSize;
            return stolen;
        }
    }
}
