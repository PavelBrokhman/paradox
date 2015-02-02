﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;

using SiliconStudio.Core;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Core.Serialization.Contents;

namespace SiliconStudio.Paradox.Effects.ProceduralModels
{
    /// <summary>
    /// A descriptor for a procedural geometry.
    /// </summary>
    [DataContract("ProceduralModelDescriptor")]
    [ContentSerializer(typeof(ProceduralModelDescriptorContentSerializer))]
    //[DataSerializer(typeof(GeometricProceduralDescriptorSerializer))]
    public class ProceduralModelDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProceduralModelDescriptor"/> class.
        /// </summary>
        public ProceduralModelDescriptor()
        {
            Type = new CubeProceduralModel();
        }

        /// <summary>
        /// Gets or sets the type of geometric primitive.
        /// </summary>
        /// <value>The type of geometric primitive.</value>
        [DataMember(10)]
        [NotNull]
        [Display("Type")]
        public IProceduralModel Type { get; set; }

        /// <summary>
        /// Gets or sets the material.
        /// </summary>
        /// <value>The material.</value>
        [DataMember(20)]
        [NotNull]
        [Display("Material")]
        public Material Material { get; set; }

        public Model GenerateModel(IServiceRegistry services)
        {
            if (services == null) throw new ArgumentNullException("services");

            if (Type == null)
            {
                throw new InvalidOperationException("Invalid GeometricPrimitive [{0}]. Expecting a non-null Type");
            }

            var model = Type.Create(services);

            if (Material != null)
            {
                model.Materials.Add(Material);
            }

            return model;
        }
    }
}