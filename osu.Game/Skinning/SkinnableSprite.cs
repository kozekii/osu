// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Localisation.SkinComponents;
using osu.Game.Overlays.Settings;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Skinning
{
    /// <summary>
    /// A skinnable element which uses a single texture backing.
    /// </summary>
    public partial class SkinnableSprite : SkinnableDrawable, ISerialisableDrawable
    {
        protected override bool ApplySizeRestrictionsToDefault => true;

        [Resolved]
        private TextureStore textures { get; set; } = null!;

        [SettingSource(typeof(SkinnableComponentStrings), nameof(SkinnableComponentStrings.SpriteName), SettingControlType = typeof(SpriteSelectorControl))]
        public Bindable<string> SpriteName { get; } = new Bindable<string>(string.Empty);

        [SettingSource("Opacity", "Transparency level for the sprite.", SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> SpriteOpacity { get; } = new BindableFloat(1.0f)
        {
            MinValue = 0.05f,
            MaxValue = 1.0f,
            Precision = 0.05f,
        };

        [Resolved]
        private ISkinSource source { get; set; } = null!;

        private readonly bool isUserPlaced;

        public SkinnableSprite(string textureName, Vector2? maxSize = null, ConfineMode confineMode = ConfineMode.NoScaling)
            : base(new SpriteComponentLookup(textureName, maxSize), confineMode)
        {
            SpriteName.Value = textureName;
        }

        public SkinnableSprite()
            : base(new SpriteComponentLookup(string.Empty), ConfineMode.NoScaling)
        {
            isUserPlaced = true;
            RelativeSizeAxes = Axes.None;
            AutoSizeAxes = Axes.Both;

            SpriteName.BindValueChanged(name =>
            {
                ((SpriteComponentLookup)ComponentLookup).LookupName = name.NewValue ?? string.Empty;
                if (IsLoaded)
                    SkinChanged(CurrentSkin);
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (isUserPlaced)
                SpriteOpacity.BindValueChanged(o => Alpha = o.NewValue, true);
        }

        protected override Drawable CreateDefault(ISkinComponentLookup lookup)
        {
            var spriteLookup = (SpriteComponentLookup)lookup;
            var texture = textures.Get(spriteLookup.LookupName);

            if (texture == null)
                return new SpriteNotFound(spriteLookup.LookupName);

            if (spriteLookup.MaxSize != null)
                texture = texture.WithMaximumSize(spriteLookup.MaxSize.Value);

            return new Sprite { Texture = texture };
        }

        public bool UsesFixedAnchor { get; set; }

        internal class SpriteComponentLookup : ISkinComponentLookup
        {
            public string LookupName { get; set; }
            public Vector2? MaxSize { get; set; }

            public SpriteComponentLookup(string textureName, Vector2? maxSize = null)
            {
                LookupName = textureName;
                MaxSize = maxSize;
            }
        }

        public partial class SpriteSelectorControl : SettingsDropdown<string>
        {
            protected override void LoadComplete()
            {
                base.LoadComplete();

                var highestPrioritySkin = getHighestPriorityUserSkin(((SkinnableSprite)SettingSourceObject).source.AllSources) as Skin;

                string[]? availableFiles = highestPrioritySkin?.SkinInfo.PerformRead(
                    s => s.Files
                          .Where(f => SupportedExtensions.IMAGE_EXTENSIONS.Contains(Path.GetExtension(f.Filename).ToLowerInvariant()))
                          .Select(f => f.Filename).Distinct()).ToArray();

                if (availableFiles?.Length > 0)
                    Items = availableFiles;

                static ISkin? getHighestPriorityUserSkin(IEnumerable<ISkin> skins)
                {
                    foreach (var skin in skins)
                    {
                        if (skin is ISkinTransformer transformer && isUserSkin(transformer.Skin))
                            return transformer.Skin;

                        if (isUserSkin(skin))
                            return skin;
                    }

                    return null;
                }

                static bool isUserSkin(ISkin skin)
                    => skin.GetType() == typeof(TrianglesSkin)
                       || skin.GetType() == typeof(ArgonProSkin)
                       || skin.GetType() == typeof(ArgonSkin)
                       || skin.GetType() == typeof(DefaultLegacySkin)
                       || skin.GetType() == typeof(RetroSkin)
                       || skin.GetType() == typeof(LegacySkin);
            }
        }

        public partial class SpriteNotFound : CompositeDrawable
        {
            public SpriteNotFound(string lookup)
            {
                AutoSizeAxes = Axes.Both;

                InternalChildren = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Size = new Vector2(50),
                        Icon = FontAwesome.Solid.QuestionCircle
                    },
                    new OsuSpriteText
                    {
                        Position = new Vector2(25, 50),
                        Text = $"missing: {lookup}",
                        Origin = Anchor.TopCentre,
                    }
                };
            }
        }
    }
}
