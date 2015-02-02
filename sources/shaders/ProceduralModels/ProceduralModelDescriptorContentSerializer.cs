﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using SiliconStudio.Core.Serialization.Contents;

namespace SiliconStudio.Paradox.Effects.ProceduralModels
{
    internal class ProceduralModelDescriptorContentSerializer : DataContentSerializer<Model, ProceduralModelDescriptor>
    {
        public override object Construct(ContentSerializerContext context)
        {
            return new ProceduralModelDescriptor();
        }
    }
}