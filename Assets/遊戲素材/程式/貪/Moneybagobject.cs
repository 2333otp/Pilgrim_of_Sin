using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
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
        [SerializeField] private float _knockoffMinDist = 4f;
        [SerializeField] private float _knockoffMaxDist = 9f;

        [SerializeField] private Transform _scaleCenter;      // 拖入 Scale 物件
        [SerializeField] private float _scaleExcludeRadius = 5f;

        [SerializeField] private float _minBagSpacing = 3f;

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
        private float _spawnY;          // 由 Spawner 傳入，不再是 SerializeField
        private int _slotIndex;       // 天秤右側排列用

        // ════════════════════════════════════════════════════════════
        //  初始化（由 MoneybagSpawner 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 初始化錢袋。新增 spawnY 與 slotIndex 參數。
        /// slotIndex 用於讓多顆錢袋在天秤右側排開，不重疊。
        /// </summary>
        public void Init(float weight, ScaleObject scale, Transform scaleRightSide,
                         GameObject interactPromptUI, float spawnY, int slotIndex = 0,
                         Transform scaleCenter = null)
        {
            Weight = weight;
            _scale = scale;
            _scaleRightSide = scaleRightSide;
            _interactPromptUI = interactPromptUI;
            _spawnY = spawnY;
            _slotIndex = slotIndex;
            _scaleCenter = scaleCenter;
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
            bool interactPressed = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                                || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
            if (interactPressed)
                PickUp();
        }

        private void PickUp()
        {
            if (CurrentState != BagState.OnGround) return;

            CurrentState = BagState.OnScale;

            if (_interactPromptUI) _interactPromptUI.SetActive(false);
            _playerNearby = false;

            // 移到天秤右側，依 slotIndex 橫向排開（每顆間距 0.4）
            if (_scaleRightSide)
                transform.SetParent(_scaleRightSide);

            float offset = (_slotIndex - 2) * 0.4f; // 以 0 為中心左右排列
            transform.localPosition = new Vector3(offset, 0f, 0f);

            _scale?.AddMoneybagWeight(Weight);
        }

        // ════════════════════════════════════════════════════════════
        //  IDamageable — 被玩家攻擊打落
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            if (CurrentState != BagState.OnScale) return;
            KnockOff();
        }

        // ════════════════════════════════════════════════════════════
        //  拋物線掉落
        // ════════════════════════════════════════════════════════════

        private void KnockOff()
        {
            CurrentState = BagState.OnGround;
            _scale?.RemoveMoneybagWeight(Weight);
            transform.SetParent(null, true);

            _flyStart = transform.position;

            Vector3 candidate;
            int safety = 50;
            do
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(_knockoffMinDist, _knockoffMaxDist);
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                candidate = new Vector3(_flyStart.x + dir.x * dist, _spawnY, _flyStart.z + dir.z * dist);
                safety--;
            }
            while (safety > 0 && (
            (_scaleCenter != null &&
             Vector2.Distance(new Vector2(candidate.x, candidate.z),
                              new Vector2(_scaleCenter.position.x, _scaleCenter.position.z))
             < _scaleExcludeRadius)
             ||
             IsNearOtherBag(candidate)
             ));

            if (safety <= 0 && _scaleCenter != null)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                candidate = new Vector3(
                    _scaleCenter.position.x + Mathf.Cos(angle) * (_knockoffMaxDist + 3f),
                    _spawnY,
                    _scaleCenter.position.z + Mathf.Sin(angle) * (_knockoffMaxDist + 3f));
            }
            _flyEnd = candidate;

            _flyTimer = 0f;
            _isFlying = true;

            if (_interactPromptUI) _interactPromptUI.SetActive(false);
            _playerNearby = false;
        }

        private bool IsNearOtherBag(Vector3 candidate)
        {
            var allBags = FindObjectsByType<MoneybagObject>(FindObjectsSortMode.None);
            foreach (var bag in allBags)
            {
                if (bag == this) continue;
                if (bag.CurrentState != BagState.OnGround) continue;
                if (Vector2.Distance(new Vector2(candidate.x, candidate.z),
                                     new Vector2(bag.transform.position.x, bag.transform.position.z))
                    < _minBagSpacing)
                    return true;
            }
            return false;
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

        // ════════════════════════════════════════════════════════════
        //  清理
        // ════════════════════════════════════════════════════════════

        private void OnDestroy()
        {
            if (_interactPromptUI) _interactPromptUI.SetActive(false);
        }
    }
}