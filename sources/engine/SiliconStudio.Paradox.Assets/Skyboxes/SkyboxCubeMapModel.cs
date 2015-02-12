// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System.Collections.Generic;

using SiliconStudio.Assets;
using SiliconStudio.Core;
using SiliconStudio.Core.Serialization;
using SiliconStudio.Paradox.Assets.Textures;
using SiliconStudio.Paradox.Effects.Skyboxes;
using SiliconStudio.Paradox.Engine.Graphics.Skyboxes;
using SiliconStudio.Paradox.Shaders;

namespace SiliconStudio.Paradox.Assets.Skyboxes
{
    /// <summary>
    /// A cubemap based skybox.
    /// </summary>
    [DataContract("SkyboxCubeMapModel")]
    [Display("Cubemap")]
    public class SkyboxCubeMapModel : ISkyboxModel
    {
        /// <summary>
        /// Gets or sets the cubemap texture.
        /// </summary>
        /// <value>The cubemap texture.</value>
        [DataMember(10)]
        public AssetReference<TextureAsset> CubeMap { get; set; }

        public ShaderSource Generate(SkyboxGeneratorContext context)
        {
            var key = context.GetTextureKey(CubeMap, SkyboxKeys.CubeMap);
            return new ShaderClassSource("ComputeSkyboxCubeMapColor", key);
        }

        public IEnumerable<IContentReference> GetDependencies()
        {
            if (CubeMap != null)
            {
                yield return CubeMap;
            }
        }
    }
}