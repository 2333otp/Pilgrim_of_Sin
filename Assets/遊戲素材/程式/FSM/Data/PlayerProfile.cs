using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 主角素材總表。掛在 PlayerActionFeedback 上，換動畫 / 音效 / 配音 / 特效都改這裡，不用動程式。
    /// 四把武器各有一份動作集；翻滾、死亡這類四把武器共用的動作放在 sharedActions。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerProfile", menuName = "PilgrimOfSin/主角/主角素材總表 (Player Profile)")]
    public class PlayerProfile : ScriptableObject
    {
        [Header("武器動作集（索引 1~4 對應 CurrentWeaponIndex）")]
        public WeaponActionSet pencil;
        public WeaponActionSet brush;
        public WeaponActionSet paintKnife;
        public WeaponActionSet palette;

        [Header("共用動作（翻滾、死亡等與武器無關的動作）")]
        public List<PlayerActionEntry> sharedActions = new List<PlayerActionEntry>();

        public WeaponActionSet GetSet(int weaponIndex) => weaponIndex switch
        {
            1 => pencil,
            2 => brush,
            3 => paintKnife,
            4 => palette,
            _ => pencil,
        };

        public IEnumerable<PlayerActionEntry> AllEntries()
        {
            foreach (var set in new[] { pencil, brush, paintKnife, palette })
            {
                if (set == null) continue;
                foreach (var e in set.actions) if (e != null) yield return e;
            }
            foreach (var e in sharedActions) if (e != null) yield return e;
        }
    }
}
