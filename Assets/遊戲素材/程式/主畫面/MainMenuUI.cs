using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Button _startGameButton;

        private void Start()
        {
            // 移除舊的 Inspector 綁定（避免重複呼叫）
            _startGameButton.onClick.RemoveAllListeners();

            // 動態綁定：透過 singleton 取得，不依賴 Inspector 跨場景引用
            _startGameButton.onClick.AddListener(() =>
            {
                SceneTransitionManager.Instance.LoadHubScene();
            });
        }
    }
}