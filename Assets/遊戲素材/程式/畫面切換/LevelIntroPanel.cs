using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 進關前的機制說明圖面板（Hub 選關確認後、載入關卡前）。
    /// 顯示一張全螢幕說明圖，玩家按 手把 Options(Start) / ESC / Backspace 就跳過並呼叫 onSkip（真正進關）。
    ///
    /// 【結構】根物件常駐啟用，只有 _content 子物件會被開關（不要在 Awake 裡把自己關掉，
    ///   否則第一次 Show 時協程會跑在失效物件上）。圖上的按鍵圖示是 _content 底下的 InputPromptUI，
    ///   隨裝置與按鍵綁定自動換圖，不燒進 PNG。
    /// 【輸入】跳過鍵的判斷跟 CutsceneManager 一致（直讀裝置，Hub 不一定有 PlayerInput 可用）。
    /// </summary>
    public class LevelIntroPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _content;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _image;
        [Tooltip("全螢幕黑色 Image（放在 Content 最上層）。按下跳過後淡成全黑，給玩家立即的回饋；" +
                 "場景切換管理器自己的淡出畫布排序在最底層，會被這張說明圖蓋住看不到。")]
        [SerializeField] private Image _fadeOverlay;

        [Header("時間")]
        [SerializeField] private float _fadeInDuration = 0.35f;
        [SerializeField] private float _skipFadeDuration = 0.3f;
        [Tooltip("顯示後這段時間內不接受跳過，避免確認選關的那一下連按直接跳過")]
        [SerializeField] private float _inputDelay = 0.5f;

        private Action _onSkip;
        private float _shownAt;

        /// <summary>說明圖顯示中（含淡入）。Hub 用它來停掉自己的暫停選單與選關輸入。</summary>
        public bool IsShowing { get; private set; }

        public void Show(Sprite sprite, Action onSkip)
        {
            _onSkip = onSkip;
            _shownAt = Time.unscaledTime;
            IsShowing = true;

            _image.sprite = sprite;
            SetOverlayAlpha(0f);
            _canvasGroup.alpha = 0f;
            _content.SetActive(true);
            StartCoroutine(FadeIn());
        }

        private IEnumerator FadeIn()
        {
            float t = 0f;
            while (t < _fadeInDuration)
            {
                t += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(t / _fadeInDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;
        }

        private void Update()
        {
            if (!IsShowing) return;
            if (Time.unscaledTime - _shownAt < _inputDelay) return;

            bool skipPressed = (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame ||
                                                              Keyboard.current.backspaceKey.wasPressedThisFrame))
                            || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (!skipPressed) return;

            // IsShowing 不關：交給呼叫端載入場景，載入期間 Hub 仍然要忽略輸入。
            var callback = _onSkip;
            _onSkip = null;
            StartCoroutine(FadeToBlack());
            callback?.Invoke();
        }

        private IEnumerator FadeToBlack()
        {
            float t = 0f;
            while (t < _skipFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                SetOverlayAlpha(Mathf.Clamp01(t / _skipFadeDuration));
                yield return null;
            }
            SetOverlayAlpha(1f);
        }

        private void SetOverlayAlpha(float a)
        {
            if (_fadeOverlay == null) return;
            var c = _fadeOverlay.color;
            c.a = a;
            _fadeOverlay.color = c;
        }
    }
}
