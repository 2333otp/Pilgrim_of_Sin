using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 跟隨天秤鍊子末端骨頭的世界位置/旋轉，但自己維持乾淨的(1,1,1)縮放。
    /// 骨頭本身因為匯入時有100x縮放（rig_deform坑），直接拿骨頭當parent會讓
    /// 掛在底下的子物件local座標被放大100倍、飛到離譜的地方，所以用這個
    /// 乾淨的中介物件在LateUpdate跟著骨頭的位置+旋轉走，錢袋掛在這個物件底下
    /// 才能既跟著天秤傾斜擺動、又不會被骨頭的縮放污染。
    /// </summary>
    public class ScaleBowlAnchor : MonoBehaviour
    {
        [SerializeField] private Transform _target;          // 要跟隨的骨頭（例如 DEF-chain04_R_end）
        [SerializeField] private Vector3 _worldOffset;        // 固定的世界座標偏移（用來校正到碗底視覺位置）

        // 骨頭的rotation在傾斜動畫中變化幅度很大、姿勢也不直覺（量到的rest pose是奇怪的
        // (0,267,180)），拿它來旋轉offset會讓校正方向隨動畫亂飄，錢袋因此跑到碗底下面去。
        // 改成只跟位置、不跟旋轉，offset固定用世界座標加總，穩定可預期。
        private void LateUpdate()
        {
            if (_target == null) return;
            transform.position = _target.position + _worldOffset;
        }
    }
}
