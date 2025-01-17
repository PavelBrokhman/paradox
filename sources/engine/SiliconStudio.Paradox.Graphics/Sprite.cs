// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;

using SiliconStudio.Core;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Core.Serialization;
using SiliconStudio.Core.Serialization.Contents;

namespace SiliconStudio.Paradox.Graphics
{
    /// <summary>
    /// A sprite.
    /// </summary>
    [DataContract]
    [ContentSerializer(typeof(DataContentSerializer<Sprite>))]
    [DataSerializerGlobal(typeof(ReferenceSerializer<Sprite>), Profile = "Asset")]
    public class Sprite
    {
        private ImageOrientation orientation;
        private Vector2 sizeInPixels;
        private Vector2 pixelsPerUnit;
        
        internal RectangleF RegionInternal;
        internal Vector4 BordersInternal;
        internal Vector2 SizeInternal;

        internal event EventHandler<EventArgs> BorderChanged;
        internal event EventHandler<EventArgs> SizeChanged;

        /// <summary>
        /// Create an instance of <see cref="Sprite"/> with a unique random name.
        /// </summary>
        public Sprite()
            : this(Guid.NewGuid().ToString(), null)
        {
        }

        /// <summary>
        /// Creates an empty <see cref="Sprite"/> having the provided name.
        /// </summary>
        /// <param name="fragmentName">Name of the fragment</param>
        public Sprite(string fragmentName)
            :this(fragmentName, null)
        {
        }

        /// <summary>
        /// Create an instance of <see cref="Sprite"/> from the provided <see cref="Texture"/>.
        /// A unique Id is set as name and the <see cref="Region"/> is initialized to the size of the whole texture.
        /// </summary>
        /// <param name="texture">The texture to use as texture</param>
        public Sprite(Texture texture)
            : this(Guid.NewGuid().ToString(), texture)
        {
        }

        /// <summary>
        /// Creates a <see cref="Sprite"/> having the provided texture and name.
        /// The region size is initialized with the whole size of the texture.
        /// </summary>
        /// <param name="fragmentName">The name of the sprite</param>
        /// <param name="texture">The texture to use as texture</param>
        public Sprite(string fragmentName, Texture texture)
        {
            Name = fragmentName;
            IsTransparent = true;

            Texture = texture;
            if(texture != null)
                Region = new Rectangle(0, 0, texture.ViewWidth, texture.ViewHeight);
        }

        /// <summary>
        /// Gets or sets the name of the image fragment.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The texture in which the image is contained
        /// </summary>
        public Texture Texture { get; set; }

        /// <summary>
        /// The position of the center of the image in pixels.
        /// </summary>
        public Vector2 Center;

        /// <summary>
        /// The rectangle specifying the region of the texture to use as fragment.
        /// </summary>
        public RectangleF Region
        {
            get { return RegionInternal; }
            set
            {
                RegionInternal = value;
                UpdateSizes();
            }
        }

        /// <summary>
        /// Gets or sets the value indicating if the fragment contains transparent regions.
        /// </summary>
        public bool IsTransparent { get; set; }

        /// <summary>
        /// Gets or sets the rotation to apply to the texture region when rendering the <see cref="Sprite"/>
        /// </summary>
        public virtual ImageOrientation Orientation
        {
            get {  return orientation; }
            set
            {
                orientation = value;
                UpdateSizes();
            }
        }
        
        /// <summary>
        /// Gets or sets size of the unstretchable borders of source sprite in pixels.
        /// </summary>
        /// <remarks>Borders size are ordered as follows X->Left, Y->Right, Z ->Top, W -> Bottom.</remarks>
        public Vector4 Borders
        {
            get { return BordersInternal; }
            set
            {
                if (value == BordersInternal)
                    return;

                BordersInternal = value;
                HasBorders = BordersInternal.Length() > MathUtil.ZeroTolerance;

                var handler = BorderChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets the value indicating if the image has unstretchable borders.
        /// </summary>
        public bool HasBorders { get; private set; }

        /// <summary>
        /// Gets the size of the sprite in scene units.
        /// Note that the orientation of the image is taken into account in this calculation.
        /// </summary>
        public Vector2 Size
        {
            get {  return SizeInternal; }
        }

        /// <summary>
        /// Gets the size of the sprite in pixels. 
        /// Note that the orientation of the image is taken into account in this calculation.
        /// </summary>
        public Vector2 SizeInPixels
        {
            get { return sizeInPixels; }
            private set
            {
                if (value == sizeInPixels)
                    return;

                sizeInPixels = value;

                var handler = SizeChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets or sets the pixels per scene unit of the sprite.
        /// </summary>
        public Vector2 PixelsPerUnit
        {
            get { return pixelsPerUnit; }
            set
            {
                if (pixelsPerUnit == value)
                    return;

                pixelsPerUnit = value;
                UpdateSizes();
            }
        }

        private void UpdateSizes()
        {
            var pixelSize = new Vector2(RegionInternal.Width, RegionInternal.Height);
            SizeInternal = new Vector2(pixelSize.X / pixelsPerUnit.X, pixelSize.Y / pixelsPerUnit.Y);
            if (orientation == ImageOrientation.Rotated90)
            {
                Utilities.Swap(ref pixelSize.X, ref pixelSize.Y);
                Utilities.Swap(ref SizeInternal.X, ref SizeInternal.Y);
            }

            SizeInPixels = pixelSize;
        }

        public override string ToString()
        {
            var textureName = Texture != null ? Texture.Name : "''";
            return Name + ", Texture: " + textureName + ", Region: " + Region;
        }

        /// <summary>
        /// Clone the current sprite.
        /// </summary>
        /// <returns>A new instance of the current sprite.</returns>
        public Sprite Clone()
        {
            return (Sprite)MemberwiseClone();
        }
    }
}