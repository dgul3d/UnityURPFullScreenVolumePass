using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using System;
using System.Collections.Generic;

namespace FullScreenVolumePass
{
    public class FullScreenVolumePass : ScriptableRenderPass
    {
        private static readonly int BLIT_TEXTURE_PROPERTY_ID = Shader.PropertyToID("_BlitTexture");
        private static readonly int BLIT_SCALE_BIAS_PROPERTY_ID = Shader.PropertyToID("_BlitScaleBias");
        private static readonly Vector4 BLIT_SCALE_BIAS = new Vector4(1f, 1f, 0f, 0f);
        private static readonly MaterialPropertyBlock SHARED_PROPERTY_BLOCK = new MaterialPropertyBlock();

        private struct ActiveEffectData
        {
            public IFullscreenEffectModule module;
            public FullScreenVolumePassVolumeComponentSettings settings;
        }

        private static readonly Comparison<ActiveEffectData> ACTIVE_EFFECT_SORT_COMPARISON = CompareActiveEffects;

        private readonly List<ActiveEffectData> m_ActiveEffects = new List<ActiveEffectData>();
        private readonly List<FullScreenVolumePassVolumeComponentSettings> m_CollectedSettings = new List<FullScreenVolumePassVolumeComponentSettings>();
        private readonly Dictionary<Material, FullScreenVolumePassVolumeComponentSettings> m_FirstSettingsByMaterial = new Dictionary<Material, FullScreenVolumePassVolumeComponentSettings>();
        private readonly HashSet<Material> m_LoggedMaterialConflicts = new HashSet<Material>();
        private readonly RenderPassEvent m_TargetInjectionPoint;
        private bool m_RequiresColorFetch;
        private ScriptableRenderPassInput m_CombinedRequirements;

        private class PassData
        {
            public TextureHandle src;

            public IFullscreenEffectModule module;
            public FullScreenVolumePassVolumeComponentSettings settings;
            public Material material;
            public int passIndex;
            public bool bindDepthStencil;
        }

        public FullScreenVolumePass(RenderPassEvent targetInjectionPoint)
        {
            m_TargetInjectionPoint = targetInjectionPoint;
            renderPassEvent = targetInjectionPoint;
            requiresIntermediateTexture = false;
        }

        public bool PrepareForCamera(in RenderingData renderingData)
        {
            m_ActiveEffects.Clear();
            m_FirstSettingsByMaterial.Clear();
            m_LoggedMaterialConflicts.Clear();
            m_RequiresColorFetch = false;
            m_CombinedRequirements = ScriptableRenderPassInput.None;

            IFullscreenEffectModule[] modules = FullscreenVolumePassRegistry.Modules;
            CameraData cameraData = renderingData.cameraData;

            for (int i = 0; i < modules.Length; i++)
            {
                m_CollectedSettings.Clear();
                modules[i].CollectSettings(cameraData, m_CollectedSettings);

                for (int settingIndex = 0; settingIndex < m_CollectedSettings.Count; settingIndex++)
                {
                    FullScreenVolumePassVolumeComponentSettings settings = m_CollectedSettings[settingIndex];
                    if (settings.injectionPoint != m_TargetInjectionPoint)
                    {
                        continue;
                    }

                    ValidateMaterialReuse(settings);
                    m_RequiresColorFetch |= settings.fetchColorBuffer;
                    m_CombinedRequirements |= settings.requirements;

                    ActiveEffectData activeEffect;
                    activeEffect.module = modules[i];
                    activeEffect.settings = settings;
                    m_ActiveEffects.Add(activeEffect);
                }
            }

            if (m_ActiveEffects.Count == 0)
            {
                return false;
            }

            ConfigureInput(m_CombinedRequirements);
            bool forceIntermediateForPostComposition =
                !m_RequiresColorFetch &&
                renderingData.cameraData.postProcessEnabled;

            requiresIntermediateTexture = m_RequiresColorFetch || forceIntermediateForPostComposition;
            m_ActiveEffects.Sort(ACTIVE_EFFECT_SORT_COMPARISON);
            return true;
        }

        public void Dispose() { }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_ActiveEffects.Count == 0)
                return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            // 1. Get the current active color texture (Read-Only reference)
            TextureHandle activeColor = resourceData.activeColorTexture;

            if (!activeColor.IsValid())
                return;

            if (m_RequiresColorFetch && resourceData.isActiveTargetBackBuffer)
                return;

            // 2. Create a temporary copy texture
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // We only need the color buffer
            desc.msaaSamples = 1;
            desc.bindMS = false;
            
            TextureHandle sourceCopyTexture = TextureHandle.nullHandle;
            if (m_RequiresColorFetch)
            {
                //TODO: rework copying
                sourceCopyTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    desc,
                    "MyFullscreen_SourceCopy",
                    false
                );
            }

            for (int index = 0; index < m_ActiveEffects.Count; index++)
            {
                ActiveEffectData activeEffect = m_ActiveEffects[index];
                FullScreenVolumePassVolumeComponentSettings settings = activeEffect.settings;
                Material material = settings.passMaterial;

                if (settings.fetchColorBuffer)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>($"Copy Active Color {index}", out var copyPassData))
                    {
                        copyPassData.src = activeColor;

                        builder.UseTexture(activeColor, AccessFlags.Read);
                        builder.SetRenderAttachment(sourceCopyTexture, 0, AccessFlags.Write);

                        builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                        {
                            Blitter.BlitTexture(context.cmd, data.src, new Vector4(1, 1, 0, 0), 0.0f, false);
                        });
                    }
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"Apply Fullscreen Effect {index}", out var passData))
                {
                    passData.src = settings.fetchColorBuffer ? sourceCopyTexture : TextureHandle.nullHandle;
                    passData.module = activeEffect.module;
                    passData.settings = settings;
                    passData.material = material;
                    passData.passIndex = settings.passIndex;
                    passData.bindDepthStencil = settings.bindDepthStencil;

                    if (settings.fetchColorBuffer)
                    {
                        builder.UseTexture(sourceCopyTexture, AccessFlags.Read);
                    }

                    bool needsColor = (settings.requirements & ScriptableRenderPassInput.Color) != ScriptableRenderPassInput.None;
                    bool needsDepth = (settings.requirements & ScriptableRenderPassInput.Depth) != ScriptableRenderPassInput.None;
                    bool needsMotion = (settings.requirements & ScriptableRenderPassInput.Motion) != ScriptableRenderPassInput.None;
                    bool needsNormal = (settings.requirements & ScriptableRenderPassInput.Normal) != ScriptableRenderPassInput.None;

                    if (needsColor)
                    {
                        if (resourceData.cameraOpaqueTexture.IsValid())
                        {
                            builder.UseTexture(resourceData.cameraOpaqueTexture);
                        }
                    }

                    if (needsDepth && resourceData.cameraDepthTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraDepthTexture);
                    }

                    if (needsMotion)
                    {
                        if (resourceData.motionVectorColor.IsValid())
                        {
                            builder.UseTexture(resourceData.motionVectorColor);
                        }

                        if (resourceData.motionVectorDepth.IsValid())
                        {
                            builder.UseTexture(resourceData.motionVectorDepth);
                        }
                    }

                    if (needsNormal && resourceData.cameraNormalsTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraNormalsTexture);
                    }

                    builder.SetRenderAttachment(activeColor, 0, AccessFlags.Write);

                    if (passData.bindDepthStencil && resourceData.activeDepthTexture.IsValid())
                    {
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                    }

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        ExecuteMainPass(context.cmd, data.src, data.material, data.passIndex, data.module, data.settings);
                    });
                }
            }
        }

        private static int CompareActiveEffects(ActiveEffectData left, ActiveEffectData right)
        {
            return left.settings.sortingPriority.CompareTo(right.settings.sortingPriority);
        }

        private static void ExecuteMainPass(
            RasterCommandBuffer commandBuffer,
            RTHandle sourceTexture,
            Material material,
            int materialPassIndex,
            IFullscreenEffectModule module,
            in FullScreenVolumePassVolumeComponentSettings settings)
        {
            SHARED_PROPERTY_BLOCK.Clear();
            if (sourceTexture != null)
            {
                SHARED_PROPERTY_BLOCK.SetTexture(BLIT_TEXTURE_PROPERTY_ID, sourceTexture);
            }

            SHARED_PROPERTY_BLOCK.SetVector(BLIT_SCALE_BIAS_PROPERTY_ID, BLIT_SCALE_BIAS);
            module.ApplyMaterialProperties(settings, SHARED_PROPERTY_BLOCK);

            commandBuffer.DrawProcedural(
                Matrix4x4.identity,
                material,
                materialPassIndex,
                MeshTopology.Triangles,
                3,
                1,
                SHARED_PROPERTY_BLOCK);
        }

        private void ValidateMaterialReuse(in FullScreenVolumePassVolumeComponentSettings settings)
        {
            Material material = settings.passMaterial;
            if (material == null)
            {
                return;
            }

            if (!m_FirstSettingsByMaterial.TryGetValue(material, out FullScreenVolumePassVolumeComponentSettings firstSettings))
            {
                m_FirstSettingsByMaterial.Add(material, settings);
                return;
            }

            bool hasDifferentSourceProfile = firstSettings.sourceProfile != settings.sourceProfile;
            bool hasDifferentSourceVolume = firstSettings.sourceVolume != settings.sourceVolume;
            if ((!hasDifferentSourceProfile && !hasDifferentSourceVolume) || m_LoggedMaterialConflicts.Contains(material))
            {
                return;
            }

            m_LoggedMaterialConflicts.Add(material);

            string firstVolumeName = firstSettings.sourceVolume != null ? firstSettings.sourceVolume.name : "<none>";
            string firstProfileName = firstSettings.sourceProfile != null ? firstSettings.sourceProfile.name : "<none>";
            string currentVolumeName = settings.sourceVolume != null ? settings.sourceVolume.name : "<none>";
            string currentProfileName = settings.sourceProfile != null ? settings.sourceProfile.name : "<none>";

            Debug.LogError(
                $"MyFullscreenRenderPass detected shared material '{material.name}' across different volume sources. " +
                $"First source: Volume='{firstVolumeName}', Profile='{firstProfileName}'. " +
                $"Current source: Volume='{currentVolumeName}', Profile='{currentProfileName}'. " +
                "This setup is intentional only if you accept cross-effect material state coupling.");
        }
    }
}