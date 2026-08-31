using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 通用戰鬥 HUD：玩家血條、Boss 血條、武器欄、鎖定準心。
    ///
    /// · 武器欄 / 鎖定準心沿用同物件上的 WeaponHUDUI / LockOnMarkerUI（各自運作，這裡不管）。
    /// · 玩家血量讀場景中的 PlayerController。
    /// · Boss 血量讀場景中實作 IBossHealth 的物件；各 Boss 場景的專屬 HUD 也可用 BindBoss() 指定。
    /// 各 Boss 場景以此為通用底，再疊上場景專屬 HUD（例：貪的 GreedBattleHUD）。
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        [Header("玩家血條")]
        [SerializeField] private Image _playerHpFill;          // Image.type = Filled (Horizontal)
        [SerializeField] private TMP_Text _playerNameText;
        [SerializeField] private string _playerName = "繆爾";

        [Header("Boss 血條")]
        [SerializeField] private Image _bossHpFill;            // Image.type = Filled (Horizontal)
        [SerializeField] private TMP_Text _bossNameText;
        [SerializeField] private GameObject _bossShieldOverlay; // 護盾外觀（非平衡時顯示；美術待補，先程式佔位）
        [SerializeField] private Color _bossFillNormal = new Color(0.78f, 0.28f, 0.28f, 1f);
        [SerializeField] private Color _bossFillShielded = new Color(0.40f, 0.36f, 0.38f, 1f);

        [Header("填充動畫")]
        [SerializeField] private float _fillLerpSpeed = 1.5f;   // 每秒補間比例（0~1 空間）

        private StateMachine.PlayerController _player;
        private StateMachine.IBossHealth _boss;
        private float _playerTarget = 1f;
        private float _bossTarget = 1f;

        private void Start()
        {
            _player = FindFirstObjectByType<StateMachine.PlayerController>();
            if (_boss == null) _boss = FindBoss();

            if (_playerNameText != null) _playerNameText.text = _playerName;
            if (_bossNameText != null && _boss != null) _bossNameText.text = _boss.DisplayName;

            if (_playerHpFill != null) _playerHpFill.fillAmount = 1f;
            if (_bossHpFill != null) _bossHpFill.fillAmount = 1f;
            SetBossShielded(false);
        }

        private StateMachine.IBossHealth FindBoss()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is StateMachine.IBossHealth b) return b;
            return null;
        }

        /// <summary>場景專屬 HUD 可直接指定要顯示的 Boss。</summary>
        public void BindBoss(StateMachine.IBossHealth boss)
        {
            _boss = boss;
            if (_bossNameText != null && boss != null) _bossNameText.text = boss.DisplayName;
        }

        /// <summary>切換 Boss 血條「受保護 / 易傷」外觀。</summary>
        public void SetBossShielded(bool shielded)
        {
            if (_bossShieldOverlay != null) _bossShieldOverlay.SetActive(shielded);
            if (_bossHpFill != null) _bossHpFill.color = shielded ? _bossFillShielded : _bossFillNormal;
        }

        private void Update()
        {
            if (_player != null && _player.MaxHp > 0f)
                _playerTarget = Mathf.Clamp01(_player.CurrentHp / _player.MaxHp);
            if (_boss != null && _boss.MaxHp > 0f)
                _bossTarget = Mathf.Clamp01(_boss.CurrentHp / _boss.MaxHp);

            float step = _fillLerpSpeed * Time.deltaTime;
            if (_playerHpFill != null)
                _playerHpFill.fillAmount = Mathf.MoveTowards(_playerHpFill.fillAmount, _playerTarget, step);
            if (_bossHpFill != null)
                _bossHpFill.fillAmount = Mathf.MoveTowards(_bossHpFill.fillAmount, _bossTarget, step);
        }
    }
}
