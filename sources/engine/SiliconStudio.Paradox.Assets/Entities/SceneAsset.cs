// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.ComponentModel;
using SharpYaml.Serialization;

using SiliconStudio.Assets;
using SiliconStudio.Assets.Compiler;
using SiliconStudio.Core;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Core.Reflection;
using SiliconStudio.Core.Yaml;
using SiliconStudio.Paradox.Engine;

using IObjectFactory = SiliconStudio.Core.Reflection.IObjectFactory;

namespace SiliconStudio.Paradox.Assets.Entities
{
    /// <summary>
    /// A scene asset.
    /// </summary>
    [DataContract("SceneAsset")]
    [AssetDescription(FileSceneExtension)]
    [ObjectFactory(typeof(SceneFactory))]
    [ThumbnailCompiler(PreviewerCompilerNames.SceneThumbnailCompilerQualifiedName)]
    [AssetFormatVersion(8)]
    [AssetUpgrader(0, 1, typeof(RemoveSourceUpgrader))]
    [AssetUpgrader(1, 2, typeof(RemoveBaseUpgrader))]
    [AssetUpgrader(2, 3, typeof(RemoveModelDrawOrderUpgrader))]
    [AssetUpgrader(3, 4, typeof(RenameSpriteProviderUpgrader))]
    [AssetUpgrader(4, 5, typeof(RemoveSpriteExtrusionMethodUpgrader))]
    [AssetUpgrader(5, 6, typeof(RemoveModelParametersUpgrader))]
    [AssetUpgrader(6, 7, typeof(RemoveEnabledFromIncompatibleComponent))]
    [AssetUpgrader(7, 8, typeof(SceneIsNotEntityUpgrader))]
    [Display(200, "Scene", "A scene")]
    public class SceneAsset : EntityAsset
    {
        public const string FileSceneExtension = ".pdxscene";

        public static SceneAsset Create()
        {
            // Create a new root entity, and make sure transformation component is created

            return new SceneAsset
            {
                Hierarchy =
                {
                    Entities = {},
                }
            };
        }

        class RemoveSourceUpgrader : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                if (asset.Source != null)
                    asset.Source = DynamicYamlEmpty.Default;
                if (asset.SourceHash != null)
                    asset.SourceHash = DynamicYamlEmpty.Default;
            }
        }

        class RemoveBaseUpgrader : IAssetUpgrader
        {
            public void Upgrade(int currentVersion, int targetVersion, ILogger log, YamlMappingNode yamlAssetNode)
            {
                dynamic asset = new DynamicYamlMapping(yamlAssetNode);
                var baseBranch = asset["~Base"];
                if (baseBranch != null)
                    asset["~Base"] = DynamicYamlEmpty.Default;

                SetSerializableVersion(asset, targetVersion);
            }

            private static void SetSerializableVersion(dynamic asset, int value)
            {
                asset.SerializedVersion = value;
                // Ensure that it is stored right after the asset Id
                asset.MoveChild("SerializedVersion", asset.IndexOf("Id") + 1);
            }
        }

        class RemoveModelDrawOrderUpgrader : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                var hierarchy = asset.Hierarchy;
                var entities = (DynamicYamlArray)hierarchy.Entities;
                foreach (dynamic entity in entities)
                {
                    var components = entity.Components;
                    var modelComponent = components["ModelComponent.Key"];
                    if (modelComponent != null)
                        modelComponent.RemoveChild("DrawOrder");
                }
            }
        }

        class RenameSpriteProviderUpgrader : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                var hierarchy = asset.Hierarchy;
                var entities = (DynamicYamlArray)hierarchy.Entities;
                foreach (dynamic entity in entities)
                {
                    var components = entity.Components;
                    var spriteComponent = components["SpriteComponent.Key"];
                    if (spriteComponent != null)
                    {
                        var provider = spriteComponent.SpriteProvider;
                        var providerAsMap = provider as DynamicYamlMapping;
                        if (providerAsMap != null && providerAsMap.Node.Tag == "!SpriteFromSpriteGroup")
                        {
                            provider.Sheet = provider.SpriteGroup;
                            provider.SpriteGroup = DynamicYamlEmpty.Default;
                            providerAsMap.Node.Tag = "!SpriteFromSheet";
                        }
                    }
                }
            }
        }

        class RemoveSpriteExtrusionMethodUpgrader : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                var hierarchy = asset.Hierarchy;
                var entities = (DynamicYamlArray)hierarchy.Entities;
                foreach (dynamic entity in entities)
                {
                    var components = entity.Components;
                    var spriteComponent = components["SpriteComponent.Key"];
                    if (spriteComponent != null)
                        spriteComponent.RemoveChild("ExtrusionMethod");
                }
            }
        }

        class RemoveModelParametersUpgrader : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                var hierarchy = asset.Hierarchy;
                var entities = (DynamicYamlArray)hierarchy.Entities;
                foreach (dynamic entity in entities)
                {
                    var components = entity.Components;
                    var spriteComponent = components["ModelComponent.Key"];
                    if (spriteComponent != null)
                        spriteComponent.RemoveChild("Parameters");
                }
            }
        }

        class RemoveEnabledFromIncompatibleComponent : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                var hierarchy = asset.Hierarchy;
                var entities = (DynamicYamlArray)hierarchy.Entities;
                foreach (dynamic entity in entities)
                {
                    foreach (var component in entity.Components)
                    {
                        // All components not in this list won't have Enabled anymore
                        if (component.Key != "BackgroundComponent.Key"
                            && component.Key != "CameraComponent.Key"
                            && component.Key != "ChildSceneComponent.Key"
                            && component.Key != "LightComponent.Key"
                            && component.Key != "ModelComponent.Key"
                            && component.Key != "SkyboxComponent.Key"
                            && component.Key != "SpriteComponent.Key"
                            && component.Key != "UIComponent.Key")
                        {
                            component.Value.RemoveChild("Enabled");
                        }
                    }
                }
            }
        }

        class SceneIsNotEntityUpgrader : AssetUpgraderBase
        {
            protected override void UpgradeAsset(int currentVersion, int targetVersion, ILogger log, dynamic asset)
            {
                // Transform RootEntity in RootEntities
                var rootEntityFieldIndex = asset.Hierarchy.IndexOf("RootEntity");
                asset.Hierarchy.RootEntity = DynamicYamlEmpty.Default;

                asset.Hierarchy.RootEntities = new DynamicYamlArray(new YamlSequenceNode());

                // Make sure it is at same position than just removed RootEntity
                asset.Hierarchy.MoveChild("RootEntities", rootEntityFieldIndex);

                // Remove previous root entity and make its SceneComponent as Hierarchy.SceneComponent
                int entityIndex = 0;
                dynamic rootEntity = null;
                foreach (var entity in asset.Hierarchy.Entities)
                {
                    if (entity.Node.Tag == "!Scene")
                    {
                        // Capture root entity and delete it from this list
                        rootEntity = entity;
                        asset.Hierarchy.Entities.RemoveAt(entityIndex);
                        break;
                    }
                    entityIndex++;
                }

                if (rootEntity == null)
                {
                    throw new InvalidOperationException("Could not upgrade SceneAsset because there no root Scene could be found");
                }

                // Update list of root entities
                foreach (var child in rootEntity.Components["TransformComponent.Key"].Children)
                {
                    asset.Hierarchy.RootEntities.Add(child.Entity.Id);
                }

                // Move scene component
                asset.Hierarchy.SceneSettings = rootEntity.Components["SceneComponent.Key"];
            }
        }

        private class SceneFactory : IObjectFactory
        {
            public object New(Type type)
            {
                return Create();
            }
        }
    }
}
