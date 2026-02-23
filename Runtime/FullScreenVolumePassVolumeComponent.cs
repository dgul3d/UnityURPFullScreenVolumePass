using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FullScreenVolumePass
{
    [Serializable]
    public enum FullScreenInjectionPoint
    {
        BeforePostProcessing = 0,
        AfterPostProcessing = 1,
    }

    [Serializable]
    public sealed class MaterialParameter : VolumeParameter<Material>
    {
        public MaterialParameter(Material value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class FullScreenInjectionPointParameter : VolumeParameter<FullScreenInjectionPoint>
    {
        public FullScreenInjectionPointParameter(FullScreenInjectionPoint value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ScriptableRenderPassInputParameter : VolumeParameter<ScriptableRenderPassInput>
    {
        public ScriptableRenderPassInputParameter(ScriptableRenderPassInput value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Custom fullscreen volume pass")]
    public class FullScreenVolumePassVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Enable or disable the effect.")]
        public BoolParameter isEnabled = new BoolParameter(true);

        [Tooltip("Intensity of the effect is controlled through the \"_Intensity\" property in the assigned material.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("Specifies at which injection point the pass will be rendered.")]
        public FullScreenInjectionPointParameter injectionPoint =
            new FullScreenInjectionPointParameter(FullScreenInjectionPoint.BeforePostProcessing);

        [Tooltip("A mask of URP textures that the assigned material will need access to. Requesting unused requirements can degrade performance unnecessarily as URP might need to run additional rendering passes to generate them.")]
        public ScriptableRenderPassInputParameter requirements = new ScriptableRenderPassInputParameter(ScriptableRenderPassInput.None);

        [Tooltip("Specifies whether the assigned material will need to use the current screen contents as an input texture. Disable this to optimize away an extra color copy pass when you know that the assigned material will only need to write on top of or hardware blend with the contents of the active color target.")]
        public BoolParameter fetchColorBuffer = new BoolParameter(true);

        [Tooltip("Specifies if the active camera's depth-stencil buffer should be bound when rendering the full screen pass. Disabling this will ensure that the material's depth and stencil commands will have no effect (this could also have a slight performance benefit).")]
        public BoolParameter bindDepthStencil = new BoolParameter(false);

        [Tooltip("The material used to render the full screen pass (typically based on the Fullscreen Shader Graph target).")]
        public MaterialParameter passMaterial = new MaterialParameter(null);

        [Tooltip("The shader pass index that should be used when rendering the assigned material.")]
        public IntParameter passIndex = new IntParameter(0);

        public bool IsActive() => isEnabled.value && intensity.value > 0f && passMaterial.value != null;

        public bool IsTileCompatible() => false;
    }
}