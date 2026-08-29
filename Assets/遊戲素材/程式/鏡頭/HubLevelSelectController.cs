using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// HubScene 運鏡選關控制器。取代 CameraController 驅動 Main Camera：
    /// 場景載入後鏡頭自動從全景滑到第一個焦點，玩家上下切換三支煙囪（三個 Boss），
    /// 按確認鍵直接進入對應 Boss 場景。沒有取消鍵，離開交給既有的 ESC 暫停選單。
    /// </summary>
    public class HubLevelSelectController : MonoBehaviour
    {
        [Serializable]
        private class FocusEntry
        {
            public string label;
            [TextArea] public string description;
            public SceneTransitionManager.BossType bossType;
            public Transform focusPoint;
        }

        [Header("References")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Transform _widePoint;
        [SerializeField] private List<FocusEntry> _entries;

        [Header("Blend 設定")]
        [SerializeField] private float _blendDuration = 0.7f;
        [SerializeField] private float _openingHoldDuration = 0.4f;
        [SerializeField] private AnimationCurve _blendEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("UI")]
        [SerializeField] private TMP_Text _bossNameText;
        [SerializeField] private TMP_Text _bossDescriptionText;
        [SerializeField] private GameObject _dividerRoot;
        [SerializeField] private GameObject _confirmHintRoot;

        private int _currentIndex;
        private bool _inputLocked;

        private Vector3 _blendStartPos;
        private Quaternion _blendStartRot;
        private Vector3 _blendTargetPos;
        private Quaternion _blendTargetRot;
        private float _blendTimer;
        private bool _isBlending;

        private void Start()
        {
            _inputLocked = true;
            if (_confirmHintRoot != null) _confirmHintRoot.SetActive(false);
            if (_bossNameText != null) _bossNameText.gameObject.SetActive(false);
            if (_bossDescriptionText != null) _bossDescriptionText.gameObject.SetActive(false);
            if (_dividerRoot != null) _dividerRoot.SetActive(false);

            StartCoroutine(OpeningRoutine());
        }

        private System.Collections.IEnumerator OpeningRoutine()
        {
            BeginBlend(_widePoint.position, _widePoint.rotation);
            yield return WaitForBlend();

            yield return new WaitForSeconds(_openingHoldDuration);

            _currentIndex = 0;
            BeginBlend(_entries[_currentIndex].focusPoint.position, _entries[_currentIndex].focusPoint.rotation);
            yield return WaitForBlend();

            UpdateBossInfoText();
            if (_bossNameText != null) _bossNameText.gameObject.SetActive(true);
            if (_bossDescriptionText != null) _bossDescriptionText.gameObject.SetActive(true);
            if (_dividerRoot != null) _dividerRoot.SetActive(true);
            if (_confirmHintRoot != null) _confirmHintRoot.SetActive(true);
            _inputLocked = false;
        }

        private System.Collections.IEnumerator WaitForBlend()
        {
            while (_isBlending) yield return null;
        }

        private void Update()
        {
            if (_isBlending) UpdateBlend();

            if (_inputLocked || _input == null) return;

            if (_input.MenuUpPressed) SwitchFocus(1);
            else if (_input.MenuDownPressed) SwitchFocus(-1);
            else if (_input.InteractPressed) Confirm();
        }

        private void SwitchFocus(int direction)
        {
            if (_entries == null || _entries.Count == 0) return;

            _currentIndex = (_currentIndex + direction + _entries.Count) % _entries.Count;
            UpdateBossInfoText();

            var target = _entries[_currentIndex].focusPoint;
            BeginBlend(target.position, target.rotation);
        }

        private void Confirm()
        {
            if (_entries == null || _entries.Count == 0) return;

            _inputLocked = true;
            SceneTransitionManager.Instance?.LoadBossScene(_entries[_currentIndex].bossType);
        }

        // ── Blend ────────────────────────────────────────────────────

        private void BeginBlend(Vector3 targetPos, Quaternion targetRot)
        {
            _blendStartPos = _mainCamera.transform.position;
            _blendStartRot = _mainCamera.transform.rotation;
            _blendTargetPos = targetPos;
            _blendTargetRot = targetRot;
            _blendTimer = 0f;
            _isBlending = true;
        }

        private void UpdateBlend()
        {
            _blendTimer += Time.deltaTime;
            float t = _blendDuration <= 0f ? 1f : Mathf.Clamp01(_blendTimer / _blendDuration);
            float eased = _blendEase.Evaluate(t);

            _mainCamera.transform.position = Vector3.Lerp(_blendStartPos, _blendTargetPos, eased);
            _mainCamera.transform.rotation = Quaternion.Slerp(_blendStartRot, _blendTargetRot, eased);

            if (t >= 1f) _isBlending = false;
        }

        private void UpdateBossInfoText()
        {
            if (_bossNameText != null) _bossNameText.text = _entries[_currentIndex].label;
            if (_bossDescriptionText != null) _bossDescriptionText.text = _entries[_currentIndex].description;
        }
    }
}
