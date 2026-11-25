using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 玩家戰鬥管理器。
    /// 負責：
    ///   - 持有各武器的攻擊傷害數值（Inspector 可調）
    ///   - 啟用/停用攻擊碰撞體
    ///   - 由 PlayerController 的攻擊狀態呼叫
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerAttackHitbox _hitbox;

        // ── 武器1 鉛筆 ───────────────────────────────────────────────
        [Header("Weapon 1 - Pencil")]
        [SerializeField] private float _pencilLight = 70f;
        [SerializeField] private float _pencilHeavy = 90f;
        [SerializeField] private float _pencilCombo1 = 165f;
        [SerializeField] private float _pencilCombo2 = 180f;
        [SerializeField] private float _pencilCombo3 = 215f;
        [SerializeField] private float _pencilCombo4 = 620f;
        [SerializeField] private float _pencilSpecial = 720f;

        // ── 武器2 水彩筆 ─────────────────────────────────────────────
        [Header("Weapon 2 - Brush")]
        [SerializeField] private float _brushLight = 90f;
        [SerializeField] private float _brushHeavy = 100f;
        [SerializeField] private float _brushCombo1 = 280f;
        [SerializeField] private float _brushCombo2 = 280f;
        [SerializeField] private float _brushCombo3 = 330f;
        [SerializeField] private float _brushCombo4 = 440f;
        [SerializeField] private float _brushSpecial = 650f;

        // ── 武器3 畫刀 ───────────────────────────────────────────────
        [Header("Weapon 3 - Palette Knife")]
        [SerializeField] private float _knifeLight = 80f;
        [SerializeField] private float _knifeHeavy = 110f;
        [SerializeField] private float _knifeCombo1 = 290f;
        [SerializeField] private float _knifeCombo2 = 300f;
        [SerializeField] private float _knifeCombo3 = 340f;
        [SerializeField] private float _knifeCombo4 = 400f;
        [SerializeField] private float _knifeSpecial = 630f;

        // ── 武器4 調色盤 ─────────────────────────────────────────────
        [Header("Weapon 4 - Palette")]
        [SerializeField] private float _paletteLight = 80f;
        [SerializeField] private float _paletteHeavy = 130f;
        [SerializeField] private float _paletteCombo1 = 280f;
        [SerializeField] private float _paletteCombo2 = 280f;
        [SerializeField] private float _paletteCombo3 = 330f;
        [SerializeField] private float _paletteCombo4 = 480f;
        [SerializeField] private float _paletteSpecial = 700f;

        // ── 當前武器索引（由 PlayerController 更新） ─────────────────
        public int CurrentWeaponIndex { get; set; } = 1;

        // ────────────────────────────────────────────────────────────
        //  公開介面（由攻擊狀態呼叫）
        // ────────────────────────────────────────────────────────────

        // 舊連段的打擊感：輕攻擊 輕、重攻擊 中、連段最後一下（4）中、其餘輕、特殊技 重
        public void StartLightAttack()
            => _hitbox?.Activate(GetDamage(AttackType.Light), null, 0f, HitImpactLevel.Light);

        public void StartHeavyAttack()
            => _hitbox?.Activate(GetDamage(AttackType.Heavy), null, 0f, HitImpactLevel.Medium);

        public void StartComboAttack(int comboIndex)
            => _hitbox?.Activate(GetDamage(AttackType.Combo, comboIndex), null, 0f,
                                 comboIndex >= 4 ? HitImpactLevel.Medium : HitImpactLevel.Light);

        public void StartSpecialAttack()
            => _hitbox?.Activate(GetDamage(AttackType.Special), null, 0f, HitImpactLevel.Heavy);

        public void EndAttack()
            => _hitbox?.Deactivate();

        /// <summary>出招表的一招：用指定傷害開啟攻擊判定（每一招重新計一次命中）。</summary>
        public void StartMoveAttack(float damage)
            => _hitbox?.Activate(damage);

        /// <summary>出招表的一下：指定判定形狀（null = 舊的球）與這一下的秒數（往前飛的判定用）。</summary>
        public void StartMoveAttack(float damage, HitboxShape shape, float duration)
            => _hitbox?.Activate(damage, shape, duration);

        /// <summary>同上，並指定打中時的打擊感檔位。</summary>
        public void StartMoveAttack(float damage, HitboxShape shape, float duration, HitImpactLevel impact)
            => _hitbox?.Activate(damage, shape, duration, impact);

        /// <summary>攻擊判定元件（F1 顯示判定形狀用）。</summary>
        public PlayerAttackHitbox Hitbox => _hitbox;

        /// <summary>
        /// 出招表沒填傷害（0）時的預設值，沿用上面的舊數值：
        /// 一般攻擊 = 輕攻擊；C1 = 重攻擊；C2 / C3 / C4 = 連段 1 / 2 / 4（C4 當最後的大招）。
        /// </summary>
        public float GetDefaultMoveDamage(bool isCharge, int index)
        {
            if (!isCharge) return GetDamage(AttackType.Light);
            return index switch
            {
                0 => GetDamage(AttackType.Heavy),
                1 => GetDamage(AttackType.Combo, 1),
                2 => GetDamage(AttackType.Combo, 2),
                _ => GetDamage(AttackType.Combo, 4),
            };
        }

        /// <summary>目前武器的特殊技（秘劍）整招傷害。</summary>
        public float SpecialDamage => GetDamage(AttackType.Special);

        /// <summary>惡魔獵人分支沒填傷害時的預設值：停頓連打 = 連段 3；蓄力 = 連段 4。</summary>
        public float GetDefaultBranchMoveDamage(bool isHold)
            => GetDamage(AttackType.Combo, isHold ? 4 : 3);

        // ────────────────────────────────────────────────────────────
        //  傷害查詢
        // ────────────────────────────────────────────────────────────

        private enum AttackType { Light, Heavy, Combo, Special }

        private float GetDamage(AttackType type, int comboIndex = 1)
        {
            return CurrentWeaponIndex switch
            {
                1 => GetPencilDamage(type, comboIndex),
                2 => GetBrushDamage(type, comboIndex),
                3 => GetKnifeDamage(type, comboIndex),
                4 => GetPaletteDamage(type, comboIndex),
                _ => 0f,
            };
        }

        private float GetPencilDamage(AttackType type, int combo) => type switch
        {
            AttackType.Light => _pencilLight,
            AttackType.Heavy => _pencilHeavy,
            AttackType.Special => _pencilSpecial,
            _ => combo switch { 1 => _pencilCombo1, 2 => _pencilCombo2, 3 => _pencilCombo3, _ => _pencilCombo4 },
        };

        private float GetBrushDamage(AttackType type, int combo) => type switch
        {
            AttackType.Light => _brushLight,
            AttackType.Heavy => _brushHeavy,
            AttackType.Special => _brushSpecial,
            _ => combo switch { 1 => _brushCombo1, 2 => _brushCombo2, 3 => _brushCombo3, _ => _brushCombo4 },
        };

        private float GetKnifeDamage(AttackType type, int combo) => type switch
        {
            AttackType.Light => _knifeLight,
            AttackType.Heavy => _knifeHeavy,
            AttackType.Special => _knifeSpecial,
            _ => combo switch { 1 => _knifeCombo1, 2 => _knifeCombo2, 3 => _knifeCombo3, _ => _knifeCombo4 },
        };

        private float GetPaletteDamage(AttackType type, int combo) => type switch
        {
            AttackType.Light => _paletteLight,
            AttackType.Heavy => _paletteHeavy,
            AttackType.Special => _paletteSpecial,
            _ => combo switch { 1 => _paletteCombo1, 2 => _paletteCombo2, 3 => _paletteCombo3, _ => _paletteCombo4 },
        };
    }
}