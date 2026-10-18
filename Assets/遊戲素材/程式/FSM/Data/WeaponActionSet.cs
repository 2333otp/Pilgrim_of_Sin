using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 單一武器的動作素材集（鉛筆 / 水彩筆 / 畫刀 / 調色盤各一個）。
    /// 每筆 PlayerActionEntry 對應 Animator 裡「動作_武器後綴」的 state，例如 LightAttack_Pencil。
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponActions_新武器", menuName = "PilgrimOfSin/主角/武器動作集 (Weapon Action Set)")]
    public class WeaponActionSet : ScriptableObject
    {
        [Tooltip("Animator state 名稱的武器後綴（Pencil / Brush / PaintKnife / Palette）。")]
        public string weaponSuffix = "Pencil";

        public List<PlayerActionEntry> actions = new List<PlayerActionEntry>();
    }
}
