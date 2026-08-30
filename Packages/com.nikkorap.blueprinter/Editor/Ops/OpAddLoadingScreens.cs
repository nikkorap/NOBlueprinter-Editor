using System;
using UnityEngine;

namespace Blueprinter.Editor.Ops
{
    [CreateAssetMenu(menuName = "Blueprinter/OpAddLoadingScreens", fileName = "OpAddLoadingScreens")]
    public class OpAddLoadingScreens : OpCore
    {
        [Tooltip("Loading screen images")]
        public Sprite[] images = Array.Empty<Sprite>();

        [Serializable]
        private class PayloadDto
        {
            public AssetRef[] imagesAssets;
        }

        public override string opId => "OpAddLoadingScreens";
        public override bool IsEmpty => images == null || images.Length == 0;

        public override object BuildPayload()
        {
            var imageAssets = RequireModAssets(images);
            if (imageAssets == null)
                return null;

            return new PayloadDto
            {
                imagesAssets = imageAssets
            };
        }
    }
}
