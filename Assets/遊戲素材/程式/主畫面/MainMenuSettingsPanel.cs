using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 主選單「設置選單」面板控制。
    /// 面板美術與音量綁定邏輯沿用 ESC 選單（PauseMenuUI）的設定子面板。
    /// 子面板間的返回改用 ESC 鍵（New Input System），不使用返回按鈕。
    /// </summary>
    public class MainMenuSettingsPanel : MonoBehaviour
    {
        [Header("開啟入口")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private GameObject _mainButtonGroup;

        [Header("子面板根物件")]
        [SerializeField] private GameObject _settingsSubPanel;
        [SerializeField] private GameObject _volumeSubPanel;
        [SerializeField] private GameObject _controlsSubPanel;
        [SerializeField] private GameObject _creditsSubPanel;

        [Header("設置選單 - 入口按鈕")]
        [SerializeField] private Button _btnSettingsVolume;
        [SerializeField] private Button _btnSettingsControls;
        [SerializeField] private Button _btnSettingsCredits;

        [Header("音量滑桿")]
        [SerializeField] private Slider _masterSlider;
        [SerializeField] private Slider _musicSlider;
        [SerializeField] private Slider _sfxSlider;

        private void Start()
        {
            _settingsButton.onClick.AddListener(OpenSettings);
            _btnSettingsVolume.onClick.AddListener(() => Show(_volumeSubPanel));
            _btnSettingsControls.onClick.AddListener(() => Show(_controlsSubPanel));
            _btnSettingsCredits.onClick.AddListener(() => Show(_creditsSubPanel));

            BindSliders();
            HideAll();
        }

        // ── ESC 鍵／手把返回鍵返回（比照 ESC 暫停選單邏輯）─────────────────

        private void Update()
        {
            bool keyboardBack = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool gamepadBack  = Gamepad.current  != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
            if (!keyboardBack && !gamepadBack)
                return;

            if (_volumeSubPanel.activeSelf || _controlsSubPanel.activeSelf || _creditsSubPanel.activeSelf)
            {
                Show(_settingsSubPanel);
            }
            else if (_settingsSubPanel.activeSelf)
            {
                CloseSettings();
            }
        }

        private void OpenSettings()
        {
            _mainButtonGroup.SetActive(false);
            Show(_settingsSubPanel);
        }

        private void CloseSettings()
        {
            HideAll();
            _mainButtonGroup.SetActive(true);
        }

        private void Show(GameObject panel)
        {
            _settingsSubPanel.SetActive(panel == _settingsSubPanel);
            _volumeSubPanel.SetActive(panel == _volumeSubPanel);
            _controlsSubPanel.SetActive(panel == _controlsSubPanel);
            _creditsSubPanel.SetActive(panel == _creditsSubPanel);

            if (panel == _volumeSubPanel)
                SyncSliderValues();
        }

        private void HideAll()
        {
            _settingsSubPanel.SetActive(false);
            _volumeSubPanel.SetActive(false);
            _controlsSubPanel.SetActive(false);
            _creditsSubPanel.SetActive(false);
        }

        // ── 音量同步（與 PauseMenuUI 相同邏輯）───────────────────────────

        private void SyncSliderValues()
        {
            var gpm = GameProgressManager.Instance;
            if (gpm == null) return;
            _masterSlider?.SetValueWithoutNotify(gpm.MasterVolume);
            _musicSlider?.SetValueWithoutNotify(gpm.MusicVolume);
            _sfxSlider?.SetValueWithoutNotify(gpm.SFXVolume);
        }

        private void BindSliders()
        {
            _masterSlider?.onValueChanged.AddListener(v => GameProgressManager.Instance?.SetMasterVolume(v));
            _musicSlider?.onValueChanged.AddListener(v => GameProgressManager.Instance?.SetMusicVolume(v));
            _sfxSlider?.onValueChanged.AddListener(v => GameProgressManager.Instance?.SetSFXVolume(v));
        }
    }
}
