using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 錢袋個體，掛在錢袋預製體上。
    /// </summary>
    public class MoneybagObject : MonoBehaviour
    {
        public float Weight { get; private set; }
        private ScaleObject _scale;

        public void Init(float weight, ScaleObject scale)
        {
            Weight = weight;
            _scale = scale;
        }

        /// <summary>玩家靠近拾取（OnTriggerEnter 呼叫）。</summary>
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _scale?.RemoveMoneybag(this);
        }

        /// <summary>被攻擊擊落（由攻擊系統呼叫）。</summary>
        public void KnockOff() => _scale?.RemoveMoneybag(this);
    }
}
