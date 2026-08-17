using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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

            // 手把/鍵盤導航需要一個初始選取目標才能上下移動，
            // 沒有預先選取的話玩家按方向鍵完全沒反應。
            // 有存檔優先選「繼續進度」，沒有就選「開啟新遊戲」。
            Button initialSelection = hasSave ? _continueButton : _startGameButton;
            EventSystem.current?.SetSelectedGameObject(initialSelection.gameObject);
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
