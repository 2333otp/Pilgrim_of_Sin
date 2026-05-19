using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 所有玩家狀態的抽象基底類別。
    /// 每個具體狀態繼承此類別，並實作 Enter / Update / Exit。
    /// </summary>
    public abstract class PlayerState
    {
        // ── 共用引用 ─────────────────────────────────────────────────
        protected PlayerController Player { get; private set; }
        protected PlayerStateMachine Machine { get; private set; }
        protected Animator Anim { get; private set; }
        protected PlayerInputReader Input { get; private set; }

        // 此狀態對應的枚舉值（用於外部查詢目前狀態）
        public abstract PlayerStateType StateType { get; }

        // ── 建構子 ───────────────────────────────────────────────────
        protected PlayerState(PlayerController player, PlayerStateMachine machine)
        {
            Player = player;
            Machine = machine;
            Anim = player.Animator;
            Input = player.InputReader;
        }

        // ── 生命周期 ─────────────────────────────────────────────────

        /// <summary>進入此狀態時呼叫一次。</summary>
        public virtual void Enter() { }

        /// <summary>每幀呼叫（物理前）。負責邏輯判斷與狀態轉換請求。</summary>
        public virtual void Update(float deltaTime) { }

        /// <summary>每幀呼叫（物理後）。負責移動、Rigidbody 操作。</summary>
        public virtual void FixedUpdate(float fixedDeltaTime) { }

        /// <summary>離開此狀態時呼叫一次。</summary>
        public virtual void Exit() { }

        // ── 共用輔助方法 ─────────────────────────────────────────────

        /// <summary>
        /// 請求切換到另一個狀態。
        /// 若 StateMachine 的優先級檢查不通過，此切換會被忽略。
        /// </summary>
        protected void RequestTransition(PlayerStateType next)
            => Machine.RequestTransition(next);

        /// <summary>播放 Animator 參數（Trigger）。</summary>
        protected void PlayAnimation(string triggerName)
            => Anim.SetTrigger(triggerName);

        /// <summary>設定 Animator Bool 參數。</summary>
        protected void SetAnimBool(string paramName, bool value)
            => Anim.SetBool(paramName, value);
    }
}