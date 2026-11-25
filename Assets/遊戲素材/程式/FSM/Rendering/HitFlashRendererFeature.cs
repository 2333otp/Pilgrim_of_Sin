using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 受擊暖色光暈的渲染步驟（加在 PC_Renderer / Mobile_Renderer 上）。
    /// 把「渲染層」帶有 HitFlashLayer 這一位的模型，用 HitFlashGlow 材質再畫一次（疊加暖光）。
    /// HitFeedback 打中時把目標的渲染層打開這一位、結束時關掉；平常沒有模型帶這一位，這一步幾乎沒有成本。
    /// 多材質槽的模型（身體＋臉）每一槽都會被畫到。
    /// </summary>
    public class HitFlashRendererFeature : ScriptableRendererFeature
    {
        /// <summary>受擊光暈用的渲染層位元（第 31 層，一般不會被燈光圖層等功能用到）。</summary>
        public const uint HitFlashLayer = 1u << 30;

        [SerializeField] private Material _glowMaterial;
        [SerializeField] private RenderPassEvent _event = RenderPassEvent.AfterRenderingOpaques;

        private GlowPass _pass;

        public override void Create()
        {
            _pass = new GlowPass { renderPassEvent = _event };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_glowMaterial == null) return;
            var type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            _pass.Material = _glowMaterial;
            renderer.EnqueuePass(_pass);
        }

        private class GlowPass : ScriptableRenderPass
        {
            public Material Material;

            private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
            };

            private class PassData
            {
                public RendererListHandle list;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Hit Flash Glow", out var passData);

                var drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData,
                                                                   SortingCriteria.CommonOpaque);
                drawing.overrideMaterial = Material;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, HitFlashLayer);

                passData.list = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, drawing, filtering));

                builder.UseRendererList(passData.list);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(data.list));
            }
        }
    }
}
