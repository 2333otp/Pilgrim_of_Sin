#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 除錯用：Play 時每個物理步檢查謬爾的人形骨骼點是否落進「非 Trigger 的實體碰撞體」內部
    /// （天秤 Box、Boss 膠囊、牆…），一發生就在 Console 記下當下的狀態、動畫、骨骼與碰撞體。
    /// 只在 Editor / Development Build 存在，會自動掛到場景裡的 PlayerController 上。
    /// 非凸面 MeshCollider 不支援 ClosestPoint，會被略過。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerPenetrationProbe : MonoBehaviour
    {
        private static readonly HumanBodyBones[] ProbeBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        private const float ReportInterval = 0.5f;
        private const float InsideMargin = 0.03f; // 骨骼點要進到表面內這麼深才算，避免貼邊誤報

        private PlayerController _pc;
        private Animator _animator;
        private Transform[] _bones;
        private readonly Collider[] _hits = new Collider[16];
        private float _nextReport;
        private int _totalReports;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttach()
        {
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!pc.GetComponent<PlayerPenetrationProbe>()) pc.gameObject.AddComponent<PlayerPenetrationProbe>();
        }

        private void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _animator = GetComponent<Animator>();
        }

        private void FixedUpdate()
        {
            if (!_animator || !_animator.isHuman || Time.timeScale == 0f) return;
            EnsureBones();

            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (!bone) continue;
                Vector3 p = bone.position;
                int n = Physics.OverlapSphereNonAlloc(p, 0.05f, _hits, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var c = _hits[h];
                    if (!c || c.transform.IsChildOf(transform)) continue;
                    if (c is MeshCollider mc && !mc.convex) continue;
                    if (c.gameObject.layer == gameObject.layer) continue;

                    // 點在碰撞體內部時 ClosestPoint 會直接回傳該點；再往外推一小段確認確實有「深度」
                    if ((c.ClosestPoint(p) - p).sqrMagnitude > 1e-8f) continue;
                    Vector3 outward = (p - c.bounds.center).normalized;
                    if ((c.ClosestPoint(p + outward * InsideMargin) - (p + outward * InsideMargin)).sqrMagnitude > 1e-8f) continue;

                    Report(ProbeBones[i], p, c);
                    return;
                }
            }
        }

        private void EnsureBones()
        {
            if (_bones != null) return;
            _bones = new Transform[ProbeBones.Length];
            for (int i = 0; i < ProbeBones.Length; i++) _bones[i] = _animator.GetBoneTransform(ProbeBones[i]);
        }

        private void Report(HumanBodyBones bone, Vector3 pos, Collider c)
        {
            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + ReportInterval;
            _totalReports++;

            var info = _animator.GetCurrentAnimatorClipInfo(0);
            string clip = info.Length > 0 ? info[0].clip.name : "?";
            Debug.LogWarning(
                $"[PenetrationProbe #{_totalReports}] 狀態={_pc.CurrentStateType} 動畫={clip} " +
                $"normalizedTime={_animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F2} | " +
                $"骨骼 {bone} @ {pos:F2} 進入 {c.name}({c.GetType().Name}, layer={LayerMask.LayerToName(c.gameObject.layer)}) | " +
                $"玩家 transform={transform.position:F2}", c);
        }
    }
}
#endif
