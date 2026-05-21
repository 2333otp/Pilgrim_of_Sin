using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    public class WrathBossStateMachine
    {
        private readonly Dictionary<WrathBossStateType, WrathBossState> _states
            = new Dictionary<WrathBossStateType, WrathBossState>();

        public WrathBossState Current { get; private set; }
        public WrathBossStateType CurrentType => Current?.StateType ?? WrathBossStateType.Idle;

        public event Action<WrathBossStateType, WrathBossStateType> OnChanged;

        public void Init(Dictionary<WrathBossStateType, WrathBossState> states,
                         WrathBossStateType initial)
        {
            foreach (var kv in states) _states[kv.Key] = kv.Value;
            Current = _states[initial];
            Current.Enter();
        }

        /// <summary>一般轉換：Dead 後鎖死。</summary>
        public bool Request(WrathBossStateType next)
        {
            if (CurrentType == WrathBossStateType.Dead) return false;
            if (!_states.TryGetValue(next, out var s))
            {
                Debug.LogWarning($"[WrathFSM] 狀態不存在：{next}");
                return false;
            }
            Transition(s);
            return true;
        }

        /// <summary>強制轉換，用於 HP=0 死亡與外部中斷。</summary>
        public void Force(WrathBossStateType next)
        {
            if (_states.TryGetValue(next, out var s)) Transition(s);
            else Debug.LogWarning($"[WrathFSM] Force: 狀態不存在 {next}");
        }

        public void Update(float dt) => Current?.Update(dt);
        public void FixedUpdate(float dt) => Current?.FixedUpdate(dt);

        private void Transition(WrathBossState next)
        {
            var prev = CurrentType;
            Current?.Exit();
            Current = next;
            Current.Enter();
            OnChanged?.Invoke(prev, next.StateType);
            Debug.Log($"[WrathFSM] {prev} → {next.StateType}");
        }
    }
}