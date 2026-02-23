using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FullScreenVolumePass
{
    public class FullScreenVolumePassRendererFeature : ScriptableRendererFeature
    {
        private FullScreenVolumePass m_BeforePostProcessingPass;
        private FullScreenVolumePass m_AfterPostProcessingPass;

        public override void Create()
        {
            m_BeforePostProcessingPass = new FullScreenVolumePass(RenderPassEvent.BeforeRenderingPostProcessing);
            m_AfterPostProcessingPass = new FullScreenVolumePass(RenderPassEvent.AfterRenderingPostProcessing);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.renderType != CameraRenderType.Base)
            {
                return;
            }

            if (renderingData.cameraData.cameraType != CameraType.Preview)
            {
                if (m_BeforePostProcessingPass != null && m_BeforePostProcessingPass.PrepareForCamera(renderingData))
                {
                    renderer.EnqueuePass(m_BeforePostProcessingPass);
                }

                if (m_AfterPostProcessingPass != null && m_AfterPostProcessingPass.PrepareForCamera(renderingData))
                {
                    renderer.EnqueuePass(m_AfterPostProcessingPass);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_BeforePostProcessingPass?.Dispose();
            m_AfterPostProcessingPass?.Dispose();

            m_BeforePostProcessingPass = null;
            m_AfterPostProcessingPass = null;

            base.Dispose(disposing);
        }
    }
}