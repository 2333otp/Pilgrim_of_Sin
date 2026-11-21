using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 出招表的動畫播放器（純 C# 類別，由 PlayerController 持有）。
    ///
    /// 為什麼不用 Animator 的 state：出招表的每一招可以放任何動畫（切好的短動畫、之後新做的動畫），
    /// Animator 裡不可能每招都先建好 state；而且 state 綁的動畫跟程式計時各記一份，換動畫時長度很容易對不上，
    /// 造成動作播到一半被切掉或卡住。這裡用 Playables 把「這一招的動畫」直接疊在 Animator 上播：
    ///   · 播的就是出招表裡放的那段動畫，長度就是那段動畫的長度（單一來源）。
    ///   · 原本的 Animator Controller 完全不用改，底下照常運作；攻擊結束時淡出，交回 Animator。
    ///   · 根位移照樣透過 Animator.deltaPosition 傳給 PlayerController.OnAnimatorMove。
    /// 兩個輪流使用的播放槽：接下一招時新舊兩招交叉淡入淡出，不會跳格。
    /// </summary>
    public class AttackClipPlayer
    {
        private readonly Animator _animator;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationPlayableOutput _output;

        private readonly AnimationClipPlayable[] _slots = new AnimationClipPlayable[2];
        private readonly float[] _slotWeight = new float[2];
        private readonly float[] _slotTarget = new float[2];
        private float _slotFadeSpeed = 10f;
        private int _active = -1;

        private float _outWeight;
        private float _outTarget;
        private float _outFadeSpeed = 10f;

        public AttackClipPlayer(Animator animator) => _animator = animator;

        /// <summary>目前這一招已經播了幾秒（已乘上播放速度前的動畫時間）。</summary>
        public float CurrentClipTime =>
            _active >= 0 && _slots[_active].IsValid() ? (float)_slots[_active].GetTime() : 0f;

        public bool IsVisible => _outWeight > 0.001f || _outTarget > 0f;

        private void EnsureGraph()
        {
            if (_graph.IsValid()) return;
            _graph = PlayableGraph.Create("PlayerComboMoves");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);   // 跟 timeScale 走：暫停時一起停
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _output = AnimationPlayableOutput.Create(_graph, "ComboMove", _animator);
            _output.SetSourcePlayable(_mixer);
            _output.SetWeight(0f);
            _graph.Play();
            _active = -1;
            _outWeight = _outTarget = 0f;
        }

        /// <summary>播一段動畫。已經在播別的招時，新舊兩招在 fadeSeconds 內交叉淡換。</summary>
        public void Play(AnimationClip clip, float speed, float fadeSeconds = 0.1f)
        {
            if (clip == null || _animator == null) return;
            EnsureGraph();

            bool continuing = _active >= 0 && _outTarget > 0f && _outWeight > 0.001f;
            int slot = _active < 0 ? 0 : 1 - _active;

            if (_slots[slot].IsValid())
            {
                _graph.Disconnect(_mixer, slot);
                _slots[slot].Destroy();
            }

            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetSpeed(speed);
            playable.SetTime(0);
            playable.SetDuration(clip.length);   // 非循環：播完停在最後一格，不會重頭再播
            // 腳部 IK 一定要關：AnimationClipPlayable 預設是開的，但鉛筆/水彩筆的動畫是用 HumanPoseHandler
            // 轉成 Humanoid 的（UpdateAnimHumanoidConverter），沒有腳的 IK 目標曲線，開著會把腳拉向不存在的
            // 目標、整隻腳摺起來（實測骨盆到腳 0.88m → 0.14m）。Animator 裡原本的攻擊 state 也都沒開 Foot IK。
            playable.SetApplyFootIK(false);
            _graph.Connect(playable, 0, _mixer, slot);
            _slots[slot] = playable;

            float speedPerSec = fadeSeconds > 0.0001f ? 1f / fadeSeconds : 1000f;
            if (continuing)
            {
                // 接下一招：新槽從 0 淡入、舊槽淡出
                _slotWeight[slot] = 0f;
                _slotTarget[slot] = 1f;
                _slotTarget[1 - slot] = 0f;
            }
            else
            {
                // 從 Animator 接手：這一槽直接全權重，整個輸出從 Animator 淡入
                _slotWeight[slot] = 1f;
                _slotTarget[slot] = 1f;
                _slotWeight[1 - slot] = 0f;
                _slotTarget[1 - slot] = 0f;
            }
            _slotFadeSpeed = speedPerSec;
            _outTarget = 1f;
            _outFadeSpeed = speedPerSec;
            _active = slot;
            ApplyWeights();
        }

        /// <summary>攻擊結束：整個輸出淡出，交回 Animator。</summary>
        public void Stop(float fadeSeconds = 0.15f)
        {
            if (!_graph.IsValid()) return;
            _outTarget = 0f;
            _outFadeSpeed = fadeSeconds > 0.0001f ? 1f / fadeSeconds : 1000f;
        }

        /// <summary>每幀由 PlayerController.Update 呼叫，推進淡入淡出。</summary>
        public void Tick(float dt)
        {
            if (!_graph.IsValid()) return;

            for (int i = 0; i < 2; i++)
                _slotWeight[i] = Mathf.MoveTowards(_slotWeight[i], _slotTarget[i], _slotFadeSpeed * dt);
            _outWeight = Mathf.MoveTowards(_outWeight, _outTarget, _outFadeSpeed * dt);
            ApplyWeights();

            // 完全淡出後釋放兩個槽的動畫，下一次出招重新從 Animator 接手
            if (_outTarget <= 0f && _outWeight <= 0f && _active >= 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (!_slots[i].IsValid()) continue;
                    _graph.Disconnect(_mixer, i);
                    _slots[i].Destroy();
                }
                _active = -1;
            }
        }

        private void ApplyWeights()
        {
            float total = _slotWeight[0] + _slotWeight[1];
            for (int i = 0; i < 2; i++)
                _mixer.SetInputWeight(i, total > 0.0001f ? _slotWeight[i] / total : 0f);
            _output.SetWeight(_outWeight);
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
