using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 貪嗔癡三隻 Boss 全數擊敗後的結局跑馬燈名單。
    /// 掛在 CreditsScene 的場景管理物件上。
    /// 內容取自 CreditsData（與 ESC 選單「製作團隊、官方社群」共用同一份資料）。
    ///
    /// 【流程】
    ///   淡入 → 延遲 → 文字往上捲動 → 捲完停在黑幕顯示「按任意鍵繼續」
    ///   → 捲動中按任意鍵可直接跳到結尾 → 停留畫面再按任意鍵 → 淡出回主選單
    /// </summary>
    public class EndingCreditsRoll : MonoBehaviour
    {
        [Header("資料來源")]
        [SerializeField] private CreditsData _creditsData;

        [Header("元件")]
        [Tooltip("內含 _rollText 的容器，往上位移做捲動")]
        [SerializeField] private RectTransform _scrollContent;
        [SerializeField] private TextMeshProUGUI _rollText;
        [SerializeField] private CanvasGroup _fadeCanvasGroup;
        [Tooltip("捲完才顯示的「按任意鍵繼續」提示")]
        [SerializeField] private GameObject _continueHint;

        [Header("捲動設定")]
        [SerializeField] private float _scrollSpeed = 60f;   // 每秒位移像素
        [SerializeField] private float _startDelay = 1.5f;
        [SerializeField] private float _fadeDuration = 1f;

        private Coroutine _scrollCoroutine;
        private bool _scrollFinished = false;
        private bool _isTransitioning = false;
        private float _startY;
        private float _targetY;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (_continueHint != null) _continueHint.SetActive(false);
            if (_rollText != null)
                _rollText.text = _creditsData != null ? _creditsData.BuildRollText() : string.Empty;

            Canvas.ForceUpdateCanvases();
            ComputeScrollRange();

            _fadeCanvasGroup.alpha = 1f;
            StartCoroutine(FadeIn());
            _scrollCoroutine = StartCoroutine(ScrollRoutine());
        }

        private void Update()
        {
            if (_isTransitioning) return;

            bool confirmPressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                                || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame
                                                              || Gamepad.current.startButton.wasPressedThisFrame));
            if (!confirmPressed) return;

            if (!_scrollFinished)
                SkipToEnd();
            else
                Continue();
        }

        /// <summary>
        /// 起點：文字完全在畫面下方（底邊貼齊可視區底部下方）。
        /// 終點：最後一行（文字區塊底邊）捲到畫面正中間就停，不用整段捲出畫面，
        /// 停下來的同時顯示「按任意鍵繼續」。
        /// _scrollContent 需設定 anchorMin=anchorMax=pivot=(0.5, 0)，
        /// 這樣 anchoredPosition.y 才能直接代表「相對可視區底部的位移」。
        /// 兩者都在 Runtime 依實際文字長度計算，不吃 Inspector 上的初始座標，
        /// 之後改動 CreditsData 文字內容不需要重新調整位置。
        /// </summary>
        private void ComputeScrollRange()
        {
            float contentHeight = _rollText != null ? _rollText.preferredHeight : 0f;
            float viewHeight = (_scrollContent.parent as RectTransform)?.rect.height ?? 0f;
            _startY = -contentHeight;
            _targetY = viewHeight * 0.5f;
            _scrollContent.anchoredPosition = new Vector2(_scrollContent.anchoredPosition.x, _startY);
        }

        private void SkipToEnd()
        {
            if (_scrollCoroutine != null) StopCoroutine(_scrollCoroutine);
            _scrollContent.anchoredPosition = new Vector2(_scrollContent.anchoredPosition.x, _targetY);
            OnScrollComplete();
        }

        private IEnumerator ScrollRoutine()
        {
            yield return new WaitForSeconds(_startDelay);

            float duration = (_targetY - _startY) / Mathf.Max(_scrollSpeed, 1f);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // 用 smoothDeltaTime（近期幀時間的移動平均）取代 deltaTime，
                // 避免單幀時間忽長忽短造成的捲動速度忽快忽慢（機動不均勻）。
                elapsed += Time.smoothDeltaTime;
                float y = Mathf.Lerp(_startY, _targetY, Mathf.Clamp01(elapsed / duration));
                // 四捨五入到整數像素，避免 TMP SDF 文字在次像素位移時邊緣閃爍抖動
                y = Mathf.Round(y);
                _scrollContent.anchoredPosition = new Vector2(_scrollContent.anchoredPosition.x, y);
                yield return null;
            }

            OnScrollComplete();
        }

        private void OnScrollComplete()
        {
            _scrollFinished = true;
            if (_continueHint != null) _continueHint.SetActive(true);
        }

        private void Continue()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionToMainMenu());
        }

        private IEnumerator TransitionToMainMenu()
        {
            _isTransitioning = true;
            yield return StartCoroutine(FadeOut());
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneTransitionManager.MAIN_SCENE);
        }

        private IEnumerator FadeIn()
        {
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.deltaTime;
                _fadeCanvasGroup.alpha = 1f - Mathf.Clamp01(t / _fadeDuration);
                yield return null;
            }
            _fadeCanvasGroup.alpha = 0f;
        }

        private IEnumerator FadeOut()
        {
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Clamp01(t / _fadeDuration);
                yield return null;
            }
            _fadeCanvasGroup.alpha = 1f;
        }
    }
}
