namespace FullScreenVolumePass
{
    public static class FullscreenVolumePassRegistry
    {
        private static readonly IFullscreenEffectModule[] MODULES =
        {
            new FullScreenVolumePassModule(),
        };

        public static IFullscreenEffectModule[] Modules => MODULES;
    }
}
