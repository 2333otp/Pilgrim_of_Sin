using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    public enum HitboxShapeType
    {
        [InspectorName("球")] Sphere,
        [InspectorName("方塊")] Box,
        [InspectorName("膠囊（柱狀）")] Capsule,
    }

    public enum HitboxAnchor
    {
        [InspectorName("主角（跟著身體方向）")] Player,
        [InspectorName("右手武器")] RightHand,
        [InspectorName("左手武器")] LeftHand,
        [InspectorName("雙手（兩個判定）")] BothHands,
    }

    public enum HitboxMotion
    {
        [InspectorName("跟著走")] Follow,
        [InspectorName("往前飛（投擲／射擊）")] Projectile,
    }

    /// <summary>
    /// 攻擊判定的形狀與位置（出招表研究：每把武器、每一招、每一下都可以不一樣）。
    /// 位置／旋轉以「掛點」為基準：主角 = 主角根物件（x 右、y 上、z 前）；
    /// 手 = 手上的武器模型（沿著武器的方向；沒有武器模型時用手骨 DEF-hand_attach）。
    /// 尺寸一律是世界公尺，不受骨架 100 倍縮放影響。
    /// 建議用選單 PilgrimOfSin → 主角 → 攻擊判定編輯器 直接在 Scene 視窗拖拉。
    /// </summary>
    [Serializable]
    public class HitboxShape
    {
        public HitboxShapeType shape = HitboxShapeType.Sphere;
        public HitboxAnchor anchor = HitboxAnchor.Player;

        [Tooltip("位置偏移（公尺，以掛點的方向為準）。")]
        public Vector3 offset = new Vector3(0f, 0.83f, 0.7f);

        [Tooltip("旋轉（以掛點的方向為準）。膠囊沿自己的 Z 軸延伸。")]
        public Vector3 rotation;

        [Tooltip("尺寸（公尺）。球：X = 直徑；方塊：XYZ = 長寬高；膠囊：X = 粗細（直徑）、Z = 長度。")]
        public Vector3 size = new Vector3(1.8f, 1.8f, 1.8f);

        public HitboxMotion motion = HitboxMotion.Follow;

        [Min(0f)]
        [Tooltip("往前飛：命中區間內從起點往主角正前方飛多遠（公尺）。起點 = 開始那一刻的位置。")]
        public float projectileDistance = 6f;

        public HitboxShape Clone() => (HitboxShape)MemberwiseClone();

        // ── 掛點與姿勢計算（遊戲中的判定、F1 顯示、編輯器都用這一套，保證看到的跟打到的一樣） ──

        public static int GetAnchorFrames(HitboxAnchor anchor, Transform player, Transform handR, Transform handL,
                                          List<Pose> frames)
        {
            frames.Clear();
            switch (anchor)
            {
                case HitboxAnchor.RightHand:
                    if (handR != null) frames.Add(new Pose(handR.position, handR.rotation));
                    break;
                case HitboxAnchor.LeftHand:
                    if (handL != null) frames.Add(new Pose(handL.position, handL.rotation));
                    break;
                case HitboxAnchor.BothHands:
                    if (handR != null) frames.Add(new Pose(handR.position, handR.rotation));
                    if (handL != null) frames.Add(new Pose(handL.position, handL.rotation));
                    break;
            }
            // 找不到手骨時退回主角，至少有判定
            if (frames.Count == 0 && player != null) frames.Add(new Pose(player.position, player.rotation));
            return frames.Count;
        }

        /// <summary>掛點座標 → 判定的世界姿勢。</summary>
        public Pose ToWorld(Pose frame)
            => new Pose(frame.position + frame.rotation * offset, frame.rotation * Quaternion.Euler(rotation));

        /// <summary>世界姿勢 → 寫回 offset / rotation（編輯器拖拉用）。</summary>
        public void FromWorld(Pose frame, Vector3 worldPos, Quaternion worldRot)
        {
            var inv = Quaternion.Inverse(frame.rotation);
            offset = inv * (worldPos - frame.position);
            rotation = (inv * worldRot).eulerAngles;
        }

        public float Radius => Mathf.Max(0.01f, size.x * 0.5f);
        public float CapsuleLength => Mathf.Max(size.x, size.z);
    }

    public static class HitboxBones
    {
        public const string RightHandAttach = "DEF-hand_attach.R";
        public const string LeftHandAttach = "DEF-hand_attach.L";

        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = Find(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
