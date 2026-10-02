using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 掛在貪 Boss 的骨架模型（子物件 Model，也就是帶 Animator 的那一個）上。
    /// Unity 的 Animation Event 只會呼叫「Animator 所在物件」上的方法，
    /// 但 Boss 的邏輯在父物件的 GreedBossController，所以這裡只負責把事件轉發上去。
    /// </summary>
    public class GreedBossAnimationEvents : MonoBehaviour
    {
        private GreedBossController _boss;

        private GreedBossController Boss
        {
            get
            {
                if (_boss == null) _boss = GetComponentInParent<GreedBossController>();
                return _boss;
            }
        }

        public void AnimEvent_AttackHit() => Boss?.AnimEvent_AttackHit();
        public void AnimEvent_AttackEnd() => Boss?.AnimEvent_AttackEnd();
    }
}
