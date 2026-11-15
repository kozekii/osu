// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Video;
using osu.Framework.Timing;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.IO;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning
{
    /// <summary>
    /// Displays a single skin asset (an image or a looping video) inside a fixed-size box.
    /// Used by the <see cref="SkinnableSprite"/> selector preview and to render video sprites at runtime.
    /// </summary>
    public partial class SkinAssetPreview : CompositeDrawable
    {
        private readonly Container content;

        private Video? video;
        private ManualClock? manualClock;
        private readonly Stopwatch wallClock = new Stopwatch();
        private double lastWallClockTime;
        private Sprite? imageSprite;
        private string currentFilename = string.Empty;

        /// <param name="showChrome">Whether to draw the dark background / border used by the settings preview.</param>
        public SkinAssetPreview(bool showChrome = true)
        {
            if (showChrome)
            {
                Masking = true;
                CornerRadius = 6;
                BorderThickness = 2;
                BorderColour = new Color4(45, 48, 66, 255);
            }

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(13, 14, 20, 255),
                    Alpha = showChrome ? 1 : 0,
                },
                content = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                },
            };
        }

        /// <summary>
        /// Swap the displayed content. Pass <paramref name="filename"/> along with either an <paramref name="image"/>
        /// texture or a <paramref name="videoStreamProvider"/> depending on the asset type.
        /// </summary>
        public void Set(string filename, Texture? image = null, Func<Stream?>? videoStreamProvider = null)
        {
            // Avoid tearing down and restarting the decoder when the selection hasn't actually changed.
            if (filename == currentFilename && content.Children.Any())
                return;

            currentFilename = filename;
            video = null;
            manualClock = null;
            imageSprite = null;
            content.Clear();

            if (string.IsNullOrEmpty(filename))
                return;

            if (SkinAssetResolver.IsVideo(filename))
            {
                var stream = videoStreamProvider?.Invoke();

                if (stream == null)
                {
                    showPlaceholder("video unavailable");
                    return;
                }

                wallClock.Restart();
                lastWallClockTime = 0;

                content.Child = video = new Video(stream, startAtCurrentTime: false)
                {
                    RelativeSizeAxes = Axes.Both,
                    FillMode = FillMode.Fit,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Clock = new FramedClock(manualClock = new ManualClock()),
                    Loop = true,
                };

                return;
            }

            if (image != null)
            {
                content.Child = imageSprite = new Sprite
                {
                    Texture = image,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };

                return;
            }

            showPlaceholder("no preview");
        }

        private void showPlaceholder(string text)
        {
            content.Child = new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.Default.With(size: 12, weight: FontWeight.Regular),
                Colour = new Color4(120, 128, 144, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };
        }

        protected override void Update()
        {
            base.Update();

            // scale the image to fit the box while preserving aspect ratio.
            if (imageSprite != null && imageSprite.Width > 0 && imageSprite.Height > 0 && DrawSize.X > 0 && DrawSize.Y > 0)
            {
                float fit = Math.Min(DrawSize.X / imageSprite.Width, DrawSize.Y / imageSprite.Height);
                imageSprite.Scale = new Vector2(fit);
            }

            // Drive the decoder from real elapsed wall-clock time so playback is always 1x,
            // independent of the rate of whatever clock this drawable happens to inherit.
            if (manualClock != null)
            {
                double now = wallClock.Elapsed.TotalMilliseconds;
                double delta = now - lastWallClockTime;
                lastWallClockTime = now;

                if (delta > 0 && delta < 100)
                    manualClock.CurrentTime += delta;
            }
        }
    }

    /// <summary>
    /// Helpers to resolve skin asset files (images / videos) from the active user skin for previewing and playback.
    /// </summary>
    public static class SkinAssetResolver
    {
        public static bool IsVideo(string filename)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant();
            return SupportedExtensions.VIDEO_EXTENSIONS.Contains(ext)
                   || ext == ".webm"
                   || ext == ".mkv";
        }

        /// <summary>
        /// Whether the given source is a user-selectable skin whose files can be enumerated.
        /// Beatmap skins are excluded: they override assets but do not own them.
        /// </summary>
        public static bool IsUserSkin(ISkin skin)
            => skin is (ArgonSkin or TrianglesSkin or LegacySkin) and not LegacyBeatmapSkin;

        /// <summary>
        /// Resolve an image texture for a skin file by its on-disk filename (extension is stripped for the texture lookup).
        /// </summary>
        public static Texture? GetImage(ISkinSource source, string filename)
        {
            if (string.IsNullOrEmpty(filename) || IsVideo(filename))
                return null;

            return source.GetTexture(Path.ChangeExtension(filename, null));
        }

        /// <summary>
        /// Open a stream to a video file stored in the highest-priority user skin. Returns <c>null</c> if not found.
        /// </summary>
        public static Stream? GetVideoStream(SkinManager skins, ISkinSource source, string filename)
        {
            if (string.IsNullOrEmpty(filename) || !IsVideo(filename))
                return null;

            if (skins is not IStorageResourceProvider provider)
                return null;

            var userSkin = getHighestPriorityUserSkin(source.AllSources) as Skin;
            if (userSkin?.SkinInfo == null)
                return null;

            try
            {
                string? storagePath = userSkin.SkinInfo.PerformRead(s =>
                    s.Files.FirstOrDefault(f => f.Filename == filename)?.File.GetStoragePath());

                if (string.IsNullOrEmpty(storagePath))
                    return null;

                return provider.Files.GetStream(storagePath);
            }
            catch
            {
                return null;
            }
        }

        private static ISkin? getHighestPriorityUserSkin(IEnumerable<ISkin> skins)
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
}
