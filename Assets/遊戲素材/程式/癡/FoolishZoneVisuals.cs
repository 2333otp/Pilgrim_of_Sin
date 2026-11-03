using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>惡區 / 善區共用的視覺小工具（佔位用，正式特效之後再換）。</summary>
    internal static class FoolishZoneVisuals
    {
        private static readonly Dictionary<PrimitiveType, Mesh> _meshCache = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>取得內建基本形狀的網格（Cube、Cylinder…）。</summary>
        public static Mesh GetMesh(PrimitiveType type)
        {
            if (_meshCache.TryGetValue(type, out var cached) && cached != null) return cached;

            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(temp);
            else Object.DestroyImmediate(temp);

            _meshCache[type] = mesh;
            return mesh;
        }

        /// <summary>用 MaterialPropertyBlock 上色（不會複製或改動共用材質）。URP 與內建管線都支援。</summary>
        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color); // URP Lit
            block.SetColor("_Color", color);     // Built-in
            renderer.SetPropertyBlock(block);
        }
    }
}
