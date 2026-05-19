using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    // ════════════════════════════════════════════════════════════════
    //  GreedBossState  抽象基底
    // ════════════════════════════════════════════════════════════════
    public abstract class GreedBossState
    {
        protected GreedBossController Boss { get; }
        protected GreedBossStateMachine Machine { get; }
        protected Animator Anim => Boss.Animator;

        public abstract GreedBossStateType StateType { get; }

        protected GreedBossState(GreedBossController boss, GreedBossStateMachine machine)
        {
            Boss = boss;
            Machine = machine;
        }

        public virtual void Enter() { }
        public virtual void Update(float dt) { }
        public virtual void FixedUpdate(float dt) { }
        public virtual void Exit() { }

        protected void Go(GreedBossStateType next) => Machine.Request(next);
        protected void ForceGo(GreedBossStateType next) => Machine.Force(next);
        protected void Trigger(string name) => Anim.SetTrigger(name);
        protected void SetBool(string name, bool v) => Anim.SetBool(name, v);
    }

    // ════════════════════════════════════════════════════════════════
    //  GreedBossStateMachine  管理器
    // ════════════════════════════════════════════════════════════════
    public class GreedBossStateMachine
    {
        private readonly Dictionary<GreedBossStateType, GreedBossState> _states
            = new Dictionary<GreedBossStateType, GreedBossState>();

        public GreedBossState Current { get; private set; }
        public GreedBossStateType CurrentType => Current?.StateType ?? GreedBossStateType.Idle;

        public event Action<GreedBossStateType, GreedBossStateType> OnChanged;

        public void Init(Dictionary<GreedBossStateType, GreedBossState> states,
                         GreedBossStateType initial)
        {
            foreach (var kv in states) _states[kv.Key] = kv.Value;
            Current = _states[initial];
            Current.Enter();
        }

        /// <summary>一般轉換：Dead 狀態後不再接受任何請求。</summary>
        public bool Request(GreedBossStateType next)
        {
            if (CurrentType == GreedBossStateType.Dead) return false;
            if (!_states.TryGetValue(next, out var s))
            {
                Debug.LogWarning($"[GreedFSM] 狀態不存在：{next}");
                return false;
            }
            Transition(s);
            return true;
        }

        /// <summary>強制轉換，用於 HP=0 死亡與 KickScale 強制中斷。</summary>
        public void Force(GreedBossStateType next)
        {
            if (_states.TryGetValue(next, out var s)) Transition(s);
            else Debug.LogWarning($"[GreedFSM] Force: 狀態不存在 {next}");
        }

        public void Update(float dt) => Current?.Update(dt);
        public void FixedUpdate(float dt) => Current?.FixedUpdate(dt);

        private void Transition(GreedBossState next)
        {
            var prev = CurrentType;
            Current?.Exit();
            Current = next;
            Current.Enter();
            OnChanged?.Invoke(prev, next.StateType);
            Debug.Log($"[GreedFSM] {prev} → {next.StateType}");
        }
    }
}
