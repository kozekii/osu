// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;

namespace osu.Game.Skinning
{
    /// <summary>
    /// An <see cref="ISkin"/> wrapper that provides only audio samples (hitsounds) from an underlying skin source,
    /// returning <c>null</c> for all visual drawables, textures, colours, and configurations.
    /// </summary>
    public class HitsoundOnlySkin : ISkin
    {
        public readonly ISkin UnderlyingSkin;

        public HitsoundOnlySkin(ISkin underlyingSkin)
        {
            UnderlyingSkin = underlyingSkin;
        }

        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

        public ISample? GetSample(ISampleInfo sampleInfo) => UnderlyingSkin.GetSample(sampleInfo);

        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull => null;
    }
}
