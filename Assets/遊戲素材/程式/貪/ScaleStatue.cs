using System.Collections;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤左碗裡的雕像（永久配重）。掛在雕像錨點（碗底中心）上，雕像模型是它的子物件。
    ///
    /// 【跟著碗】錨點每幀用「碗骨頭相對平衡姿勢的旋轉」加「平衡姿勢下骨頭→碗底中心的偏移」算出位置與朝向
    ///   （實測在所有天秤動畫姿勢下誤差 0.000m / 0.1°，連碗翻到 107° 也準），所以傾斜、重製(MoneyBagUpdate)
    ///   時雕像會隨碗整個移動旋轉，不穿模。ScaleBowlAnchor 只跟位置，雕像要連旋轉一起跟，所以另寫。
    /// 【踢翻 Break】碗被踢飛的瞬間（骨頭速度超過門檻）雕像脫離碗，沿拋物線甩出去、落地躺一下、沉進地板消失；
    ///   等天秤動畫回到待機姿勢，雕像在碗裡縮放彈出（永久配重，每一輪都在）。
    /// 【重製 HitReset】不特別演出，隨碗晃動。
    /// </summary>
    public class ScaleStatue : MonoBehaviour
    {
        private enum State { Seated, AwaitKick, Flying, Gone }

        [Header("References")]
        [SerializeField] private ScaleObject _scale;
        [SerializeField] private Animator _libraAnimator;
        [SerializeField] private Transform _bowlBone;   // DEF-chain04_L_end（左碗鏈條末端骨頭）
        [SerializeField] private Transform _statue;     // 雕像模型根，必須是本物件的子物件

        [Header("碗內定位（用動畫平衡姿勢量測，勿隨意改）")]
        [Tooltip("平衡姿勢下，骨頭 → 碗底中心 的世界偏移")]
        [SerializeField] private Vector3 _floorOffsetAtRest = new Vector3(0.04229f, 0.62439f, -0.00087f);
        [Tooltip("平衡姿勢下骨頭的世界旋轉（歐拉角）。碗水平的時候骨頭就是這個旋轉")]
        [SerializeField] private Vector3 _restBoneEuler = new Vector3(0f, 90f, 180f);

        [Header("踢翻：雕像被甩出去")]
        [Tooltip("碗骨頭速度超過這個值（m/s）就視為被踢飛。Break 動畫蓄力時最高約 1.6，被踢瞬間 8 以上")]
        [SerializeField] private float _kickSpeed = 6f;
        [Tooltip("Break 動畫（2.46 秒）播到多少比例之後才開始偵測踢飛。實測被踢飛約在 1.4 秒 = 0.57")]
        [SerializeField, Range(0f, 0.9f)] private float _kickMinNormalizedTime = 0.5f;
        [SerializeField] private float _flightDuration = 0.75f;
        [SerializeField] private float _apexHeight = 2.5f;
        [Tooltip("相對起飛點的水平位移（世界座標）。Break 動畫把左碗往 +Z 甩出約 6.7m")]
        [SerializeField] private Vector2 _landOffsetXZ = new Vector2(0.5f, 4.5f);
        [Tooltip("飛行中總共翻滾的角度（落地時躺平 = 450 度 = 一圈多 90 度）")]
        [SerializeField] private float _tumbleDegrees = 450f;
        [Tooltip("地板頂面的世界 Y（GreedBossScene 的「地板」頂面 = 0）")]
        [SerializeField] private float _groundY = 0f;
        [Tooltip("躺平落地時雕像軸心離地的高度（雕像身體有厚度，避免一半陷進地板）")]
        [SerializeField] private float _lieHeight = 0.3f;
        [SerializeField] private float _lieDuration = 1.2f;
        [SerializeField] private float _sinkDuration = 0.8f;
        [SerializeField] private float _sinkDepth = 3.5f;
        [Tooltip("踢翻事件後最久等這麼久還沒偵測到被踢飛，就強制甩出（保險）")]
        [SerializeField] private float _kickTimeout = 4f;

        [Header("重新出現")]
        [SerializeField] private float _popDuration = 0.4f;

        [Header("音效（可留空）")]
        [SerializeField] private SoundEffectData _landSfx;
        [SerializeField] private SoundEffectData _respawnSfx;

        // 雕像在錨點底下原本的座位（Awake 時記下來，重新出現時還原）
        private Vector3 _seatLocalPos;
        private Quaternion _seatLocalRot;
        private Vector3 _seatLocalScale;
        private Quaternion _restRot;

        private State _state = State.Seated;
        private Vector3 _lastBonePos;
        private float _awaitStartTime;

        private static readonly string[] SettledStates = { "Center", "ToLeft", "ToRight", "LeftToCenter", "RightToCenter" };

        private void Awake()
        {
            _restRot = Quaternion.Euler(_restBoneEuler);
            if (_statue != null)
            {
                _seatLocalPos = _statue.localPosition;
                _seatLocalRot = _statue.localRotation;
                _seatLocalScale = _statue.localScale;
            }
        }

        private void OnEnable()
        {
            if (_scale == null) return;
            _scale.OnBreakStarted += HandleBreakStarted;
        }

        private void OnDisable()
        {
            if (_scale == null) return;
            _scale.OnBreakStarted -= HandleBreakStarted;
        }

        private void HandleBreakStarted()
        {
            if (_state != State.Seated) return;
            _state = State.AwaitKick;
            _awaitStartTime = Time.time;
            _lastBonePos = _bowlBone != null ? _bowlBone.position : Vector3.zero;
        }

        private void LateUpdate()
        {
            SnapToBowl();

            switch (_state)
            {
                case State.AwaitKick:
                    UpdateAwaitKick();
                    break;
                case State.Gone:
                    TryRespawn();
                    break;
            }
        }

        /// <summary>把錨點貼到目前碗的位置與朝向（也給編輯器貼齊用）。</summary>
        [ContextMenu("貼齊目前碗的位置")]
        public void SnapToBowl()
        {
            if (_bowlBone == null) return;
            var rest = Application.isPlaying ? _restRot : Quaternion.Euler(_restBoneEuler);
            var rel = _bowlBone.rotation * Quaternion.Inverse(rest);
            transform.SetPositionAndRotation(_bowlBone.position + rel * _floorOffsetAtRest, rel);
        }

        private void UpdateAwaitKick()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;   // 暫停中

            float speed = (_bowlBone.position - _lastBonePos).magnitude / dt;
            _lastBonePos = _bowlBone.position;

            // 只在 Break 動畫本體播到一半之後才偵測。動畫剛切進 Break（例如從左傾狀態切過來）時
            // 骨頭會瞬間跳到 Break 的起始姿勢，速度會暴衝，不能當成被踢飛。
            if (_libraAnimator != null && Time.time - _awaitStartTime <= _kickTimeout)
            {
                var info = _libraAnimator.GetCurrentAnimatorStateInfo(0);
                if (_libraAnimator.IsInTransition(0) || !info.IsName("Break") || info.normalizedTime < _kickMinNormalizedTime)
                    return;
            }

            if (speed > _kickSpeed || Time.time - _awaitStartTime > _kickTimeout)
                StartCoroutine(FlyRoutine());
        }

        private IEnumerator FlyRoutine()
        {
            _state = State.Flying;

            Vector3 startPos = _statue.position;
            Quaternion startRot = _statue.rotation;
            _statue.SetParent(null, true);

            var landPos = new Vector3(startPos.x + _landOffsetXZ.x, _groundY + _lieHeight, startPos.z + _landOffsetXZ.y);
            Vector3 flightDir = new Vector3(landPos.x - startPos.x, 0f, landPos.z - startPos.z).normalized;
            Vector3 tumbleAxis = Vector3.Cross(Vector3.up, flightDir);   // 往飛行方向前翻

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, _flightDuration);
                float k = Mathf.Clamp01(t);
                Vector3 p = Vector3.Lerp(startPos, landPos, k);
                p.y += _apexHeight * 4f * k * (1f - k);
                _statue.SetPositionAndRotation(p, Quaternion.AngleAxis(_tumbleDegrees * k, tumbleAxis) * startRot);
                yield return null;
            }

            _landSfx?.Play();
            yield return new WaitForSeconds(_lieDuration);

            Vector3 lyingPos = _statue.position;
            float s = 0f;
            while (s < 1f)
            {
                s += Time.deltaTime / Mathf.Max(0.01f, _sinkDuration);
                float k = Mathf.Clamp01(s);
                _statue.position = lyingPos + Vector3.down * (_sinkDepth * k * k);   // 越沉越快，從地板底下消失
                yield return null;
            }

            _statue.gameObject.SetActive(false);
            _state = State.Gone;
        }

        private void TryRespawn()
        {
            if (_libraAnimator == null || _libraAnimator.IsInTransition(0)) return;

            var info = _libraAnimator.GetCurrentAnimatorStateInfo(0);
            bool settled = false;
            foreach (var n in SettledStates)
                if (info.IsName(n)) { settled = true; break; }
            if (!settled) return;

            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            _state = State.Seated;   // 先設回，避免重複觸發；彈出期間已經跟著碗走

            _statue.SetParent(transform, false);
            _statue.localPosition = _seatLocalPos;
            _statue.localRotation = _seatLocalRot;
            _statue.localScale = Vector3.zero;
            _statue.gameObject.SetActive(true);
            _respawnSfx?.Play();

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, _popDuration);
                _statue.localScale = _seatLocalScale * EaseOutBack(Mathf.Clamp01(t));
                yield return null;
            }
            _statue.localScale = _seatLocalScale;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
