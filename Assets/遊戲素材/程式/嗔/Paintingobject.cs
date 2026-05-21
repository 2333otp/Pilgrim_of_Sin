using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 畫的場景物件，掛在五芒星各定點的畫上。
    /// 玩家攻擊命中後呼叫 ModifyPainting()。
    /// </summary>
    public class PaintingObject : MonoBehaviour
    {
        public event Action OnModified;
        public bool IsModified { get; private set; } = false;

        /// <summary>玩家攻擊命中此畫時呼叫。</summary>
        public void ModifyPainting()
        {
            if (IsModified) return;
            IsModified = true;
            OnModified?.Invoke();
            // TODO: 切換畫的視覺狀態
            Debug.Log($"[Painting] {gameObject.name} 已被改寫。");
        }

        /// <summary>五芒星重新開始時恢復原始狀態。</summary>
        public void ResetPainting()
        {
            IsModified = false;
            // TODO: 恢復畫的視覺狀態
        }
    }
}