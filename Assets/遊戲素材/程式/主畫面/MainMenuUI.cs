using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Button _startGameButton;   // 開啟新遊戲
        [SerializeField] private Button _continueButton;    // 繼續進度
        [SerializeField] private Button _quitButton;         // 結束遊戲

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            // 移除舊的 Inspector 綁定（避免重複呼叫）
            _startGameButton.onClick.RemoveAllListeners();

            // 動態綁定：透過 singleton 取得，不依賴 Inspector 跨場景引用
            _startGameButton.onClick.AddListener(() =>
            {
                SceneTransitionManager.Instance.LoadCutscene();
            });

            bool hasSave = GameProgressManager.SaveFileExists();
            _continueButton.gameObject.SetActive(hasSave);
            if (hasSave)
            {
                _continueButton.onClick.RemoveAllListeners();
                _continueButton.onClick.AddListener(() =>
                {
                    SceneTransitionManager.Instance.LoadHubScene();
                });
            }

            _quitButton.onClick.RemoveAllListeners();
            _quitButton.onClick.AddListener(QuitGame);
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
