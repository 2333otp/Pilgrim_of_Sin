using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 貪死亡演出：死亡動畫播到開始幀時在腳前擺錢袋（純裝飾），翼尖掃過就把錢袋帶往懷中縮小消失，
    /// 播到結束幀時剩下的一起掃進來。設定在 GreedBossProfile.deathSweep。由 GreedDeadState 每幀呼叫 Tick。
    /// </summary>
    public class GreedMoneySweep
    {
        private class Bag
        {
            public GameObject go;
            public Vector3 baseScale;
            public float spawnTime;
            public float unlockTime;      // 這個時間之後翼尖掃到才會帶走（錯開，讓錢袋分好幾次被掃進來）
            public float pullTime = -1f;
            public Vector3 pullFrom;
        }

        private readonly GreedBossController _boss;
        private readonly GreedDeathSweepConfig _cfg;
        private readonly float _fps;
        private readonly GameObject _prefab;
        private readonly List<Bag> _bags = new List<Bag>();
        private readonly List<Transform> _sweepBones = new List<Transform>();
        private bool _spawned;

        public GreedMoneySweep(GreedBossController boss, GreedDeathSweepConfig cfg, AnimationClip deathClip, GameObject fallbackPrefab)
        {
            _boss = boss;
            _cfg = cfg;
            _fps = deathClip != null && deathClip.frameRate > 0f ? deathClip.frameRate : 30f;
            _prefab = cfg.bagPrefab != null ? cfg.bagPrefab : fallbackPrefab;

            foreach (var name in cfg.sweepBones)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var t = FindDescendant(boss.transform, name);
                if (t != null) _sweepBones.Add(t);
                else Debug.LogWarning($"[GreedMoneySweep] 找不到掃錢袋用的骨頭「{name}」。", boss);
            }
        }

        /// <param name="time">死亡動畫開始後經過的秒數。</param>
        public void Tick(float time)
        {
            if (!_spawned && time >= _cfg.startFrame / _fps)
            {
                _spawned = true;
                Spawn(time);
            }
            if (!_spawned) return;

            bool sweepAll = time >= _cfg.endFrame / _fps;
            float groundY = _boss.GroundY;

            for (int i = _bags.Count - 1; i >= 0; i--)
            {
                var b = _bags[i];
                if (b.go == null) { _bags.RemoveAt(i); continue; }

                if (b.pullTime < 0f)
                {
                    // 冒出來：由小變大（稍微彈一下）
                    float k = _cfg.popInDuration > 0f ? Mathf.Clamp01((time - b.spawnTime) / _cfg.popInDuration) : 1f;
                    b.go.transform.localScale = b.baseScale * EaseOutBack(k);

                    if (sweepAll || k >= 1f && time >= b.unlockTime && TouchedByWing(b.go.transform.position, groundY))
                    {
                        // 結束幀一起掃：稍微錯開，看起來像一把撈進來
                        b.pullTime = sweepAll ? time + Random.Range(0f, 0.12f) : time;
                        b.pullFrom = b.go.transform.position;
                    }
                    continue;
                }

                if (time < b.pullTime) continue;
                float p = Mathf.Clamp01((time - b.pullTime) / _cfg.pullDuration);
                float e = p * p * (3f - 2f * p);
                Vector3 target = _boss.transform.TransformPoint(_cfg.gatherPointLocal);
                b.go.transform.position = Vector3.Lerp(b.pullFrom, target, e) + Vector3.up * Mathf.Sin(p * Mathf.PI) * 0.2f;
                b.go.transform.localScale = b.baseScale * Mathf.Lerp(1f, _cfg.gatherEndScale, e);
                if (p >= 1f)
                {
                    Object.Destroy(b.go);
                    _bags.RemoveAt(i);
                }
            }
        }

        /// <summary>立刻清掉所有錢袋（例如場景結束前）。</summary>
        public void Clear()
        {
            foreach (var b in _bags) if (b.go != null) Object.Destroy(b.go);
            _bags.Clear();
        }

        private bool TouchedByWing(Vector3 bagPos, float groundY)
        {
            foreach (var bone in _sweepBones)
            {
                if (bone == null) continue;
                Vector3 w = bone.position;
                if (w.y - groundY > _cfg.pickupMaxHeight) continue;
                Vector2 d = new Vector2(w.x - bagPos.x, w.z - bagPos.z);
                if (d.sqrMagnitude <= _cfg.pickupRadius * _cfg.pickupRadius) return true;
            }
            return false;
        }

        private void Spawn(float time)
        {
            if (_prefab == null)
            {
                Debug.LogWarning("[GreedMoneySweep] 沒有錢袋 Prefab（素材總表沒填、場景的錢袋生成器也沒有），略過掃錢袋演出。", _boss);
                return;
            }

            Transform root = _boss.transform;
            Vector3 fwd = root.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var placed = new List<Vector2>();

            // 解鎖時間：把 開始幀 ~ 結束幀 的前 85% 平均分給每個錢袋（順序打亂），
            // 翼尖要在解鎖之後掃過才帶走 → 錢袋會分好幾次被掃進懷裡，不會第一下就全沒了
            float t0 = _cfg.startFrame / _fps, t1 = _cfg.endFrame / _fps;
            var order = new List<int>();
            for (int i = 0; i < _cfg.count; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }

            for (int n = 0; n < _cfg.count; n++)
            {
                // 在前方範圍內找一個不跟其他錢袋擠在一起的位置
                Vector2 local = Vector2.zero;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    local = new Vector2(Random.Range(-_cfg.halfWidth, _cfg.halfWidth),
                                        Random.Range(_cfg.forwardRange.x, _cfg.forwardRange.y));
                    bool ok = true;
                    foreach (var q in placed) if ((q - local).sqrMagnitude < _cfg.minSpacing * _cfg.minSpacing) { ok = false; break; }
                    if (ok) break;
                }
                placed.Add(local);

                Vector3 pos = root.position + right * local.x + fwd * local.y;
                pos.y = _boss.GroundY;
                var go = CreateVisualBag(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                float unlock = t0 + (t1 - t0) * 0.85f * (_cfg.count > 1 ? order[n] / (float)(_cfg.count - 1) : 0f);
                var bag = new Bag { go = go, baseScale = go.transform.localScale, spawnTime = time + n * 0.03f, unlockTime = unlock };
                go.transform.localScale = Vector3.zero;
                _bags.Add(bag);
            }
        }

        /// <summary>產生只有外觀的錢袋：在停用的容器裡生成並拿掉碰撞體 / 腳本，確保錢袋腳本的 Awake 不會執行。</summary>
        private GameObject CreateVisualBag(Vector3 groundPos, Quaternion rot)
        {
            var holder = new GameObject("_bagHolder");
            holder.SetActive(false);
            var go = Object.Instantiate(_prefab, holder.transform);
            go.name = "貪_死亡演出錢袋";
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            if (!_cfg.keepBagLight)
                foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;

            go.transform.SetParent(null, false);
            Object.Destroy(holder);
            go.transform.SetPositionAndRotation(groundPos, rot);
            go.transform.localScale = _prefab.transform.localScale * _cfg.bagScale;

            // 讓袋底貼地（Prefab 的原點不一定在底部）
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                go.transform.position += Vector3.up * (groundPos.y - b.min.y);
            }
            return go;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
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
