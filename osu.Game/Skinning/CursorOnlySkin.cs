// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;

namespace osu.Game.Skinning
{
    /// <summary>
    /// An <see cref="ISkin"/> wrapper that provides only visual cursor drawables, textures, and configurations
    /// from an underlying skin source, returning <c>null</c> for hitsounds and non-cursor components.
    /// </summary>
    public class CursorOnlySkin : ISkin
    {
        public readonly ISkin UnderlyingSkin;

        public CursorOnlySkin(ISkin underlyingSkin)
        {
            UnderlyingSkin = underlyingSkin;
        }

        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
        {
            if (isCursorLookup(lookup))
                return UnderlyingSkin.GetDrawableComponent(lookup);

            return null;
        }

        public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            if (isCursorTexture(componentName))
                return UnderlyingSkin.GetTexture(componentName, wrapModeS, wrapModeT);

            return null;
        }

        public ISample? GetSample(ISampleInfo sampleInfo) => null;

        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull
        {
            if (isCursorConfig(lookup))
                return UnderlyingSkin.GetConfig<TLookup, TValue>(lookup);

            return null;
        }

        private static bool isCursorLookup(ISkinComponentLookup lookup) => hasCursorName(lookup);

        private static bool isCursorTexture(string componentName) =>
            componentName.Contains("cursor", StringComparison.OrdinalIgnoreCase)
            || componentName.Equals("star2", StringComparison.OrdinalIgnoreCase);

        private static bool isCursorConfig(object lookup) => hasCursorName(lookup);

        /// <summary>
        /// Whether a lookup refers to a cursor element by name. <see cref="SkinComponentLookup{T}"/> renders
        /// its component enum name via <see cref="object.ToString"/>, so this covers every ruleset's cursor
        /// components (and cursor skin configurations) without depending on ruleset assemblies.
        /// </summary>
        private static bool hasCursorName(object lookup) =>
            lookup?.ToString()?.Contains("cursor", StringComparison.OrdinalIgnoreCase) == true;
    }
}
