using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 繆爾玩家狀態機管理器。
    /// 負責：
    ///   1. 維護所有狀態實例
    ///   2. 執行優先級規則，決定是否允許轉換
    ///   3. 呼叫當前狀態的 Update / FixedUpdate
    /// </summary>
    public class PlayerStateMachine
    {
        // ── 優先級表（數字越小越優先） ─────────────────────────────
        // 未列出的狀態視為最低優先（int.MaxValue），可無條件進入
        private static readonly Dictionary<PlayerStateType, int> StatePriority
            = new Dictionary<PlayerStateType, int>
        {
            { PlayerStateType.Paused,       0 },
            { PlayerStateType.Dead,         0 }, // 死亡與暫停同級，任何時候都可觸發
            { PlayerStateType.SpecialSkill, 1 }, // 可打斷普攻和連段（1 < 2）
            { PlayerStateType.LightAttack,  2 },
            { PlayerStateType.HeavyAttack,  2 },
            { PlayerStateType.ComboAttack,  2 },
            { PlayerStateType.Jump,         3 },
            { PlayerStateType.Roll,         3 }, // Roll 只有 SpecialSkill（1）可打斷
        };

        // ── Damaged 可中斷的狀態白名單 ──────────────────────────────
        // 只有這些狀態下被打才會進入 Damaged
        // SpecialSkill、Roll、WeaponSwitch、Paused 不在列 → 這些狀態下免疫 Damaged
        private static readonly HashSet<PlayerStateType> DamagableStates
            = new HashSet<PlayerStateType>
        {
            PlayerStateType.Idle,
            PlayerStateType.Walk,
            PlayerStateType.Sprint,
            PlayerStateType.Jump,
            PlayerStateType.Fall,
            PlayerStateType.LightAttack,
            PlayerStateType.HeavyAttack,
            PlayerStateType.ComboAttack,
            PlayerStateType.Damaged,       // 連續受傷可重入
        };

        // ── 狀態字典 ─────────────────────────────────────────────────
        private readonly Dictionary<PlayerStateType, PlayerState> _states
            = new Dictionary<PlayerStateType, PlayerState>();

        // ── 當前 / 前一個狀態 ────────────────────────────────────────
        public PlayerState CurrentState { get; private set; }
        public PlayerStateType CurrentStateType => CurrentState?.StateType ?? PlayerStateType.Idle;
        /// <summary>進入當前狀態之前的狀態，供 PausedState 恢復用。</summary>
        public PlayerStateType PreviousStateType { get; private set; } = PlayerStateType.Idle;

        // ── 事件（可選，供 UI / 動畫系統訂閱） ─────────────────────
        public event Action<PlayerStateType, PlayerStateType> OnStateChanged;

        // ── 初始化 ───────────────────────────────────────────────────

        /// <summary>
        /// 註冊狀態實例並設定初始狀態。
        /// 在 PlayerController.Start() 中呼叫。
        /// </summary>
        public void Init(Dictionary<PlayerStateType, PlayerState> states,
                         PlayerStateType initialState)
        {
            foreach (var kvp in states)
                _states[kvp.Key] = kvp.Value;

            CurrentState = _states[initialState];
            CurrentState.Enter();
        }

        // ── 狀態轉換 ─────────────────────────────────────────────────

        /// <summary>
        /// 請求切換狀態。
        /// 規則：
        ///   - 目標狀態不存在 → 忽略
        ///   - Damaged 請求：只有在 DamagableStates 白名單內的當前狀態才允許
        ///     （SpecialSkill / Roll / WeaponSwitch / Paused 免疫）
        ///   - 優先級：目標數字 &lt;= 當前數字 → 允許
        ///             目標數字 &gt; 當前數字  → 拒絕
        ///   - IsUnconditionalTransition 的狀態不受優先級限制
        /// </summary>
        public bool RequestTransition(PlayerStateType next)
        {
            if (!_states.TryGetValue(next, out PlayerState nextState))
            {
                Debug.LogWarning($"[StateMachine] 找不到狀態：{next}");
                return false;
            }

            // Damaged 白名單檢查：只有可被打斷的狀態才允許進入 Damaged
            if (next == PlayerStateType.Damaged && !DamagableStates.Contains(CurrentStateType))
                return false;

            // 優先級檢查（Damaged 本身優先級未定義 → int.MaxValue，走無條件通道）
            int currentPriority = GetPriority(CurrentStateType);
            int nextPriority = GetPriority(next);

            bool allowed = nextPriority <= currentPriority
                           || IsUnconditionalTransition(next);

            if (!allowed) return false;

            DoTransition(nextState);
            return true;
        }

        // ── 強制轉換（繞過優先級，供系統內部使用） ─────────────────

        /// <summary>強制切換，不做優先級檢查（例如 HP=0 觸發死亡）。</summary>
        public void ForceTransition(PlayerStateType next)
        {
            if (!_states.TryGetValue(next, out PlayerState nextState))
            {
                Debug.LogWarning($"[StateMachine] 強制轉換找不到狀態：{next}");
                return;
            }
            DoTransition(nextState);
        }

        // ── Update 循環 ──────────────────────────────────────────────

        public void Update(float deltaTime)
            => CurrentState?.Update(deltaTime);

        public void FixedUpdate(float fixedDeltaTime)
            => CurrentState?.FixedUpdate(fixedDeltaTime);

        // ── 私有輔助 ─────────────────────────────────────────────────

        private void DoTransition(PlayerState nextState)
        {
            PreviousStateType = CurrentStateType;
            CurrentState?.Exit();
            CurrentState = nextState;
            CurrentState.Enter();
            OnStateChanged?.Invoke(PreviousStateType, nextState.StateType);
        }

        private static int GetPriority(PlayerStateType type)
            => StatePriority.TryGetValue(type, out int p) ? p : int.MaxValue;

        /// <summary>
        /// 無條件允許的轉換：不受優先級限制，任何狀態下都可進入。
        /// Damaged 不在此列，改由 DamagableStates 白名單控制。
        /// </summary>
        private static bool IsUnconditionalTransition(PlayerStateType next)
        {
            return next == PlayerStateType.Idle
                || next == PlayerStateType.Walk
                || next == PlayerStateType.Sprint
                || next == PlayerStateType.Fall
                || next == PlayerStateType.Dead
                || next == PlayerStateType.SpecialSkillCooldown
                || next == PlayerStateType.WeaponSwitch
                || next == PlayerStateType.Damaged;
        }
    }
}