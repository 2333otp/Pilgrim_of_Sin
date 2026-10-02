using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 通關通知（Boss 擊敗瞬間顯示的金色印記提示，如「貪 • 心魔克服」）。
    /// 掛在 Boss 場景 Canvas 底下，由 BossResultPortal 於 OnBossDefeated() 時觸發播放。
    /// 三個 Boss 場景共用同一份 Prefab，只有顯示的文字不同（依 BossType 自動判斷）。
    /// </summary>
    public class LevelClearNotification : MonoBehaviour
    {
        [Header("元件參照")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TextMeshProUGUI _text;

        [Header("時間設定（秒，使用 unscaled time）")]
        [SerializeField] private float _fadeInDuration = 0.6f;
        [SerializeField] private float _holdDuration = 1.8f;
        [SerializeField] private float _fadeOutDuration = 0.8f;

        [Header("失敗提示（玩家死亡時，文字改用這個顏色）")]
        [SerializeField] private Color _failureTextColor = new Color(0.72f, 0.74f, 0.80f, 1f);   // 偏冷的灰銀色

        [Header("文字縮放動畫（淡入時由大縮小到正常）")]
        [SerializeField] private float _startScale = 1.15f;
        [SerializeField] private float _endScale = 1f;

        private Coroutine _playRoutine;
        private Color _defaultTextColor = Color.white;

        private void Awake()
        {
            if (_text != null) _defaultTextColor = _text.color;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        /// <summary>依 Boss 類型顯示對應通關文字，播放完畢後呼叫 onComplete。</summary>
        public void Show(SceneTransitionManager.BossType bossType, Action onComplete = null)
        {
            Show(GetClearText(bossType), onComplete);
        }

        /// <summary>直接指定文字內容顯示，播放完畢後呼叫 onComplete。</summary>
        public void Show(string message, Action onComplete = null)
        {
            ShowInternal(message, _defaultTextColor, onComplete);
        }

        /// <summary>玩家死亡時的失敗提示（如「貪 • 心魔未克服」），文字用 _failureTextColor。</summary>
        public void ShowFailure(SceneTransitionManager.BossType bossType, Action onComplete = null)
        {
            ShowInternal(GetFailureText(bossType), _failureTextColor, onComplete);
        }

        private void ShowInternal(string message, Color textColor, Action onComplete)
        {
            if (_playRoutine != null) StopCoroutine(_playRoutine);

            if (_text != null)
            {
                _text.text = message;
                _text.color = textColor;
            }
            gameObject.SetActive(true);
            _playRoutine = StartCoroutine(PlayRoutine(onComplete));
        }

        private static string GetClearText(SceneTransitionManager.BossType bossType) => bossType switch
        {
            SceneTransitionManager.BossType.Greed => "貪 • 心魔克服",
            SceneTransitionManager.BossType.Wrath => "嗔 • 心魔克服",
            SceneTransitionManager.BossType.Foolish => "癡 • 心魔克服",
            _ => string.Empty,
        };

        private static string GetFailureText(SceneTransitionManager.BossType bossType) => bossType switch
        {
            SceneTransitionManager.BossType.Greed => "貪 • 心魔未克服",
            SceneTransitionManager.BossType.Wrath => "嗔 • 心魔未克服",
            SceneTransitionManager.BossType.Foolish => "癡 • 心魔未克服",
            _ => string.Empty,
        };

        private IEnumerator PlayRoutine(Action onComplete)
        {
            RectTransform textTransform = _text != null ? _text.rectTransform : null;

            float elapsed = 0f;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeInDuration);
                if (_canvasGroup != null) _canvasGroup.alpha = t;
                if (textTransform != null)
                    textTransform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, t);
                yield return null;
            }
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            if (textTransform != null) textTransform.localScale = Vector3.one * _endScale;

            yield return new WaitForSecondsRealtime(_holdDuration);

            elapsed = 0f;
            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (_canvasGroup != null) _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / _fadeOutDuration);
                yield return null;
            }
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
            _playRoutine = null;

            onComplete?.Invoke();
        }
    }
}
