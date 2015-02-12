﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using SiliconStudio.Paradox.Engine.Graphics;

namespace SiliconStudio.Paradox.Effects
{
    /// <summary>
    /// Extensions for <see cref="EntityComponentRendererBase"/>
    /// </summary>
    public static class RendererExtensions
    {
        /// <summary>
        /// Appends a name to an existing <see cref="EntityComponentRendererBase.DebugName"/>.
        /// </summary>
        /// <typeparam name="T">Type of the renderer</typeparam>
        /// <param name="renderer">The renderer</param>
        /// <param name="name">Name to append to <see cref="EntityComponentRendererBase.DebugName"/>.</param>
        /// <returns>The renderer</returns>
        public static T AppendDebugName<T>(this T renderer, string name) where T : EntityComponentRendererBase
        {
            renderer.DebugName = renderer.DebugName == null ? name : renderer.DebugName + " " + name;
            return renderer;
        }
    }
}