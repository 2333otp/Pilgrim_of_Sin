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
            StartCoroutine(FadeIn());

            _videoPlayer.loopPointReached += _ => Skip();
            _videoPlayer.Play();
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