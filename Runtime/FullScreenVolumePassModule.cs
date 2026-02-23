using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace FullScreenVolumePass
{
    public sealed class FullScreenVolumePassModule : IFullscreenEffectModule
        {
        private static readonly int INTENSITY_PROPERTY_ID = Shader.PropertyToID("_Intensity");

        public void CollectSettings(in CameraData cameraData, List<FullScreenVolumePassVolumeComponentSettings> outputSettings)
        {
            Volume[] volumes = VolumeManager.instance.GetVolumes(cameraData.volumeLayerMask);
            if (volumes == null || volumes.Length == 0)
            {
                return;
            }

            Vector3 triggerPosition = cameraData.volumeTrigger != null
                ? cameraData.volumeTrigger.position
                : cameraData.camera.transform.position;

            for (int index = 0; index < volumes.Length; index++)
            {
                Volume volume = volumes[index];
                if (volume == null || !volume.enabled || volume.weight <= 0f || volume.profile == null)
                {
                    continue;
                }

                if (!volume.profile.TryGet(out FullScreenVolumePassVolumeComponent component))
                {
                    continue;
                }

                if (!component.isEnabled.value || component.passMaterial.value == null)
                {
                    continue;
                }

                if (component.passIndex.value < 0 || component.passIndex.value >= component.passMaterial.value.passCount)
                {
                    Debug.LogWarning(
                        $"MyFullscreenEffectVolume on profile '{volume.profile.name}' has pass index {component.passIndex.value} out of bounds " +
                        $"for material '{component.passMaterial.value.name}' (pass count: {component.passMaterial.value.passCount}).");
                    continue;
                }

                float influence = GetVolumeInfluence(volume, triggerPosition);
                float effectiveIntensity = component.intensity.value * influence;
                if (effectiveIntensity <= 0f)
                {
                    continue;
                }

                outputSettings.Add(new FullScreenVolumePassVolumeComponentSettings(
                    ToRenderPassEvent(component.injectionPoint.value),
                    volume.priority,
                    component.passMaterial.value,
                    component.passIndex.value,
                    effectiveIntensity,
                    component.fetchColorBuffer.value,
                    component.requirements.value,
                    component.bindDepthStencil.value,
                    volume,
                    volume.profile));
            }
        }

        public void ApplyMaterialProperties(in FullScreenVolumePassVolumeComponentSettings settings, MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetFloat(INTENSITY_PROPERTY_ID, settings.intensity);
        }

        private static float GetVolumeInfluence(Volume volume, Vector3 triggerPosition)
        {
            float influence = volume.weight;
            if (influence <= 0f)
            {
                return 0f;
            }

            if (volume.isGlobal)
            {
                return influence;
            }

            if (!volume.TryGetComponent(out Collider volumeCollider))
            {
                return 0f;
            }

            float blendDistance = volume.blendDistance;
            if (blendDistance <= 0f)
            {
                bool isInside = volumeCollider.bounds.Contains(triggerPosition);
                return isInside ? influence : 0f;
            }

            float distance = Vector3.Distance(volumeCollider.ClosestPoint(triggerPosition), triggerPosition);
            float localWeight = 1f - Mathf.Clamp01(distance / blendDistance);
            return influence * localWeight;
        }

        private static RenderPassEvent ToRenderPassEvent(FullScreenInjectionPoint injectionPoint)
        {
            return injectionPoint switch
            {
                FullScreenInjectionPoint.AfterPostProcessing => RenderPassEvent.AfterRenderingPostProcessing,
                _ => RenderPassEvent.BeforeRenderingPostProcessing,
            };
        }
    }
}