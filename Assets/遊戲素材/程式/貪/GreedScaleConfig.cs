using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤（Libra）受擊觸發設定，企劃可直接在 Assets/遊戲素材/SO 底下調整，不用開場景。
    /// </summary>
    [CreateAssetMenu(fileName = "GreedScaleConfig", menuName = "PilgrimOfSin/Greed Scale Config")]
    public class GreedScaleConfig : ScriptableObject
    {
        [Tooltip("玩家累積攻擊天秤幾下才觸發一次「受擊+重製」動畫（打落錢袋、重新生成一批）。")]
        [Min(1)]
        [SerializeField] private int _hitsRequiredToTrigger = 1;

        public int HitsRequiredToTrigger => _hitsRequiredToTrigger;
    }
}
