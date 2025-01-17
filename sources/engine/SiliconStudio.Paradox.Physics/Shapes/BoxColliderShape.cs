﻿// Copyright (c) 2014-2015 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using SiliconStudio.Core.Mathematics;
using SiliconStudio.Paradox.Graphics;
using SiliconStudio.Paradox.Graphics.GeometricPrimitives;

namespace SiliconStudio.Paradox.Physics
{
    public class BoxColliderShape : ColliderShape 
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoxColliderShape"/> class.
        /// </summary>
        /// <param name="size">The size of the cube</param>
        public BoxColliderShape(Vector3 size)
        {
            Type = ColliderShapeTypes.Box;
            Is2D = false;

            InternalShape = new BulletSharp.BoxShape(size/2);

            DebugPrimitiveMatrix = Matrix.Scaling(size * 1.01f);
        }

        public override GeometricPrimitive CreateDebugPrimitive(GraphicsDevice device)
        {
            return GeometricPrimitive.Cube.New(device);
        }
    }
}
