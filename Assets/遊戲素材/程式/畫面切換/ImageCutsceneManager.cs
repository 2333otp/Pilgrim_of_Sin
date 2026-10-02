using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 過場靜態圖片管理器。
    /// 掛在 ImageCutsceneScene 的 GameObject 上。
    /// 依累計通關的 Boss 組合選對應圖片，
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

        [Header("依累計通關組合的過場圖（索引 = 貪1 + 嗔2 + 癡4 的位元組合）")]
        [Tooltip("0全圖未通關 1貪 2嗔 3貪嗔 4痴 5貪痴 6嗔痴 7全通關。" +
                 "失敗（玩家死亡）時也是顯示『目前進度』對應的這張，進度沒變所以畫面也不會讓人誤會。")]
        [SerializeField] private Sprite[] _comboImages = new Sprite[8];
        [SerializeField] private Sprite[] _defaultImages;  // 找不到對應時的備用圖

        [Header("時間設定")]
        [SerializeField] private float _displayDuration = 5f;  // 圖片停留秒數
        [SerializeField] private float _failDisplayDuration = 3f;  // 失敗時圖片停留秒數（比勝利短，讓玩家快點回去再挑戰）
        [SerializeField] private float _fadeDuration = 1f;     // 淡入/淡出各幾秒

        private bool _isFailure;

        private void Start()
        {
            // 這次是不是失敗收場；讀完立刻清掉，避免下一次進來沿用舊狀態
            _isFailure = SceneTransitionManager.LastBattleFailed;
            SceneTransitionManager.LastBattleFailed = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            Sprite sprite = GetComboSprite();
            if (sprite == null && _defaultImages != null && _defaultImages.Length > 0)
                sprite = _defaultImages[0];

            if (sprite != null)
                _displayImage.sprite = sprite;
            else
                Debug.LogWarning("[ImageCutscene] 沒有可用的過場圖，請在 Inspector 指派 Sprite。");

            _fadeCanvasGroup.alpha = 1f;
            StartCoroutine(PlaySequence());
        }

        private Sprite GetComboSprite()
        {
            var gp = GameProgressManager.Instance;
            if (gp == null || _comboImages == null) return null;

            int mask = (gp.IsBossDefeated(SceneTransitionManager.BossType.Greed)   ? 1 : 0)
                     | (gp.IsBossDefeated(SceneTransitionManager.BossType.Wrath)   ? 2 : 0)
                     | (gp.IsBossDefeated(SceneTransitionManager.BossType.Foolish) ? 4 : 0);
            return mask < _comboImages.Length ? _comboImages[mask] : null;
        }

        private IEnumerator PlaySequence()
        {
            yield return StartCoroutine(Fade(1f, 0f));       // 淡入（黑→圖）
            yield return new WaitForSeconds(_isFailure ? _failDisplayDuration : _displayDuration);
            yield return StartCoroutine(Fade(0f, 1f));       // 淡出（圖→黑）

            // 貪嗔癡三隻 Boss 皆已擊敗 → 接結局跑馬燈名單，否則照舊回小木屋。
            // 失敗收場一律回小木屋（已通關後回來重打又輸了，不該再看一次結局名單）。
            bool allDefeated = !_isFailure
                                && GameProgressManager.Instance != null
                                && GameProgressManager.Instance.AllBossesDefeated;
            string nextScene = allDefeated
                ? SceneTransitionManager.CREDITS_SCENE
                : SceneTransitionManager.HUB_SCENE;
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextScene);
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
