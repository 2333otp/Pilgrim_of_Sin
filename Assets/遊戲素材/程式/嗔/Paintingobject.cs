using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 五芒星頂點的畫，掛在場景中各頂點的畫物件上。
    /// 玩家攻擊命中 Collider 後，透過 IDamageable 扣耐久值；
    /// 耐久歸零後永久改回學徒簽名，進度不重置（玩家被打斷後保留）。
    ///
    /// 視覺切換：在 Inspector 把「主角版本」視覺掛 _originalVisual、
    ///           「學徒版本」視覺掛 _modifiedVisual（可以是子物件或任意 GameObject）。
    ///           兩個都不設也不會報錯，只是沒有視覺變化。
    /// </summary>
    public class PaintingObject : MonoBehaviour, IDamageable
    {
        [SerializeField] private float _maxDurability = 350f;

        [Header("Visual")]
        [Tooltip("主角簽名版本的視覺物件（初始顯示）")]
        [SerializeField] private GameObject _originalVisual;
        [Tooltip("學徒簽名版本的視覺物件（改寫後顯示）")]
        [SerializeField] private GameObject _modifiedVisual;

        public event Action OnModified;
        public bool IsModified { get; private set; } = false;

        private float _currentDurability;

        private void Awake()
        {
            _currentDurability = _maxDurability;
            ApplyVisual(modified: false);
        }

        // ── IDamageable ───────────────────────────────────────────────

        public void TakeDamage(float amount)
        {
            if (IsModified) return;
            _currentDurability = Mathf.Max(0f, _currentDurability - amount);
            Debug.Log($"[Painting] {gameObject.name} 耐久 {_currentDurability}/{_maxDurability}");

            if (_currentDurability <= 0f)
                ModifyPainting();
        }

        // ── 改寫與重置 ────────────────────────────────────────────────

        private void ModifyPainting()
        {
            IsModified = true;
            ApplyVisual(modified: true);
            OnModified?.Invoke();
            Debug.Log($"[Painting] {gameObject.name} 耐久歸零，改回學徒簽名。");
        }

        /// <summary>關卡重試時由外部呼叫，恢復初始狀態（場景 reload 時自動還原，此方法供 Editor 測試用）。</summary>
        public void ResetPainting()
        {
            IsModified = false;
            _currentDurability = _maxDurability;
            ApplyVisual(modified: false);
        }

        // ── 視覺切換 ──────────────────────────────────────────────────

        private void ApplyVisual(bool modified)
        {
            if (_originalVisual != null) _originalVisual.SetActive(!modified);
            if (_modifiedVisual != null) _modifiedVisual.SetActive(modified);
        }
    }
}
