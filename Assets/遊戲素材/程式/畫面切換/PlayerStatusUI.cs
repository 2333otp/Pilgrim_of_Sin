using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 玩家狀態面板。
    /// 掛在 PlayerStatus_Panel 物件上，由 PauseMenuUI 呼叫 Refresh()。
    /// </summary>
    public class PlayerStatusUI : MonoBehaviour
    {
        [Header("角色立繪")]
        [SerializeField] private Image _characterIllustration;
        [SerializeField] private Sprite _characterSprite;

        [Header("HP")]
        [SerializeField] private Slider _hpBar;
        [SerializeField] private Text   _hpText;

        [Header("已克服心魔文字")]
        [SerializeField] private Text _defeatedBossesText;

        [Header("Placeholder 按鈕（點了沒反應，供視覺呈現）")]
        [SerializeField] private Button _btnBackpack;
        [SerializeField] private Button _btnWeaponInfo;
        [SerializeField] private Button _btnMemories;
        [SerializeField] private Button _btnCharData;

        private void Awake()
        {
            if (_characterIllustration != null && _characterSprite != null)
                _characterIllustration.sprite = _characterSprite;
        }

        /// <summary>顯示面板前由 PauseMenuUI 呼叫，更新所有數值。</summary>
        public void Refresh(StateMachine.PlayerController player)
        {
            RefreshHP(player);
            RefreshDefeatedBosses();
        }

        private void RefreshHP(StateMachine.PlayerController player)
        {
            if (player == null) return;

            float current = player.CurrentHp;
            float max     = player.MaxHp;

            if (_hpBar != null)
            {
                _hpBar.maxValue = max;
                _hpBar.value    = current;
            }

            if (_hpText != null)
                _hpText.text = $"Hp: {Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
        }

        private void RefreshDefeatedBosses()
        {
            if (_defeatedBossesText == null) return;
            _defeatedBossesText.text =
                GameProgressManager.Instance != null
                    ? GameProgressManager.Instance.GetDefeatedBossesText()
                    : string.Empty;
        }
    }
}
