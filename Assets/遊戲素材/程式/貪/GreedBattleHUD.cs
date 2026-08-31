using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 貪關卡專屬 HUD：天秤狀態圖示 + 左右提示橫幅。
    /// 掛在 GameplayCanvas，讀 GreedBossController / ScaleObject 的相位，並驅動通用 CombatHUD 的護盾外觀。
    /// </summary>
    public class GreedBattleHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CombatHUD _combatHUD;
        [SerializeField] private StateMachine.GreedBossController _boss;
        [SerializeField] private StateMachine.ScaleObject _scale;

        [Header("天秤圖示（頂中）")]
        [SerializeField] private Image _scaleIcon;
        [SerializeField] private Sprite _spriteBalanced;
        [SerializeField] private Sprite _spriteTiltLeft;
        [SerializeField] private Sprite _spriteTiltRight;

        [Header("左側橫幅（狀態提示，彈出→停幾秒→收回）")]
        [SerializeField] private RectTransform _leftBanner;
        [SerializeField] private TMP_Text _leftBannerText;
        [SerializeField] private float _leftShownX = 0f;
        [SerializeField] private float _leftHiddenX = -760f;

        [Header("右側提示（互動，隨條件顯示）")]
        [SerializeField] private RectTransform _rightPrompt;
        [SerializeField] private TMP_Text _rightPromptText;
        [SerializeField] private float _rightShownX = 0f;
        [SerializeField] private float _rightHiddenX = 760f;

        [Header("動畫")]
        [SerializeField] private float _bannerHoldSeconds = 3f;
        [SerializeField] private float _slideSeconds = 0.3f;

        [Header("靠近錢袋提示旗標（MoneybagObject 控制的 InteractPrompt）")]
        [SerializeField] private GameObject _moneybagNearFlag;

        // 「按 ___ 鍵」的按鍵字之後團隊定案再填
        private const string AttackMsg = "按 ___ 鍵攻擊";
        private const string PickupMsg = "按 ___ 鍵撿取錢袋";

        private StateMachine.ScalePhase _lastPhase;
        private bool _seenFirstPhase;
        private Coroutine _leftRoutine;
        private Coroutine _rightRoutine;
        private bool _rightVisible;

        private void Start()
        {
            if (_boss == null) _boss = FindFirstObjectByType<StateMachine.GreedBossController>();
            if (_scale == null) _scale = FindFirstObjectByType<StateMachine.ScaleObject>();
            if (_combatHUD == null) _combatHUD = FindFirstObjectByType<CombatHUD>();
            if (_boss != null) _combatHUD?.BindBoss(_boss);

            if (_leftBanner != null) SetX(_leftBanner, _leftHiddenX);
            if (_rightPrompt != null) SetX(_rightPrompt, _rightHiddenX);

            _lastPhase = _boss != null ? _boss.CurrentPhase : StateMachine.ScalePhase.StatueHeavy;
        }

        private void Update()
        {
            if (_boss == null) return;
            var phase = _boss.CurrentPhase;

            UpdateScaleIcon();
            _combatHUD?.SetBossShielded(phase != StateMachine.ScalePhase.Balanced);

            if (!_seenFirstPhase)
            {
                _seenFirstPhase = true;
                if (phase != StateMachine.ScalePhase.Balanced && phase != StateMachine.ScalePhase.Kicked)
                    ShowLeftBanner("天秤傾斜了！收集錢袋使天秤平衡！");
            }
            else if (phase != _lastPhase)
            {
                if (phase == StateMachine.ScalePhase.Balanced)
                    ShowLeftBanner("趁現在！攻擊心魔的最佳時刻！");
                else if (_lastPhase == StateMachine.ScalePhase.Balanced && phase != StateMachine.ScalePhase.Kicked)
                    ShowLeftBanner("天秤傾斜了！收集錢袋使天秤平衡！");
            }
            _lastPhase = phase;

            UpdateRightPrompt();
        }

        // ── 天秤圖示 ─────────────────────────────────────────────────

        private void UpdateScaleIcon()
        {
            if (_scaleIcon == null || _scale == null) return;
            Sprite s = _scale.CurrentViewerTilt switch
            {
                StateMachine.ScaleObject.ViewerTilt.Balanced  => _spriteBalanced,
                StateMachine.ScaleObject.ViewerTilt.TiltLeft   => _spriteTiltLeft,
                StateMachine.ScaleObject.ViewerTilt.TiltRight  => _spriteTiltRight,
                _ => null,
            };
            if (s != null) _scaleIcon.sprite = s;
        }

        // ── 左側橫幅 ─────────────────────────────────────────────────

        private void ShowLeftBanner(string msg)
        {
            if (_leftBanner == null) return;
            if (_leftBannerText != null) _leftBannerText.text = msg;
            if (_leftRoutine != null) StopCoroutine(_leftRoutine);
            _leftRoutine = StartCoroutine(LeftBannerRoutine());
        }

        private IEnumerator LeftBannerRoutine()
        {
            yield return Slide(_leftBanner, _leftBanner.anchoredPosition.x, _leftShownX);
            yield return new WaitForSeconds(_bannerHoldSeconds);
            yield return Slide(_leftBanner, _leftBanner.anchoredPosition.x, _leftHiddenX);
        }

        // ── 右側提示 ─────────────────────────────────────────────────

        private void UpdateRightPrompt()
        {
            if (_rightPrompt == null) return;
            bool nearBag = _moneybagNearFlag != null && _moneybagNearFlag.activeInHierarchy;
            bool inWindow = _boss != null && _boss.IsInBalanceWindow;

            string msg = nearBag ? PickupMsg : (inWindow ? AttackMsg : null);
            bool shouldShow = msg != null;
            if (shouldShow && _rightPromptText != null) _rightPromptText.text = msg;

            if (shouldShow != _rightVisible)
            {
                _rightVisible = shouldShow;
                if (_rightRoutine != null) StopCoroutine(_rightRoutine);
                _rightRoutine = StartCoroutine(Slide(_rightPrompt, _rightPrompt.anchoredPosition.x,
                                                     shouldShow ? _rightShownX : _rightHiddenX));
            }
        }

        // ── 共用 ─────────────────────────────────────────────────────

        private IEnumerator Slide(RectTransform rt, float from, float to)
        {
            float t = 0f;
            while (t < _slideSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _slideSeconds));
                SetX(rt, Mathf.Lerp(from, to, k));
                yield return null;
            }
            SetX(rt, to);
        }

        private static void SetX(RectTransform rt, float x)
        {
            var p = rt.anchoredPosition;
            p.x = x;
            rt.anchoredPosition = p;
        }
    }
}
