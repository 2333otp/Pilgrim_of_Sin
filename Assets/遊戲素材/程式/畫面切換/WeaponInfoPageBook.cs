using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PilgrimOfSin.StateMachine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「武器介紹」兩頁書。玩家狀態面板的分頁內容之一，由 PauseMenuUI 開關。
    ///   第 1 頁（武器一覽）：左欄四把武器清單、中間原畫、右欄名稱與介紹。
    ///   第 2 頁（出招表）　：選中那把武器的出招表（MoveListTableUI）＋動作展示。
    /// 操作（手把）：第 1 頁上下換武器（頭尾相接）；第 2 頁直接按 □ △ R1 試招（MoveTrialController）；左右 = 翻頁。
    /// 返回由 PauseMenuUI 統一處理（分頁開著時它不吃上下鍵，交給這裡）。
    /// 版面由 Editor 選單「PilgrimOfSin/UI/重建武器介紹版面」產生，數值以 1920×1080 為準。
    /// 武器順序固定跟各 Boss 場景下方武器欄由左到右一致：鉛筆 / 畫筆 / 畫刀 / 調色盤。
    /// </summary>
    public class WeaponInfoPageBook : MonoBehaviour
    {
        [System.Serializable]
        public class WeaponPage
        {
            public Sprite art;
            public string weaponName;
            [Tooltip("清單上顯示的短名稱（例：鉛筆）")]
            public string shortName;
            [TextArea(2, 5)]
            [Tooltip("介紹文字。請照語意手動換行（Enter），避免系統自動斷行留下一兩個字的孤行。")]
            public string description;
            [Tooltip("勾選：原畫以兩把交叉呈現（比照武器欄的調色刀圖示）。取消：單張。")]
            public bool crossed = true;
            [Tooltip("非交叉時單張原畫的旋轉角度（度），比照武器欄圖示的斜放")]
            public float singleRotation = 0f;
            [Tooltip("這把武器的動作集（出招表與動作展示從這裡讀）")]
            public WeaponActionSet actionSet;
        }

        [System.Serializable]
        public class WeaponRow
        {
            public Button button;
            [Tooltip("選中時左濃右淡的底色帶")] public Image band;
            [Tooltip("選中時左側的金色直線")] public Image bar;
            public TMP_Text marker;
            public TMP_Text label;
        }

        [Header("頁面")]
        [SerializeField] private GameObject _page1Root;
        [SerializeField] private GameObject _page2Root;
        [SerializeField] private TMP_Text _pageIndicatorText;

        [Header("翻頁箭頭（第 1 頁顯示右箭頭、第 2 頁顯示左箭頭）")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;

        [Header("第 1 頁：武器清單")]
        [SerializeField] private WeaponRow[] _rows = new WeaponRow[4];

        [Header("第 1 頁：原畫與介紹")]
        [Tooltip("主原畫：交叉時為其中一把，非交叉時為單張")]
        [SerializeField] private Image _artImage;
        [Tooltip("交叉用的第二把原畫，非交叉頁面自動隱藏")]
        [SerializeField] private Image _artImageB;
        [Tooltip("交叉時每把傾斜角度（度）")]
        [SerializeField] private float _crossAngle = 23f;
        [Tooltip("斜放單張原畫的高度")]
        [SerializeField] private float _rotatedArtHeight = 660f;
        [Tooltip("交叉原畫每把的高度")]
        [SerializeField] private float _crossedArtHeight = 510f;
        [Tooltip("平放原畫（例：調色盤）的寬度")]
        [SerializeField] private float _flatArtWidth = 450f;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _descriptionText;

        [Header("第 2 頁")]
        [SerializeField] private TMP_Text _movePageTitleText;
        [SerializeField] private MoveListTableUI _moveTable;
        [Tooltip("按鍵試招＋小 Muir 動作展示")]
        [SerializeField] private MoveTrialController _trial;

        [Header("底部提示列：第 1 頁顯示「選擇武器」，第 2 頁換成「□ △ R1 試招」")]
        [SerializeField] private GameObject _selectHint;
        [SerializeField] private GameObject _trialHint;

        [Header("清單顏色")]
        [SerializeField] private Color _rowTextOn = new Color32(0xF3, 0xDC, 0xA6, 0xFF);
        [SerializeField] private Color _rowTextOff = new Color32(0x8F, 0x84, 0x6E, 0xFF);

        [Header("武器資料（順序：鉛筆/畫筆/畫刀/調色盤）")]
        [SerializeField] private WeaponPage[] _pages = new WeaponPage[4];

        private int _page;      // 0 = 武器一覽, 1 = 出招表
        private int _weapon;

        private void OnEnable()
        {
            _page = 0;
            _weapon = 0;
            RefreshAll();
        }

        private void OnDisable()
        {
            _trial?.End();
        }

        private void Start()
        {
            if (_leftArrowBtn != null) _leftArrowBtn.onClick.AddListener(() => SetPage(0));
            if (_rightArrowBtn != null) _rightArrowBtn.onClick.AddListener(() => SetPage(1));
            for (int i = 0; i < _rows.Length; i++)
            {
                int idx = i;
                if (_rows[i]?.button != null) _rows[i].button.onClick.AddListener(() => SelectWeapon(idx));
            }
        }

        private void Update()
        {
            var reader = PauseMenuUI.Instance != null ? PauseMenuUI.Instance.InputReader : null;
            if (reader == null) return;

            if (reader.MenuPageLeftPressed) { SetPage(0); return; }
            if (reader.MenuPageRightPressed) { SetPage(1); return; }

            // 第 2 頁不用上下鍵：攻擊鍵交給 MoveTrialController 試招
            if (_page != 0) return;
            if (reader.MenuUpPressed) SelectWeapon((_weapon - 1 + _pages.Length) % _pages.Length);
            else if (reader.MenuDownPressed) SelectWeapon((_weapon + 1) % _pages.Length);
        }

        private void SelectWeapon(int index)
        {
            _weapon = Mathf.Clamp(index, 0, _pages.Length - 1);
            RefreshAll();
        }

        private void SetPage(int page)
        {
            if (_page == page) return;
            _page = page;
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_pages == null || _pages.Length == 0) return;
            _weapon = Mathf.Clamp(_weapon, 0, _pages.Length - 1);
            var page = _pages[_weapon];

            if (_page1Root != null) _page1Root.SetActive(_page == 0);
            if (_page2Root != null) _page2Root.SetActive(_page == 1);
            if (_rightArrowBtn != null) _rightArrowBtn.gameObject.SetActive(_page == 0);
            if (_leftArrowBtn != null) _leftArrowBtn.gameObject.SetActive(_page == 1);

            if (_pageIndicatorText != null)
            {
                const string on = "#D9B36A", off = "#8A7F6A";
                _pageIndicatorText.text =
                    $"<color={(_page == 0 ? on : off)}>武器一覽</color>  <color=#7A6C52>｜</color>  " +
                    $"<color={(_page == 1 ? on : off)}>出招表</color>     <color=#B9AD94>{_page + 1} / 2</color>";
            }

            if (_selectHint != null) _selectHint.SetActive(_page == 0);
            if (_trialHint != null) _trialHint.SetActive(_page == 1);

            if (_page == 0) { _trial?.End(); RefreshWeaponList(page); }
            else RefreshMovePage(page);
        }

        private void RefreshWeaponList(WeaponPage page)
        {
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (row == null) continue;
                bool on = i == _weapon;
                if (row.band != null) row.band.enabled = on;
                if (row.bar != null) row.bar.enabled = on;
                if (row.marker != null) row.marker.enabled = on;
                if (row.label != null)
                {
                    row.label.text = i < _pages.Length ? _pages[i]?.shortName : string.Empty;
                    row.label.color = on ? _rowTextOn : _rowTextOff;
                }
            }

            ApplyArt(page);
            if (_nameText != null) _nameText.text = page?.weaponName ?? string.Empty;
            if (_descriptionText != null) _descriptionText.text = page?.description ?? string.Empty;
        }

        private void ApplyArt(WeaponPage page)
        {
            bool cross = page != null && page.crossed;
            bool hasArt = page?.art != null;
            float rot = page != null ? page.singleRotation : 0f;

            Vector2 size = Vector2.zero;
            if (hasArt)
            {
                Vector2 s = page.art.rect.size;
                float aspect = s.x / Mathf.Max(1f, s.y);
                if (cross) size = new Vector2(_crossedArtHeight * aspect, _crossedArtHeight);
                else if (!Mathf.Approximately(rot, 0f)) size = new Vector2(_rotatedArtHeight * aspect, _rotatedArtHeight);
                else size = new Vector2(_flatArtWidth, _flatArtWidth / aspect);
            }

            if (_artImage != null)
            {
                _artImage.sprite = page?.art;
                _artImage.enabled = hasArt;
                _artImage.rectTransform.sizeDelta = size;
                _artImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, cross ? _crossAngle : rot);
            }

            if (_artImageB != null)
            {
                _artImageB.sprite = page?.art;
                _artImageB.rectTransform.sizeDelta = size;
                _artImageB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_crossAngle);
                _artImageB.gameObject.SetActive(cross && hasArt);
            }
        }

        private void RefreshMovePage(WeaponPage page)
        {
            if (_movePageTitleText != null)
                _movePageTitleText.text = $"<b>{page?.weaponName}</b>   <size=60%><color=#CFC4AD><cspace=0.3em>出招表</cspace></color></size>";

            if (_moveTable != null) _moveTable.Build(page?.actionSet);
            _trial?.Begin(page?.actionSet, _weapon + 1);
        }
    }
}
