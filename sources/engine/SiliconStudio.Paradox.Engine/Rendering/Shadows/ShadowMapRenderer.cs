// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;

using SiliconStudio.Core;
using SiliconStudio.Core.Collections;
using SiliconStudio.Core.Extensions;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Paradox.Engine;
using SiliconStudio.Paradox.Graphics;
using SiliconStudio.Paradox.Rendering.Lights;
using SiliconStudio.Paradox.Shaders;

namespace SiliconStudio.Paradox.Rendering.Shadows
{
    /// <summary>
    /// Handles rendering of shadow map casters.
    /// </summary>
    public class ShadowMapRenderer : EntityComponentRendererCoreBase
    {
        // TODO: Extract a common interface and implem for shadow renderer (not only shadow maps)

        private FastListStruct<ShadowMapAtlasTexture> atlases;

        private PoolListStruct<LightShadowMapTexture> shadowMapTextures;

        private readonly int MaximumTextureSize = (int)(MaximumShadowSize * ComputeSizeFactor(LightShadowImportance.High, LightShadowMapSize.Large) * 2.0f);

        private readonly static PropertyKey<ShadowMapRenderer> Current = new PropertyKey<ShadowMapRenderer>("ShadowMapRenderer.Current", typeof(ShadowMapRenderer));

        private const float MaximumShadowSize = 1024;

        internal static readonly ParameterKey<ShadowMapReceiverInfo[]> Receivers = ParameterKeys.New(new ShadowMapReceiverInfo[1]);
        internal static readonly ParameterKey<ShadowMapReceiverVsmInfo[]> ReceiversVsm = ParameterKeys.New(new ShadowMapReceiverVsmInfo[1]);
        internal static readonly ParameterKey<ShadowMapCascadeLevel[]> LevelReceivers = ParameterKeys.New(new ShadowMapCascadeLevel[1]);
        internal static readonly ParameterKey<int> ShadowMapLightCount = ParameterKeys.New(0);

        /// <summary>
        /// The shadow map caster extension a discard extension
        /// </summary>
        private static readonly ShaderMixinGeneratorSource ShadowMapCasterExtension = new ShaderMixinGeneratorSource("ShadowMapCaster") { Discard = true };

        // rectangles to blur for each shadow map
        private HashSet<LightShadowMapTexture> shadowMapTexturesToBlur = new HashSet<LightShadowMapTexture>();

        private readonly Entity cameraEntity;

        private readonly ModelComponentRenderer modelRenderer;

        private readonly string effectName;

        private readonly ModelComponentRenderer shadowModelComponentRenderer;

        private readonly RenderItemCollection opaqueRenderItems;

        private readonly RenderItemCollection transparentRenderItems;

        private readonly Dictionary<Type, ILightShadowMapRenderer> renderers;

        private readonly ParameterCollection shadowCasterParameters;

        public readonly Dictionary<LightComponent, LightShadowMapTexture> LightComponentsWithShadows;

        private readonly Dictionary<ShaderGroupDataKey, ILightShadowMapShaderGroupData> shaderGroupDatas;

        private List<LightComponent> visibleLights;

        private RasterizerState shadowRasterizerState;

        public ShadowMapRenderer(string effectName)
        {
            if (effectName == null) throw new ArgumentNullException("effectName");
            this.effectName = effectName;
            atlases = new FastListStruct<ShadowMapAtlasTexture>(16);
            shadowMapTextures = new PoolListStruct<LightShadowMapTexture>(16, CreateLightShadowMapTexture);
            LightComponentsWithShadows = new Dictionary<LightComponent, LightShadowMapTexture>(16);

            opaqueRenderItems = new RenderItemCollection(512, false);
            transparentRenderItems = new RenderItemCollection(512, true);

            renderers = new Dictionary<Type, ILightShadowMapRenderer>();

            ShadowCamera = new CameraComponent { UseCustomViewMatrix = true, UseCustomProjectionMatrix = true };

            // Creates a model renderer for the shadows casters
            shadowModelComponentRenderer = new ModelComponentRenderer(effectName + ".ShadowMapCaster")
            {
                Callbacks =
                {
                    UpdateMeshes = FilterCasters,
                }
            };

            shadowCasterParameters = new ParameterCollection();
            shadowCasterParameters.Set(ParadoxEffectBaseKeys.ExtensionPostVertexStageShader, ShadowMapCasterExtension);
        }

        /// <summary>
        /// Gets or sets the camera.
        /// </summary>
        /// <value>The camera.</value>
        public CameraComponent Camera { get; private set; }

        /// <summary>
        /// The shadow camera used for rendering from the shadow space.
        /// </summary>
        public readonly CameraComponent ShadowCamera;


        public Dictionary<Type, ILightShadowMapRenderer> Renderers
        {
            get
            {
                return renderers;
            }
        }

        public ILightShadowMapRenderer FindRenderer(Type lightType)
        {
            ILightShadowMapRenderer shadowMapRenderer;
            renderers.TryGetValue(lightType, out shadowMapRenderer);
            return shadowMapRenderer;
        }

        public void Attach(ModelComponentRenderer modelRenderer)
        {
            // TODO: Add logic to plug shadow mapping into 

        }

        /// <summary>
        /// Draws the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="visibleLights">The visible lights.</param>
        public void Draw(RenderContext context, List<LightComponent> visibleLights)
        {
            var current = context.Tags.Get(Current);
            if (current != null)
            {
                return;
            }

            this.visibleLights = visibleLights;

            using (context.PushTagAndRestore(Current, this))
            {
                PreDrawCoreInternal(context);
                DrawCore(context);
                PostDrawCoreInternal(context);
            }
        }

        public void RenderCasters(RenderContext context, EntityGroupMask cullingMask)
        {
            context.GraphicsDevice.PushState();
            context.PushParameters(shadowCasterParameters);
            try
            {
                CameraComponentRenderer.UpdateParameters(context, ShadowCamera);

                opaqueRenderItems.Clear();
                transparentRenderItems.Clear();

                // When rendering shadow maps, objects should not be culled by the rasterizer (in case the object is out of the frustum but cast
                // a shadow into the frustum)
                shadowModelComponentRenderer.RasterizerState = shadowRasterizerState;

                // We should not cull models in the view frustum, as objects can be outside the frustum and cast shadows
                // TODO: We need at some point to be perform shadow culling based on the frustum of the shadow view
                shadowModelComponentRenderer.CullingModeOverride = CullingMode.None;
                shadowModelComponentRenderer.CurrentCullingMask = cullingMask;
                shadowModelComponentRenderer.Prepare(context, opaqueRenderItems, transparentRenderItems);
                shadowModelComponentRenderer.Draw(context, opaqueRenderItems, 0, opaqueRenderItems.Count - 1);
            }
            finally
            {
                context.PopParameters();
                context.GraphicsDevice.PopState();
            }
        }

        protected override void InitializeCore()
        {
            base.InitializeCore();

            var shadowRenderState = new RasterizerStateDescription(CullMode.None) { DepthClipEnable = false };
            shadowRasterizerState = RasterizerState.New(Context.GraphicsDevice, shadowRenderState);
        }

        protected void DrawCore(RenderContext context)
        {
            // We must be running inside the context of 
            var sceneInstance = SceneInstance.GetCurrent(context);
            if (sceneInstance == null)
            {
                throw new InvalidOperationException("ShadowMapRenderer expects to be used inside the context of a SceneInstance.Draw()");
            }

            // Gets the current camera
            Camera = context.GetCurrentCamera();
            if (Camera == null)
            {
                return;
            }

            // Clear currently associated shadows
            shadowMapTextures.Clear();
            LightComponentsWithShadows.Clear();

            // Collect all required shadow maps
            CollectShadowMaps();

            // No shadow maps to render
            if (shadowMapTextures.Count == 0)
            {
                return;
            }

            // Assign rectangles to shadow maps
            AssignRectangles();

            // Reset the state of renderers
            foreach (var rendererKeyPairs in renderers)
            {
                var renderer = rendererKeyPairs.Value;
                renderer.Reset();
            }

            // Prepare and render shadow maps
            foreach (var shadowMapTexture in shadowMapTextures)
            {
                shadowMapTexture.Renderer.Render(context, this, shadowMapTexture);
            }
        }

        private void AssignRectangles()
        {
            // Clear atlases
            foreach (var atlas in atlases)
            {
                atlas.Clear();
            }

            // Assign rectangles for shadowmaps
            foreach (var shadowMapTexture in shadowMapTextures)
            {
                AssignRectangles(shadowMapTexture);
            }
        }

        private void AssignRectangles(LightShadowMapTexture lightShadowMapTexture)
        {
            lightShadowMapTexture.CascadeCount = lightShadowMapTexture.Shadow.GetCascadeCount();
            var size = lightShadowMapTexture.Size;

            // Try to fit the shadow map into an existing atlas
            ShadowMapAtlasTexture currentAtlas = null;
            foreach (var atlas in atlases)
            {
                if (atlas.TryInsert(size, size, lightShadowMapTexture.CascadeCount, (int index, ref Rectangle rectangle) => lightShadowMapTexture.SetRectangle(index, rectangle)))
                {
                    currentAtlas = atlas;
                    break;
                }
            }

            // Allocate a new atlas texture
            if (currentAtlas == null)
            {
                // TODO: handle FilterType texture creation here
                // TODO: This does not work for Omni lights

                var texture = Texture.New2D(Context.GraphicsDevice, MaximumTextureSize, MaximumTextureSize, 1, PixelFormat.D32_Float, TextureFlags.DepthStencil | TextureFlags.ShaderResource);
                currentAtlas = new ShadowMapAtlasTexture(texture, atlases.Count) { FilterType = lightShadowMapTexture.FilterType };
                atlases.Add(currentAtlas);

                for (int i = 0; i < lightShadowMapTexture.CascadeCount; i++)
                {
                    var rect = Rectangle.Empty;
                    currentAtlas.Insert(size, size, ref rect);
                    lightShadowMapTexture.SetRectangle(i, rect);
                }
            }

            // Make sure the atlas cleared (will be clear just once)
            currentAtlas.ClearRenderTarget(Context);
            lightShadowMapTexture.TextureId = (byte)currentAtlas.Id;
            lightShadowMapTexture.Atlas = currentAtlas;
        }

        private void CollectShadowMaps()
        {
            foreach (var lightComponent in visibleLights)
            {
                var light = lightComponent.Type as IDirectLight;
                if (light == null)
                {
                    continue;
                }

                var shadowMap = light.Shadow;
                if (!shadowMap.Enabled)
                {
                    continue;
                }

                // Check if the light has a shadow map renderer
                var lightType = light.GetType();
                ILightShadowMapRenderer renderer;
                if (!renderers.TryGetValue(lightType, out renderer))
                {
                    continue;
                }

                var direction = lightComponent.Direction;
                var position = lightComponent.Position;

                // Compute the coverage of this light on the screen
                var size = light.ComputeScreenCoverage(Context, position, direction);

                // Converts the importance into a shadow size factor
                var sizeFactor = ComputeSizeFactor(shadowMap.Importance, shadowMap.Size);

                // Compute the size of the final shadow map
                // TODO: Handle GraphicsProfile
                var shadowMapSize = (int)Math.Min(MaximumShadowSize * sizeFactor, MathUtil.NextPowerOfTwo(size * sizeFactor));

                if (shadowMapSize <= 0) // TODO: Validate < 0 earlier in the setters
                {
                    continue;
                }

                // Get or allocate  a ShadowMapTexture
                var shadowMapTexture = shadowMapTextures.Add();
                shadowMapTexture.Initialize(lightComponent, light, shadowMap, shadowMapSize, renderer);
                LightComponentsWithShadows.Add(lightComponent, shadowMapTexture);
            }
        }

        private static float ComputeSizeFactor(LightShadowImportance importance, LightShadowMapSize shadowMapSize)
        {
            // Calculate a basic factor from the importance of this shadow map
            var factor = importance == LightShadowImportance.High ? 2.0f : importance == LightShadowImportance.Medium ? 1.0f : 0.5f;

            // Then reduce the size based on the shadow map size
            factor *= (float)Math.Pow(2.0f, (int)shadowMapSize - 2.0f);
            return factor;
        }

        private static void FilterCasters(RenderContext context, FastList<RenderMesh> meshes)
        {
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                // If there is no caster
                if (!mesh.IsShadowCaster)
                {
                    meshes.SwapRemoveAt(i--);
                }
            }
        }

        private static LightShadowMapTexture CreateLightShadowMapTexture()
        {
            return new LightShadowMapTexture();
        }

        struct ShaderGroupDataKey : IEquatable<ShaderGroupDataKey>
        {
            public readonly Texture Texture;

            public readonly ILightShadowMapRenderer Renderer;

            public readonly int CascadeCount;

            public readonly int LightMaxCount;

            public ShaderGroupDataKey(Texture texture, ILightShadowMapRenderer renderer, int cascadeCount, int lightMaxCount)
            {
                Texture = texture;
                Renderer = renderer;
                CascadeCount = cascadeCount;
                LightMaxCount = lightMaxCount;
            }

            public bool Equals(ShaderGroupDataKey other)
            {
                return Texture.Equals(other.Texture) && Renderer.Equals(other.Renderer) && CascadeCount == other.CascadeCount && LightMaxCount == other.LightMaxCount;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is ShaderGroupDataKey && Equals((ShaderGroupDataKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Texture.GetHashCode();
                    hashCode = (hashCode * 397) ^ Renderer.GetHashCode();
                    hashCode = (hashCode * 397) ^ CascadeCount;
                    hashCode = (hashCode * 397) ^ LightMaxCount;
                    return hashCode;
                }
            }

            public static bool operator ==(ShaderGroupDataKey left, ShaderGroupDataKey right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(ShaderGroupDataKey left, ShaderGroupDataKey right)
            {
                return !left.Equals(right);
            }
        }
    }
}