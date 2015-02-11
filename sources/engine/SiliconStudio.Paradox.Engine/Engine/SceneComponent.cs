﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;

using SiliconStudio.Core;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Paradox.Engine.Graphics.Composers;
using SiliconStudio.Paradox.EntityModel;

namespace SiliconStudio.Paradox.Engine
{
    /// <summary>
    /// A component used internally to tag a Scene.
    /// </summary>
    public sealed class SceneComponent : EntityComponent
    {
        /// <summary>
        /// The key of this component.
        /// </summary>
        public static PropertyKey<SceneComponent> Key = new PropertyKey<SceneComponent>("Key", typeof(SceneComponent));

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneComponent"/> class.
        /// </summary>
        public SceneComponent()
        {
            SceneRenderer = new SceneRendererLayers();
        }

        /// <summary>
        /// Gets or sets the graphics composer for this scene.
        /// </summary>
        /// <value>The graphics composer.</value>
        [DataMember(10)]
        [Display("Graphics Composition")]
        [NotNull]
        public ISceneRenderer SceneRenderer { get; set; }   // TODO: Should we move this to a special component?

        protected internal override PropertyKey DefaultKey
        {
            get
            {
                return Key;
            }
        }

        private static readonly Type[] DefaultProcessors = new Type[] { typeof(SceneProcessor) };
        protected internal override IEnumerable<Type> GetDefaultProcessors()
        {
            return DefaultProcessors;
        }
    }
}