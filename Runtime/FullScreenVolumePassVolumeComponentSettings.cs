using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace FullScreenVolumePass
{
    public readonly struct FullScreenVolumePassVolumeComponentSettings
    {
        public readonly RenderPassEvent injectionPoint;
        public readonly float sortingPriority;
        public readonly Material passMaterial;
        public readonly int passIndex;
        public readonly float intensity;
        public readonly bool fetchColorBuffer;
        public readonly ScriptableRenderPassInput requirements;
        public readonly bool bindDepthStencil;
        public readonly Volume sourceVolume;
        public readonly VolumeProfile sourceProfile;

        public FullScreenVolumePassVolumeComponentSettings(
            RenderPassEvent injectionPoint,
            float sortingPriority,
            Material passMaterial,
            int passIndex,
            float intensity,
            bool fetchColorBuffer,
            ScriptableRenderPassInput requirements,
            bool bindDepthStencil,
            Volume sourceVolume,
            VolumeProfile sourceProfile)
        {
            this.injectionPoint = injectionPoint;
            this.sortingPriority = sortingPriority;
            this.passMaterial = passMaterial;
            this.passIndex = passIndex;
            this.intensity = intensity;
            this.fetchColorBuffer = fetchColorBuffer;
            this.requirements = requirements;
            this.bindDepthStencil = bindDepthStencil;
            this.sourceVolume = sourceVolume;
            this.sourceProfile = sourceProfile;
        }
    }

    public interface IFullscreenEffectModule
    {
        public void CollectSettings(in CameraData cameraData, List<FullScreenVolumePassVolumeComponentSettings> outputSettings);

        public void ApplyMaterialProperties(in FullScreenVolumePassVolumeComponentSettings settings, MaterialPropertyBlock propertyBlock);
    }
}