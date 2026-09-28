using System.Collections.Generic;
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

        /// <summary>互動範圍，供 Spawner 換算最小間距用，避免兩顆錢袋的互動範圍互相重疊。</summary>
        public float InteractRadius => _interactRadius;

        // 場景內所有錢袋共用同一個提示 UI（唯一一個），見 CheckPlayerProximity() 的註解。
        private static readonly List<MoneybagObject> _allBags = new List<MoneybagObject>();

        private void OnEnable() => _allBags.Add(this);
        private void OnDisable() => _allBags.Remove(this);

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

                // 場景內所有錢袋共用同一個提示 UI（唯一一個），每顆錢袋各自獨立開關。
                // 兩顆錢袋的偵測範圍如果重疊，玩家離開 A 的範圍、卻還站在 B 腳邊時，
                // A 的 SetActive(false) 有機率在同一影格晚於 B 的 SetActive(true) 執行，
                // 直接把提示關掉——即使玩家明明還站在 B 旁邊。改成「除非全部錢袋都確認
                // 沒人在旁邊，否則不要關」，開的時候不受影響（只要有一顆近就該開）。
                if (_playerNearby || !AnyBagNearby())
                    if (_interactPromptUI) _interactPromptUI.SetActive(_playerNearby);
            }
        }

        private static bool AnyBagNearby()
        {
            foreach (var bag in _allBags)
                if (bag != null && bag._playerNearby) return true;
            return false;
        }

        // ════════════════════════════════════════════════════════════
        //  按 X 撿起
        // ════════════════════════════════════════════════════════════

        // 錢袋間距如果太小，會有兩顆錢袋的互動範圍重疊，玩家同時站在兩顆的範圍內，
        // 按一次互動鍵時每顆錢袋各自獨立判定，導致一次動作卻撿到兩顆。這裡用「這一幀
        // 是否已經有錢袋被撿走」擋掉同一次按鍵重複觸發，不管間距多近都保證一次只撿一顆。
        private static int _lastPickupFrame = -1;

        private void CheckPickupInput()
        {
            // Time.timeScale == 0 代表 ESC 選單開著（PauseMenuUI 暫停時的作法），
            // 這時候互動鍵（R2）要留給選單的返回操作用，不能同時撿起錢袋。
            if (!_playerNearby || Time.timeScale == 0f) return;
            if (Time.frameCount == _lastPickupFrame) return;
            bool interactPressed = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                                || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
            if (interactPressed)
                PickUp();
        }

        private void PickUp()
        {
            if (CurrentState != BagState.OnGround) return;

            _lastPickupFrame = Time.frameCount;
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
            // 間距至少要大於兩顆錢袋互動範圍的總和，否則兩顆的偵測圈會重疊：
            // 玩家同時站在兩顆範圍內，提示 UI 互搶、按一次互動鍵可能兩顆一起被撿走。
            float safeSpacing = Mathf.Max(_minBagSpacing, _interactRadius * 2f + 1f);

            var allBags = FindObjectsByType<MoneybagObject>(FindObjectsSortMode.None);
            foreach (var bag in allBags)
            {
                if (bag == this) continue;
                if (bag.CurrentState != BagState.OnGround) continue;
                if (Vector2.Distance(new Vector2(candidate.x, candidate.z),
                                     new Vector2(bag.transform.position.x, bag.transform.position.z))
                    < safeSpacing)
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