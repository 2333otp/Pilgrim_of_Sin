using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 過場靜態圖片管理器。
    /// 掛在 ImageCutsceneScene 的 GameObject 上。
    /// 根據 SceneTransitionManager.LastBossType 隨機選一張對應圖片，
    /// 淡入顯示 → 等待 → 淡出 → 回小木屋。
    ///
    /// 【Inspector 設定步驟】
    ///   1. Canvas 下建一個全螢幕黑色 Image（作為背景）
    ///   2. 背景上層建一個 Image（_displayImage）用來顯示過場圖
    ///   3. 最頂層建一個全螢幕黑色 Image，掛 CanvasGroup（_fadeCanvasGroup），
    ///      勾掉 Image 的 Raycast Target，Alpha 初始設 1
    ///   4. 將各 Boss 對應的 Sprite（需先在 Project 設定為 Sprite 類型）拖入陣列
    /// </summary>
    public class ImageCutsceneManager : MonoBehaviour
    {
        [Header("顯示元件")]
        [SerializeField] private Image _displayImage;
        [SerializeField] private CanvasGroup _fadeCanvasGroup;

        [Header("各 Boss 過場圖（可指派多張，隨機選一）")]
        [SerializeField] private Sprite[] _greedImages;    // 貪：建議 過場貪、過場全
        [SerializeField] private Sprite[] _wrathImages;    // 嗔：建議 過場嗔、過場全
        [SerializeField] private Sprite[] _foolishImages;  // 癡：建議 過場癡、過場全
        [SerializeField] private Sprite[] _defaultImages;  // 找不到對應時的備用圖

        [Header("時間設定")]
        [SerializeField] private float _displayDuration = 5f;  // 圖片停留秒數
        [SerializeField] private float _fadeDuration = 1f;     // 淡入/淡出各幾秒

        private void Start()
        {
            Sprite[] pool = SceneTransitionManager.LastBossType switch
            {
                SceneTransitionManager.BossType.Greed   => _greedImages,
                SceneTransitionManager.BossType.Wrath   => _wrathImages,
                SceneTransitionManager.BossType.Foolish => _foolishImages,
                _                                        => _defaultImages,
            };

            if (pool == null || pool.Length == 0)
                pool = _defaultImages;

            if (pool != null && pool.Length > 0)
                _displayImage.sprite = pool[Random.Range(0, pool.Length)];
            else
                Debug.LogWarning("[ImageCutscene] 沒有可用的過場圖，請在 Inspector 指派 Sprite。");

            _fadeCanvasGroup.alpha = 1f;
            StartCoroutine(PlaySequence());
        }

        private IEnumerator PlaySequence()
        {
            yield return StartCoroutine(Fade(1f, 0f));       // 淡入（黑→圖）
            yield return new WaitForSeconds(_displayDuration);
            yield return StartCoroutine(Fade(0f, 1f));       // 淡出（圖→黑）
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneTransitionManager.HUB_SCENE);
        }

        private IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / _fadeDuration));
                yield return null;
            }
            _fadeCanvasGroup.alpha = to;
        }
    }
}
