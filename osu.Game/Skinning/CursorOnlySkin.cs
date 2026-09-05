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

        private static bool isCursorLookup(ISkinComponentLookup lookup)
        {
            if (lookup == null) return false;

            var type = lookup.GetType();
            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SkinComponentLookup<>))
                {
                    var componentProp = type.GetField("Component");
                    if (componentProp != null)
                    {
                        var val = componentProp.GetValue(lookup)?.ToString();
                        if (val != null && val.IndexOf("Cursor", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
                type = type.BaseType;
            }

            return lookup.ToString()?.IndexOf("Cursor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool isCursorTexture(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
                return false;

            string lower = componentName.ToLowerInvariant();
            return lower.StartsWith("cursor", StringComparison.Ordinal)
                   || lower.Contains("/cursor", StringComparison.Ordinal)
                   || lower.Contains("cursortrail", StringComparison.Ordinal)
                   || lower.Contains("cursormiddle", StringComparison.Ordinal)
                   || lower.Contains("cursorexpand", StringComparison.Ordinal)
                   || lower.Contains("cursorripple", StringComparison.Ordinal)
                   || lower.Contains("cursorsmoke", StringComparison.Ordinal)
                   || lower == "star2";
        }

        private static bool isCursorConfig(object lookup)
        {
            if (lookup == null)
                return false;

            string str = lookup.ToString() ?? string.Empty;
            return str.IndexOf("Cursor", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
