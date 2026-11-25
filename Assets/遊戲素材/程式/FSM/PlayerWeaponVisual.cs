using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 依 PlayerController.OnWeaponSwitched 動態把武器模型掛到角色手骨（hand_attach）上。
    /// 索引對應：1鉛筆(右手) 2畫筆(右手) 3畫刀(雙手各一把) 4調色盤(左手)。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerWeaponVisual : MonoBehaviour
    {
        [Header("武器模型（索引對應 CurrentWeaponIndex）")]
        [SerializeField] private GameObject _pencilPrefab;
        [SerializeField] private GameObject _brushPrefab;
        [SerializeField] private GameObject _paintKnifePrefab;
        [SerializeField] private GameObject _palettePrefab;

        [Header("手部掛點骨骼名稱")]
        [SerializeField] private string _handAttachLeftBoneName = "DEF-hand_attach.L";
        [SerializeField] private string _handAttachRightBoneName = "DEF-hand_attach.R";

        // 部分武器模型的建模軸向跟手骨朝向不一致，掛上去角度會歪，靠這幾個欄位在 Inspector 微調到對齊為止。
        [Header("各武器軸向校正（掛上後方向不對時在這裡調）")]
        [SerializeField] private Vector3 _pencilRotationOffset;
        [SerializeField] private Vector3 _brushRotationOffset;
        [SerializeField] private Vector3 _paintKnifeRotationOffsetRight;
        [SerializeField] private Vector3 _paintKnifeRotationOffsetLeft;
        [SerializeField] private Vector3 _paletteRotationOffset;

        // 握把位置：模型裡「要握在手上」的那一點（模型自己的座標）。掛上去時把這一點對齊到手骨。
        // 畫刀的 FBX 原點在刀尖下方約 0.4 m、握把在 Y ≈ 0.83~0.98，直接掛原點的話整把刀會離手 0.4~1 m。
        [Header("各武器握把位置（模型座標；這一點會對齊到手骨，0 = 模型原點）")]
        [SerializeField] private Vector3 _pencilGripPoint;
        [SerializeField] private Vector3 _brushGripPoint;
        [SerializeField] private Vector3 _paintKnifeGripPoint = new Vector3(0f, 0.91f, 0f);
        [SerializeField] private Vector3 _paletteGripPoint;

        // DEF-hand_attach 是掛在「前臂」底下的骨頭，動畫裡沒有它的曲線（Blender 裡跟著手的約束沒有匯出），
        // 所以它不會跟著手腕轉：手腕一轉，武器跟拳頭的角度就差到 100° 以上。
        // 勾選的武器改掛在「手腕錨點」上：開場時（骨架還在綁定姿勢）記下 hand_attach 相對手骨的位置，
        // 在手骨底下建一個同位置的錨點，武器跟著手腕一起轉，等於還原 Blender 裡的約束。
        [Header("跟著手腕轉（hand_attach 不會跟手腕轉；勾選 = 改掛在手骨上的同位置錨點）")]
        [SerializeField] private bool _pencilFollowWrist;
        [SerializeField] private bool _brushFollowWrist;
        [SerializeField] private bool _paintKnifeFollowWrist = true;
        [SerializeField] private bool _paletteFollowWrist;

        private PlayerController _player;
        private Transform _handAttachL;
        private Transform _handAttachR;
        private Transform _wristAnchorL;
        private Transform _wristAnchorR;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>目前掛在右手／左手的武器模型（沒有就 null）。攻擊判定的「手」掛點以武器模型的方向為準。</summary>
        public Transform RightWeapon { get; private set; }
        public Transform LeftWeapon { get; private set; }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();

            var root = GetComponent<Animator>().transform;
            _handAttachL = FindDescendant(root, _handAttachLeftBoneName);
            _handAttachR = FindDescendant(root, _handAttachRightBoneName);

            if (_handAttachL == null)
                Debug.LogWarning($"[PlayerWeaponVisual] 找不到骨頭 {_handAttachLeftBoneName}", this);
            if (_handAttachR == null)
                Debug.LogWarning($"[PlayerWeaponVisual] 找不到骨頭 {_handAttachRightBoneName}", this);

            // Awake 時 Animator 還沒跑，骨架是綁定姿勢：這時候記下 hand_attach 相對手骨的位置
            var anim = GetComponent<Animator>();
            _wristAnchorR = CreateWristAnchor(anim, _handAttachR, true);
            _wristAnchorL = CreateWristAnchor(anim, _handAttachL, false);
        }

        /// <summary>
        /// 在手骨底下建一個錨點，位置／方向 = 現在（綁定姿勢下）hand_attach 相對手骨的位置。
        /// 之後它跟著手腕轉，綁定姿勢時跟 hand_attach 完全重合。
        /// </summary>
        public static Transform CreateWristAnchor(Animator anim, Transform attach, bool right)
        {
            if (anim == null || attach == null || !anim.isHuman) return null;
            var hand = anim.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (hand == null) return null;
            var go = new GameObject(attach.name + " (WristAnchor)");
            go.hideFlags = attach.gameObject.hideFlags;
            var t = go.transform;
            t.SetParent(hand, false);
            t.localPosition = hand.InverseTransformPoint(attach.position);
            t.localRotation = Quaternion.Inverse(hand.rotation) * attach.rotation;
            t.localScale = Vector3.one;
            return t;
        }

        private Transform Mount(bool right, bool followWrist)
        {
            if (followWrist)
            {
                var anchor = right ? _wristAnchorR : _wristAnchorL;
                if (anchor != null) return anchor;
            }
            return right ? _handAttachR : _handAttachL;
        }

        private void Start()
        {
            if (_player == null) return;
            _player.OnWeaponSwitched += Equip;
            Equip(_player.Combat != null ? _player.Combat.CurrentWeaponIndex : 1);
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.OnWeaponSwitched -= Equip;
        }

        /// <summary>
        /// 不靠 PlayerController 直接換武器（給武器介紹頁的展示用 Muir 用；遊戲中照舊走 OnWeaponSwitched）。
        /// 1鉛筆 2畫筆 3畫刀 4調色盤。
        /// </summary>
        public void EquipWeapon(int weaponIndex) => Equip(weaponIndex);

        private void Equip(int weaponIndex)
        {
            ClearSpawned();

            switch (weaponIndex)
            {
                case 1:
                    SpawnOn(Mount(true, _pencilFollowWrist), true, _pencilPrefab, _pencilRotationOffset, _pencilGripPoint);
                    break;
                case 2:
                    SpawnOn(Mount(true, _brushFollowWrist), true, _brushPrefab, _brushRotationOffset, _brushGripPoint);
                    break;
                case 3:
                    // 左右手骨骼的 rest pose 不是完美鏡像（實測驗證過），兩手要各自獨立的軸向校正，不能共用同一個offset。
                    SpawnOn(Mount(true, _paintKnifeFollowWrist), true, _paintKnifePrefab, _paintKnifeRotationOffsetRight, _paintKnifeGripPoint);
                    SpawnOn(Mount(false, _paintKnifeFollowWrist), false, _paintKnifePrefab, _paintKnifeRotationOffsetLeft, _paintKnifeGripPoint);
                    break;
                case 4:
                    SpawnOn(Mount(false, _paletteFollowWrist), false, _palettePrefab, _paletteRotationOffset, _paletteGripPoint);
                    break;
            }
        }

        private void SpawnOn(Transform bone, bool right, GameObject prefab, Vector3 rotationOffset, Vector3 gripPoint)
        {
            var instance = AttachModel(bone, prefab, rotationOffset, gripPoint);
            if (instance == null) return;

            _spawned.Add(instance);
            if (right) RightWeapon = instance.transform;
            else LeftWeapon = instance.transform;
        }

        /// <summary>
        /// 把武器模型掛到手骨上（遊戲中與攻擊判定編輯器的預覽共用，保證兩邊一樣）：
        /// 旋轉 = rotationOffset；位置 = 讓模型的 gripPoint（握把）剛好落在手骨上。
        /// </summary>
        public static GameObject AttachModel(Transform bone, GameObject prefab, Vector3 rotationOffset, Vector3 gripPoint)
        {
            if (bone == null || prefab == null) return null;

            var instance = Instantiate(prefab, bone);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(rotationOffset);

            // rig_deform 骨架本身在匯入時就帶了 100x 的縮放（Blender 匯出的常見換算），
            // hand_attach 骨頭的世界縮放因此被放大了 100 倍。武器模型是照真實世界比例做的，
            // 直接繼承這個縮放會被放大 100 倍，所以這裡用骨頭的 lossyScale 反向抵銷，
            // 讓武器維持原本作者設定好的實際大小。
            var parentLossyScale = bone.lossyScale;
            instance.transform.localScale = new Vector3(
                1f / parentLossyScale.x,
                1f / parentLossyScale.y,
                1f / parentLossyScale.z);

            // 握把對齊手骨：把模型往回移，讓 gripPoint 落在骨頭原點（之後跟著骨頭動，相對位置不變）
            if (gripPoint != Vector3.zero)
                instance.transform.position += bone.position - instance.transform.TransformPoint(gripPoint);

            return instance;
        }

        private void ClearSpawned()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Destroy(_spawned[i]);
            }
            _spawned.Clear();
            RightWeapon = LeftWeapon = null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDescendant(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
