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

        [SettingSource(typeof(SkinnableComponentStrings), nameof(SkinnableComponentStrings.Opacity), SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> Opacity { get; } = new BindableNumber<float>(1)
        {
            MinValue = 0.01f,
            MaxValue = 1f,
            Precision = 0.01f,
        };

        /// <summary>
        /// Backwards-compatibility alias for custom skin presets saved with <c>sprite_opacity</c>.
        /// </summary>
        public BindableNumber<float> SpriteOpacity => Opacity;

        [Resolved]
        private ISkinSource source { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        public SkinnableSprite(string textureName, Vector2? maxSize = null, ConfineMode confineMode = ConfineMode.NoScaling)
            : base(new SpriteComponentLookup(textureName, maxSize), confineMode)
        {
            SpriteName.Value = textureName;
        }

        public SkinnableSprite()
            : base(new SpriteComponentLookup(string.Empty), ConfineMode.NoScaling)
        {
            RelativeSizeAxes = Axes.None;
            AutoSizeAxes = Axes.Both;

            SpriteName.BindValueChanged(name =>
            {
                ((SpriteComponentLookup)ComponentLookup).LookupName = name.NewValue ?? string.Empty;
                if (IsLoaded)
                    SkinChanged(CurrentSkin);
            });
            Opacity.BindValueChanged(opacity => Alpha = opacity.NewValue, true);
        }

        protected override Drawable CreateDefault(ISkinComponentLookup lookup)
        {
            var spriteLookup = (SpriteComponentLookup)lookup;

            // Video assets are not backed by a texture; render them as a looping preview that also plays at runtime.
            if (SkinAssetResolver.IsVideo(spriteLookup.LookupName))
            {
                var videoPreview = new SkinAssetPreview(showChrome: false)
                {
                    Size = spriteLookup.MaxSize ?? new Vector2(256, 144),
                };

                videoPreview.Set(spriteLookup.LookupName,
                    videoStreamProvider: () => SkinAssetResolver.GetVideoStream(skins, source, spriteLookup.LookupName));

                return videoPreview;
            }

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
            [Resolved]
            private SkinManager skins { get; set; } = null!;

            private SkinAssetPreview? preview;

            protected override void LoadComplete()
            {
                base.LoadComplete();

                var sprite = (SkinnableSprite)SettingSourceObject;
                var highestPrioritySkin = getHighestPriorityUserSkin(sprite.source.AllSources) as Skin;

                string[]? availableFiles = highestPrioritySkin?.SkinInfo.PerformRead(
                    s => s.Files
                          .Where(f =>
                          {
                              string ext = Path.GetExtension(f.Filename).ToLowerInvariant();
                              return SupportedExtensions.IMAGE_EXTENSIONS.Contains(ext) || SkinAssetResolver.IsVideo(f.Filename);
                          })
                          .Select(f => f.Filename).Distinct()).ToArray();

                if (availableFiles?.Length > 0)
                    Items = availableFiles;

                // Preview of the currently selected asset (image thumbnail or looping video).
                Content.Add(preview = new SkinAssetPreview
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 90,
                    Margin = new MarginPadding { Top = 8 },
                });

                Current.BindValueChanged(_ => updatePreview(), true);

                static ISkin? getHighestPriorityUserSkin(IEnumerable<ISkin> skins)
                {
                    foreach (var skin in skins)
                    {
                        if (skin is ISkinTransformer transformer && SkinAssetResolver.IsUserSkin(transformer.Skin))
                            return transformer.Skin;

                        if (SkinAssetResolver.IsUserSkin(skin))
                            return skin;
                    }

                    return null;
                }
            }

            private void updatePreview()
            {
                if (preview == null)
                    return;

                string filename = Current.Value ?? string.Empty;

                if (string.IsNullOrEmpty(filename))
                {
                    preview.Set(string.Empty);
                    return;
                }

                var sprite = (SkinnableSprite)SettingSourceObject;

                preview.Set(
                    filename,
                    image: SkinAssetResolver.GetImage(sprite.source, filename),
                    videoStreamProvider: () => SkinAssetResolver.GetVideoStream(skins, sprite.source, filename));
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
