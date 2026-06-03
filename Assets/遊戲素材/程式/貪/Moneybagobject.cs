using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 錢袋個體。
    /// 負責：
    ///   - 追蹤自己的狀態（地板上 / 天秤上）
    ///   - 玩家走近時顯示「按 X 撿起」提示，按 X 後放上天秤
    ///   - 被玩家攻擊後以拋物線掉回地板
    ///   - 實作 IDamageable，讓 PlayerAttackHitbox 可以打到
    /// </summary>
    public class MoneybagObject : MonoBehaviour, IDamageable
    {
        // ── 狀態 ──────────────────────────────────────────────────────
        public enum BagState { OnGround, OnScale }
        public BagState CurrentState { get; private set; } = BagState.OnGround;

        // ── 數值 ──────────────────────────────────────────────────────
        public float Weight { get; private set; }

        // ── 互動範圍 ──────────────────────────────────────────────────
        [Header("Interaction")]
        [SerializeField] private float _interactRadius = 2f;

        // ── 拋物線掉落 ────────────────────────────────────────────────
        [Header("Knockoff Arc")]
        [SerializeField] private float _arcHeight = 2f;
        [SerializeField] private float _arcDuration = 0.5f;

        // ── 內部 ──────────────────────────────────────────────────────
        private ScaleObject _scale;
        private Transform _scaleRightSide;
        private GameObject _interactPromptUI;
        private Transform _player;
        private bool _playerNearby;
        private bool _isFlying;
        private float _flyTimer;
        private Vector3 _flyStart;
        private Vector3 _flyEnd;

        // ════════════════════════════════════════════════════════════
        //  初始化（由 MoneybagSpawner 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 初始化錢袋。4 個參數版本。
        /// </summary>
        public void Init(float weight, ScaleObject scale, Transform scaleRightSide, GameObject interactPromptUI)
        {
            Weight = weight;
            _scale = scale;
            _scaleRightSide = scaleRightSide;
            _interactPromptUI = interactPromptUI;
            CurrentState = BagState.OnGround;

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj) _player = playerObj.transform;

            if (_interactPromptUI) _interactPromptUI.SetActive(false);
        }

        // ════════════════════════════════════════════════════════════
        //  Update
        // ════════════════════════════════════════════════════════════

        private void Update()
        {
            if (_isFlying)
            {
                UpdateArc();
                return;
            }

            if (CurrentState == BagState.OnGround)
            {
                CheckPlayerProximity();
                CheckPickupInput();
            }
        }

        // ════════════════════════════════════════════════════════════
        //  玩家接近偵測 & 互動提示
        // ════════════════════════════════════════════════════════════

        private void CheckPlayerProximity()
        {
            if (_player == null) return;

            float dist = Vector3.Distance(transform.position, _player.position);
            bool nearby = dist <= _interactRadius;

            if (nearby != _playerNearby)
            {
                _playerNearby = nearby;
                if (_interactPromptUI) _interactPromptUI.SetActive(_playerNearby);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  按 X 撿起
        // ════════════════════════════════════════════════════════════

        private void CheckPickupInput()
        {
            if (!_playerNearby) return;

            // 新版 Input System
            if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                PickUp();
        }

        private void PickUp()
        {
            if (CurrentState != BagState.OnGround) return;

            CurrentState = BagState.OnScale;

            if (_interactPromptUI) _interactPromptUI.SetActive(false);
            _playerNearby = false;

            // 移到天秤右側
            if (_scaleRightSide)
                transform.SetParent(_scaleRightSide);
            transform.localPosition = Vector3.zero;

            // 通知天秤增加右側重量
            _scale?.AddMoneybagWeight(Weight);

            Debug.Log($"[Moneybag] 撿起，重量 {Weight:F1}，放上天秤右側。");
        }

        // ════════════════════════════════════════════════════════════
        //  IDamageable — 被玩家攻擊打落
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            // 只有在天秤上才能被打落
            if (CurrentState != BagState.OnScale) return;

            KnockOff();
        }

        // ════════════════════════════════════════════════════════════
        //  拋物線掉落
        // ════════════════════════════════════════════════════════════

        private void KnockOff()
        {
            CurrentState = BagState.OnGround;

            // 通知天秤減少右側重量
            _scale?.RemoveMoneybagWeight(Weight);

            // 脫離天秤父物件
            transform.SetParent(null);

            // 計算拋物線終點
            Vector3 randomOffset = new Vector3(
                Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
            _flyStart = transform.position;
            _flyEnd = new Vector3(
                _flyStart.x + randomOffset.x,
                _spawnY,
                _flyStart.z + randomOffset.z);

            _flyTimer = 0f;
            _isFlying = true;

            if (_interactPromptUI) _interactPromptUI.SetActive(false);
            _playerNearby = false;

            Debug.Log($"[Moneybag] 被打落，拋物線掉回地板。");
        }

        private void UpdateArc()
        {
            _flyTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_flyTimer / _arcDuration);

            Vector3 pos = Vector3.Lerp(_flyStart, _flyEnd, t);
            pos.y += _arcHeight * Mathf.Sin(Mathf.PI * t);

            transform.position = pos;

            if (t >= 1f)
                _isFlying = false;
        }

        // ── 地板 Y 值（與 MoneybagSpawner._spawnY 對應） ─────────────
        // 錢袋落地後的 Y 座標，預設 0.5
        [SerializeField] private float _spawnY = 0.5f;

        // ════════════════════════════════════════════════════════════
        //  清理
        // ════════════════════════════════════════════════════════════

        private void OnDestroy()
        {
            if (_interactPromptUI) _interactPromptUI.SetActive(false);
        }
    }
}