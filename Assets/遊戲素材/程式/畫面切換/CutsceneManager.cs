using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using System.Collections;

namespace PilgrimOfSin
{
    public class CutsceneManager : MonoBehaviour
    {
        [Header("元件")]
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private CanvasGroup _fadeCanvasGroup;

        [Header("設定")]
        [SerializeField] private string _nextSceneName = "HubScene";
        [SerializeField] private float _fadeDuration = 1f;

        private bool _isTransitioning = false;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            _fadeCanvasGroup.alpha = 1f;
            _videoPlayer.loopPointReached += _ => Skip();
            StartCoroutine(StartVideoThenFadeIn());
        }

        /// <summary>
        /// 開頭／結尾動畫共用同一張 RenderTexture，裡面會殘留「上一次播放的最後一格」。
        /// 如果一載入就淡入，影片第一格還沒畫出來前會先露出那張舊畫面，看起來像閃過一張圖。
        /// 所以：先把畫布清成全黑 → 等影片準備好、第一格真的畫進畫布 → 才開始淡入。
        /// </summary>
        private IEnumerator StartVideoThenFadeIn()
        {
            ClearTargetTexture();

            _videoPlayer.Prepare();
            float waited = 0f;
            while (!_videoPlayer.isPrepared && waited < 5f) // 保險：最多等 5 秒，避免影片壞掉時卡在黑畫面
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            _videoPlayer.Play();

            waited = 0f;
            while (_videoPlayer.frame < 1 && waited < 2f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!_isTransitioning)
                yield return StartCoroutine(FadeIn());
        }

        private void ClearTargetTexture()
        {
            var rt = _videoPlayer.targetTexture;
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }

        private void Update()
        {
            if (_isTransitioning) return;

            bool skipPressed = (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame ||
                                                              Keyboard.current.backspaceKey.wasPressedThisFrame))
                            || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (skipPressed)
            {
                Skip();
            }
        }

        private void Skip()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionToHub());
        }

        private IEnumerator TransitionToHub()
        {
            _isTransitioning = true;
            _videoPlayer.Pause();
            yield return StartCoroutine(FadeOut()); // CutsceneScene 自己淡出
                                                    // 畫面已全黑，直接載入，SceneTransitionManager 會處理 HubScene 的淡入
            UnityEngine.SceneManagement.SceneManager.LoadScene(_nextSceneName);
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