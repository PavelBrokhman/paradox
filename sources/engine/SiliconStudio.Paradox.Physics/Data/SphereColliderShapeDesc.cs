﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System.ComponentModel;

using SiliconStudio.Core;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Core.Serialization.Contents;

namespace SiliconStudio.Paradox.Physics
{
    [ContentSerializer(typeof(DataContentSerializer<SphereColliderShapeDesc>))]
    [DataContract("SphereColliderShapeDesc")]
    [Display(50, "SphereColliderShape")]
    public class SphereColliderShapeDesc : IColliderShapeDesc
    {
        /// <userdoc>
        /// Select this if this shape will represent a Circle 2D shape
        /// </userdoc>
        [DataMember(10)]
        public bool Is2D;

        /// <userdoc>
        /// The radius of the sphere/circle.
        /// </userdoc>
        [DataMember(20)]
        [DefaultValue(0.5f)]
        public float Radius = 0.5f;

        /// <userdoc>
        /// The offset with the real graphic mesh.
        /// </userdoc>
        [DataMember(30)]
        public Vector3 LocalOffset;
    }
}