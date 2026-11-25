using TMPro;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 純文字選單項目（沒有木板底圖）的「選中記號」：選中時在文字左邊顯示 ◆。
    /// 文字顏色的變化由 Button.colors（normal / highlighted）＋ targetGraphic 指向文字負責，
    /// 這個元件只管記號；由 PauseMenuUI.ApplyButtonColor / MainMenuSettingsPanel.ApplyColor 呼叫。
    ///
    /// 背景：老師反映 ESC 選單木板上的白字看不清楚（2026-11-24），
    /// 改成拿掉木板、亮底用深咖啡字、暗底用米白字；拿掉木板後要另外標示「選到哪一顆」，
    /// 只靠字變色在深咖啡↔深紅之間太接近，所以加這個記號。
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuItemHighlight : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [Tooltip("選中時顯示的記號（◆）。")]
        [SerializeField] private RectTransform _marker;
        [Tooltip("記號跟文字左緣的距離（像素）。")]
        [SerializeField] private float _gap = 22f;

        private void Awake() => SetHighlighted(false);

        private void OnDisable() => SetHighlighted(false);

        public void SetHighlighted(bool on)
        {
            if (_marker == null) return;
            if (on && _label != null)
            {
                // 文字置中、長短不一：記號跟著這一行文字的實際寬度放在左邊
                float half = _label.preferredWidth * 0.5f;
                var pos = _marker.anchoredPosition;
                pos.x = -(half + _gap + _marker.rect.width * 0.5f);
                _marker.anchoredPosition = pos;
            }
            _marker.gameObject.SetActive(on);
        }
    }
}
