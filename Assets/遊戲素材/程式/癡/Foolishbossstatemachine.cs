using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    public class FoolishBossStateMachine
    {
        private readonly Dictionary<FoolishBossStateType, FoolishBossState> _states
            = new Dictionary<FoolishBossStateType, FoolishBossState>();

        public FoolishBossState Current { get; private set; }
        public FoolishBossStateType CurrentType => Current?.StateType ?? FoolishBossStateType.Idle;

        public event Action<FoolishBossStateType, FoolishBossStateType> OnChanged;

        public void Init(Dictionary<FoolishBossStateType, FoolishBossState> states,
                         FoolishBossStateType initial)
        {
            foreach (var kv in states) _states[kv.Key] = kv.Value;
            Current = _states[initial];
            Current.Enter();
        }

        /// <summary>一般轉換：Dead 後鎖死。</summary>
        public bool Request(FoolishBossStateType next)
        {
            if (CurrentType == FoolishBossStateType.Dead) return false;
            if (!_states.TryGetValue(next, out var s))
            {
                Debug.LogWarning($"[FoolishFSM] 狀態不存在：{next}");
                return false;
            }
            Transition(s);
            return true;
        }

        /// <summary>強制轉換，用於 HP=0 死亡。</summary>
        public void Force(FoolishBossStateType next)
        {
            if (_states.TryGetValue(next, out var s)) Transition(s);
            else Debug.LogWarning($"[FoolishFSM] Force: 狀態不存在 {next}");
        }

        public void Update(float dt) => Current?.Update(dt);
        public void FixedUpdate(float dt) => Current?.FixedUpdate(dt);

        private void Transition(FoolishBossState next)
        {
            var prev = CurrentType;
            Current?.Exit();
            Current = next;
            Current.Enter();
            OnChanged?.Invoke(prev, next.StateType);
            Debug.Log($"[FoolishFSM] {prev} → {next.StateType}");
        }
    }
}