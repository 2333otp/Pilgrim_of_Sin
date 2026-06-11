using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 武器 HUD。
    /// 掛在各 Boss 場景的 WeaponHUD Canvas 上。
    /// 自動找到場景中的 PlayerController 並訂閱 OnWeaponSwitched 事件。
    /// </summary>
    public class WeaponHUDUI : MonoBehaviour
    {
        [Header("武器圖標（順序：鉛筆/畫筆/調色刀/調色盤）")]
        [SerializeField] private Image[] _weaponImages = new Image[4];

        [Header("一般狀態 Sprites（未選中）")]
        [SerializeField] private Sprite[] _normalSprites = new Sprite[4];

        [Header("選擇狀態 Sprites（已選中）")]
        [SerializeField] private Sprite[] _selectedSprites = new Sprite[4];

        private StateMachine.PlayerController _player;

        private void Start()
        {
            _player = FindFirstObjectByType<StateMachine.PlayerController>();
            if (_player == null) return;

            _player.OnWeaponSwitched += Refresh;
            Refresh(_player.Combat?.CurrentWeaponIndex ?? 1);
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.OnWeaponSwitched -= Refresh;
        }

        private void Refresh(int weaponIndex)
        {
            for (int i = 0; i < _weaponImages.Length; i++)
            {
                if (_weaponImages[i] == null) continue;
                bool isSelected = (i + 1 == weaponIndex);
                _weaponImages[i].sprite = isSelected ? _selectedSprites[i] : _normalSprites[i];
            }
        }
    }
}
