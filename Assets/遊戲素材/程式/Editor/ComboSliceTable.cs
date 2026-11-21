#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 連段動畫切割表（只在 Editor 使用）。
    /// 每一筆 = 一支原始動畫 + 要切成哪幾段（用「影格」表示，跟 Animation 視窗看到的格數一致）。
    /// 改完數字後按 Inspector 上的「產生切割動畫」，輸出到 animation/Sliced/&lt;武器&gt;/。
    /// 原始動畫不會被修改；重複產生會覆蓋輸出檔（GUID 不變，已引用的地方不會斷）。
    /// </summary>
    [CreateAssetMenu(fileName = "ComboSliceTable", menuName = "PilgrimOfSin/主角/連段動畫切割表")]
    public class ComboSliceTable : ScriptableObject
    {
        public List<SliceSource> sources = new List<SliceSource>();
    }

    [Serializable]
    public class SliceSource
    {
        [Tooltip("顯示用名稱，例如 鉛筆 Combo2。")]
        public string label;

        [Tooltip("輸出子資料夾（武器名），例如 Pencil。")]
        public string weapon;

        [Tooltip("要切的原始動畫（Humanoid）。")]
        public AnimationClip source;

        [Tooltip("勾選才會產生。")]
        public bool enabled = true;

        [Tooltip("把根的上下位移烘進姿勢（Bake Into Pose: Root Transform Position Y）。\n" +
                 "跳躍類的動作要勾：遊戲裡只吃水平根位移，上下位移會被丟掉，不勾的話角色會貼在地上播空中動作。")]
        public bool bakeRootY;

        [TextArea(1, 4)]
        [Tooltip("分析備註（自動分析的揮擊高峰、可疑處）。")]
        public string note;

        public List<SliceSegment> segments = new List<SliceSegment>();
    }

    [Serializable]
    public class SliceSegment
    {
        [Tooltip("輸出檔名（不含 .anim）。")]
        public string name;

        [Min(0)]
        [Tooltip("起始影格（含）。")]
        public int startFrame;

        [Min(0)]
        [Tooltip("結束影格（含）。")]
        public int endFrame;
    }
}
#endif
