// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.IO;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Utils;
using osuTK;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace osu.Game.Skinning.Components
{
    public enum ComboburstSide
    {
        Alternate,
        Random,
        LeftOnly,
        RightOnly,
        Both,
    }

    public enum ComboburstLayering
    {
        AboveGameplay,
        BelowGameplay,
    }

    /// <summary>
    /// A classic osu! comboburst overlay that slides in character artwork from the bottom corners
    /// on combo milestones with customizable intervals, sides, duration, layering, and randomization.
    /// </summary>
    public partial class ComboburstDisplay : CompositeDrawable, ISerialisableDrawable
    {
        public bool UsesFixedAnchor { get; set; }

        [SettingSource("Combo Interval", "Trigger a comboburst every N combo (e.g. 50, 100).")]
        public BindableNumber<int> ComboInterval { get; } = new BindableInt(50)
        {
            MinValue = 10,
            MaxValue = 500,
            Precision = 10,
        };

        [SettingSource("Minimum Combo", "Minimum combo count before combobursts begin appearing.")]
        public BindableNumber<int> MinimumCombo { get; } = new BindableInt(30)
        {
            MinValue = 10,
            MaxValue = 500,
            Precision = 10,
        };

        [SettingSource("Display Side", "Which side of the screen combobursts should appear on.")]
        public Bindable<ComboburstSide> Side { get; } = new Bindable<ComboburstSide>(ComboburstSide.Alternate);

        [SettingSource("Layering", "Display combobursts in front of HUD or behind gameplay hit objects.")]
        public Bindable<ComboburstLayering> Layering { get; } = new Bindable<ComboburstLayering>(ComboburstLayering.AboveGameplay);

        [SettingSource("Randomize Character", "Randomly select from available comboburst images instead of cycling sequentially.")]
        public BindableBool RandomizeCharacter { get; } = new BindableBool(true);

        [SettingSource("Hold Duration (ms)", "How long the character stays visible on screen.")]
        public BindableNumber<double> HoldDuration { get; } = new BindableDouble(750)
        {
            MinValue = 200,
            MaxValue = 3000,
            Precision = 50,
        };

        [SettingSource("Mirror Right-Side Bursts", "Flip character horizontally when sliding in from the right edge.")]
        public BindableBool MirrorRight { get; } = new BindableBool(true);

        [SettingSource("Burst Opacity", "Transparency level for comboburst character sprites.", SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> BurstOpacity { get; } = new BindableFloat(1.0f)
        {
            MinValue = 0.1f,
            MaxValue = 1.0f,
            Precision = 0.05f,
        };

        [SettingSource("Burst Scale", "Scale multiplier for comboburst character sprites.", SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> BurstScale { get; } = new BindableFloat(1.0f)
        {
            MinValue = 0.5f,
            MaxValue = 2.0f,
            Precision = 0.05f,
        };

        [SettingSource("Custom Prefix Filter", "Specific prefix to look for (leave blank for all comboburst images).", SettingControlType = typeof(ComboburstSelectorControl))]
        public Bindable<string> CustomPrefix { get; } = new Bindable<string>(string.Empty);

        [Resolved]
        private SkinManager skinManager { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

        [Resolved(canBeNull: true)]
        private ScoreProcessor? scoreProcessor { get; set; }

        [Resolved(canBeNull: true)]
        private GameplayUnderlayContainer? underlayContainer { get; set; }

        /// <summary>
        /// Bursts are displayed at roughly their "@2x" halves (2048px artwork shows at 1024px), so anything
        /// larger than this is wasted VRAM and drags a huge upload through the draw thread for no visual gain.
        /// </summary>
        private const int max_burst_texture_size = 1024;

        /// <summary>
        /// How many decoded images to keep ready ahead of time, so a burst never has to wait on a
        /// decode (and its upload) at the exact moment it is meant to appear.
        /// </summary>
        private const int warm_pool_size = 3;

        private readonly List<string> availableFiles = new List<string>();
        private readonly List<string> warmPool = new List<string>();
        private readonly Dictionary<string, Texture> textureCache = new Dictionary<string, Texture>();
        private readonly HashSet<string> pendingLoads = new HashSet<string>();
        private readonly List<string> cachedFileList = new List<string>();
        private string? cachedFileListKey;
        private ISkin? activeSkin;
        private RealmBackedResourceStore<SkinInfo>? burstFileStore;
        private IRenderer? burstRenderer;
        private Guid burstFileStoreSkinId;
        private int loadGeneration;
        private readonly Container burstContainer;
        private int lastTriggeredCombo;
        private int currentTextureIndex;
        private bool nextSideIsLeft = true;

        public ComboburstDisplay()
        {
            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;

            InternalChild = burstContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            CustomPrefix.BindValueChanged(_ => Scheduler.AddOnce(reloadTextures));
            skinManager.CurrentSkinInfo.BindValueChanged(_ => Scheduler.AddOnce(reloadTextures));

            if (skinSource != null)
                skinSource.SourceChanged += onSkinSourceChanged;

            if (scoreProcessor != null)
                scoreProcessor.Combo.BindValueChanged(onComboChanged);
        }

        private void onSkinSourceChanged() => Scheduler.AddOnce(reloadTextures);

        protected override void LoadComplete()
        {
            base.LoadComplete();
            reloadTextures();
        }

        private void reloadTextures()
        {
            if (!IsLoaded)
                return;

            availableFiles.Clear();

            string prefix = CustomPrefix.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(prefix))
                prefix = "comboburst";

            prefix = Path.GetFileNameWithoutExtension(prefix);
            prefix = Regex.Replace(prefix, @"@\d+x$", "", RegexOptions.IgnoreCase);
            prefix = Regex.Replace(prefix, @"[-_]\d+$", "");

            var currentSkin = skinManager?.CurrentSkin.Value;
            var currentSkinInfo = skinManager?.CurrentSkinInfo.Value;

            if (currentSkin == null || currentSkinInfo == null)
                return;

            // Reloading is triggered by both load completion and skin-source changes during a single
            // player load; resolving the same skin twice would double the file scan for no gain.
            string cacheKey = $"{currentSkinInfo.ID}|{prefix}";

            activeSkin = currentSkin;

            // The texture pipeline reads straight from the skin's user files; it goes stale with the skin.
            if (burstFileStore == null || burstFileStoreSkinId != currentSkinInfo.ID)
                rebuildTexturePipeline();

            if (cachedFileListKey == cacheKey)
            {
                availableFiles.AddRange(cachedFileList);
                refillWarmPool();
                return;
            }

            // Search ONLY the files actually present in the active user skin.
            List<string>? matchingFiles = currentSkinInfo.PerformRead(s =>
                s.Files
                 .Where(f => SupportedExtensions.IMAGE_EXTENSIONS.Contains(Path.GetExtension(f.Filename).ToLowerInvariant()))
                 .Where(f =>
                 {
                     string filenameNoExt = Path.GetFileNameWithoutExtension(f.Filename);
                     string cleanFilename = Regex.Replace(filenameNoExt, @"@\d+x$", "", RegexOptions.IgnoreCase);
                     string baseFilename = Regex.Replace(cleanFilename, @"[-_]\d+$", "");
                     string dirName = (Path.GetDirectoryName(f.Filename) ?? string.Empty).Replace('\\', '/');
                     string fullNoExt = Path.ChangeExtension(f.Filename, null).Replace('\\', '/');

                     return string.Equals(cleanFilename, prefix, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(baseFilename, prefix, StringComparison.OrdinalIgnoreCase)
                            || cleanFilename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(dirName, prefix, StringComparison.OrdinalIgnoreCase)
                            || dirName.EndsWith("/" + prefix, StringComparison.OrdinalIgnoreCase)
                            || fullNoExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                 })
                 .Select(f => f.Filename.Replace('\\', '/'))
                 .Distinct()
                 .OrderBy(f => f)
                 .ToList()
            );

            if (matchingFiles != null)
            {
                // Collapse "comboburst-5.png" and "comboburst-5@2x.png" into one entry, keeping the
                // high-resolution variant - that's the one legacy skins would pick anyway.
                var filesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string file in matchingFiles)
                {
                    string pathNoExt = Path.ChangeExtension(file, null);
                    string cleanPathNoExt = Regex.Replace(pathNoExt, @"@\d+x$", "", RegexOptions.IgnoreCase);

                    if (!filesByName.TryGetValue(cleanPathNoExt, out string? existing) || isHighResolution(file) && !isHighResolution(existing))
                        filesByName[cleanPathNoExt] = file;
                }

                availableFiles.AddRange(filesByName.Values.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }

            // Fallback for custom prefix if specific prefix wasn't found in skin
            if (availableFiles.Count == 0 && prefix != "comboburst")
                availableFiles.Add("comboburst");

            cachedFileListKey = cacheKey;
            cachedFileList.Clear();
            cachedFileList.AddRange(availableFiles);

            // Textures belong to the previous skin; drop them and invalidate in-flight loads.
            textureCache.Clear();
            warmPool.Clear();
            loadGeneration++;

            // Pre-decode a few images so upcoming combo milestones aren't waiting on a decode.
            refillWarmPool();
        }

        private void onComboChanged(ValueChangedEvent<int> e)
        {
            int currentCombo = e.NewValue;

            // Reset milestone only when combo actually resets to 0
            if (currentCombo == 0)
            {
                lastTriggeredCombo = 0;
                return;
            }

            int minCombo = MinimumCombo.Value;
            if (currentCombo < minCombo)
                return;

            int interval = Math.Max(10, ComboInterval.Value);
            int currentMilestone = (currentCombo / interval) * interval;

            if (currentMilestone > lastTriggeredCombo && currentMilestone >= minCombo)
            {
                lastTriggeredCombo = currentMilestone;
                triggerComboburst();
            }
        }

        private void triggerComboburst()
        {
            if (availableFiles.Count == 0)
                return;

            string path;

            if (RandomizeCharacter.Value && warmPool.Count > 0)
            {
                int poolIndex = RNG.Next(warmPool.Count);
                path = warmPool[poolIndex];
                warmPool.RemoveAt(poolIndex);
            }
            else
            {
                int index;
                if (RandomizeCharacter.Value)
                {
                    index = RNG.Next(availableFiles.Count);
                }
                else
                {
                    index = currentTextureIndex % availableFiles.Count;
                    currentTextureIndex++;
                }

                path = availableFiles[index];
            }

            // Play the cue immediately - the artwork may still be decoding, but the burst should
            // never be silenced just because the first use of a large image is still in flight.
            var sample = skinSource?.GetSample(new osu.Game.Audio.SampleInfo("comboburst"))
                         ?? skinManager?.CurrentSkin.Value.GetSample(new osu.Game.Audio.SampleInfo("comboburst"));
            sample?.Play();

            if (textureCache.TryGetValue(path, out var texture))
            {
                spawnBurst(texture);
                refillWarmPool();
                return;
            }

            if (pendingLoads.Contains(path))
                return;

            loadTexture(path, loaded =>
            {
                spawnBurst(loaded);
                refillWarmPool();
            });
        }

        /// <summary>
        /// Loads a single comboburst image off the update thread. Eagerly decoding every comboburst
        /// in the skin used to cost seconds of load time and gigabytes of VRAM for 4K image packs.
        /// </summary>
        private void loadTexture(string path, Action<Texture> onLoaded, bool warmLoad = false)
        {
            if (!pendingLoads.Add(path))
                return;

            int generation = loadGeneration;

            Task.Run(() =>
            {
                Texture? texture = null;

                try
                {
                    texture = loadBurstTexture(path);
                }
                catch (Exception)
                {
                    // A skin can be disposed mid-lookup when switching presets; treat as missing.
                }

                try
                {
                    Schedule(() =>
                    {
                        pendingLoads.Remove(path);

                        if (generation != loadGeneration || IsDisposed)
                            return;

                        if (texture == null)
                        {
                            // Don't keep retrying a lookup that will never resolve.
                            availableFiles.Remove(path);
                            cachedFileList.Remove(path);
                            return;
                        }

                        textureCache[path] = texture;

                        if (warmLoad)
                        {
                            warmPool.Add(path);
                            refillWarmPool();
                        }

                        onLoaded(texture);
                    });
                }
                catch (ObjectDisposedException)
                {
                }
            });
        }

        private void rebuildTexturePipeline()
        {
            burstFileStore?.Dispose();
            burstFileStore = null;
            burstRenderer = null;

            if (skinManager is not IStorageResourceProvider provider)
                return;

            var skinInfo = skinManager.CurrentSkinInfo.Value;

            burstFileStore = new RealmBackedResourceStore<SkinInfo>(skinInfo, provider.Files, provider.RealmAccess);
            burstRenderer = provider.Renderer;
            burstFileStoreSkinId = skinInfo.ID;
        }

        /// <summary>
        /// Decodes a comboburst image off the update thread, downscaling it to roughly its on-screen
        /// resolution before it reaches the GPU. 2048x2048 burst packs are a 16 MB draw-thread upload
        /// plus mipmap generation per image; at the size bursts actually display at, that work is
        /// invisible except as frame drops the first time each one is used.
        /// </summary>
        private Texture? loadBurstTexture(string path)
        {
            var fileStore = burstFileStore;
            var renderer = burstRenderer;

            if (fileStore != null && renderer != null)
            {
                using (Stream? stream = fileStore.GetStream(path))
                {
                    if (stream != null)
                    {
                        SixLabors.ImageSharp.Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                        int originalWidth = image.Width;

                        if (image.Width > max_burst_texture_size || image.Height > max_burst_texture_size)
                        {
                            image.Mutate(i => i.Resize(new ResizeOptions
                            {
                                Size = new Size(max_burst_texture_size, max_burst_texture_size),
                                Mode = ResizeMode.Max,

                                // Box averages whole source pixels instead of sampling a 2x2 kernel per
                                // output pixel - it is an order of magnitude faster at this ratio and
                                // the visual difference on character art is imperceptible.
                                Sampler = KnownResamplers.Box,
                            }));
                        }

                        var upload = new TextureUpload(image);
                        var texture = renderer.CreateTexture(upload.Width, upload.Height, false, TextureFilteringMode.Linear, WrapMode.None, WrapMode.None);

                        // "@2x" artwork is displayed at half its pixel size by legacy skin lookups.
                        float legacyScale = isHighResolution(path) ? 2f : 1f;

                        // Keep the on-screen size identical to the untouched legacy lookup.
                        texture.AssetName = path;
                        texture.ScaleAdjust = legacyScale * upload.Width / originalWidth;
                        texture.SetData(upload);

                        return texture;
                    }
                }
            }

            // Fallback for skins whose files aren't visible through the active skin's storage
            // (e.g. beatmap skin overrides); loads at full resolution like before.
            return activeSkin?.GetTexture(path);
        }

        private static bool isHighResolution(string filename) =>
            Path.GetFileNameWithoutExtension(filename).EndsWith("@2x", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Keeps a small set of images decoded ahead of time. With random selection a lucky prefetch
        /// almost never matches the next pick, so the pool is what actually gets drawn from.
        /// </summary>
        private void refillWarmPool()
        {
            if (availableFiles.Count == 0)
                return;

            int target = RandomizeCharacter.Value ? warm_pool_size : 1;

            for (int attempts = 0; warmPool.Count < target && pendingLoads.Count < 2 && attempts < 32; attempts++)
            {
                string path = RandomizeCharacter.Value
                    ? availableFiles[RNG.Next(availableFiles.Count)]
                    : availableFiles[currentTextureIndex % availableFiles.Count];

                if (warmPool.Contains(path) || pendingLoads.Contains(path))
                    continue;

                if (textureCache.ContainsKey(path))
                {
                    warmPool.Add(path);
                    continue;
                }

                loadTexture(path, _ => { }, warmLoad: true);
            }
        }

        private void spawnBurst(Texture texture)
        {
            switch (Side.Value)
            {
                case ComboburstSide.LeftOnly:
                    spawnBurstSprite(texture, isLeft: true);
                    break;

                case ComboburstSide.RightOnly:
                    spawnBurstSprite(texture, isLeft: false);
                    break;

                case ComboburstSide.Both:
                    spawnBurstSprite(texture, isLeft: true);
                    spawnBurstSprite(texture, isLeft: false);
                    break;

                case ComboburstSide.Random:
                    spawnBurstSprite(texture, isLeft: RNG.NextBool());
                    break;

                case ComboburstSide.Alternate:
                default:
                    spawnBurstSprite(texture, isLeft: nextSideIsLeft);
                    nextSideIsLeft = !nextSideIsLeft;
                    break;
            }
        }

        private void spawnBurstSprite(Texture texture, bool isLeft)
        {
            float targetScale = BurstScale.Value;
            float spriteWidth = texture.DisplayWidth * targetScale;
            double holdTime = HoldDuration.Value;
            float opacity = BurstOpacity.Value;

            Container targetContainer = (Layering.Value == ComboburstLayering.BelowGameplay && underlayContainer != null)
                ? underlayContainer
                : burstContainer;

            Sprite sprite;

            if (isLeft)
            {
                sprite = new Sprite
                {
                    Texture = texture,
                    Scale = new Vector2(targetScale),
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Position = new Vector2(-spriteWidth, 0),
                    Alpha = 0,
                };

                targetContainer.Add(sprite);

                sprite.FadeTo(opacity, 180, Easing.OutQuad)
                      .MoveToX(0, 250, Easing.OutBack)
                      .Delay(holdTime)
                      .FadeOut(300, Easing.InQuad)
                      .MoveToX(-spriteWidth * 0.4f, 300, Easing.InQuad)
                      .Expire();
            }
            else
            {
                bool mirror = MirrorRight.Value;

                sprite = new Sprite
                {
                    Texture = texture,
                    Scale = new Vector2(mirror ? -targetScale : targetScale, targetScale),
                    Anchor = Anchor.BottomRight,
                    Origin = mirror ? Anchor.BottomLeft : Anchor.BottomRight,
                    Position = new Vector2(spriteWidth, 0),
                    Alpha = 0,
                };

                targetContainer.Add(sprite);

                sprite.FadeTo(opacity, 180, Easing.OutQuad)
                      .MoveToX(0, 250, Easing.OutBack)
                      .Delay(holdTime)
                      .FadeOut(300, Easing.InQuad)
                      .MoveToX(spriteWidth * 0.4f, 300, Easing.InQuad)
                      .Expire();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            // Invalidate any in-flight texture lookups so their continuations don't touch a disposed drawable.
            loadGeneration++;

            burstFileStore?.Dispose();
            burstFileStore = null;

            if (skinSource != null)
                skinSource.SourceChanged -= onSkinSourceChanged;
        }

        public partial class ComboburstSelectorControl : SettingsDropdown<string>
        {
            [Resolved]
            private SkinManager skins { get; set; } = null!;

            protected override void LoadComplete()
            {
                base.LoadComplete();

                var currentSkinInfo = skins.CurrentSkinInfo.Value;
                string[]? availableFiles = currentSkinInfo?.PerformRead(
                    s => s.Files
                          .Where(f => SupportedExtensions.IMAGE_EXTENSIONS.Contains(Path.GetExtension(f.Filename).ToLowerInvariant()))
                          .Select(f => f.Filename.Replace('\\', '/'))
                          .Distinct()).ToArray();

                if (availableFiles != null && availableFiles.Length > 0)
                {
                    var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (string file in availableFiles)
                    {
                        string dir = (Path.GetDirectoryName(file) ?? string.Empty).Replace('\\', '/');
                        if (!string.IsNullOrEmpty(dir))
                            options.Add(dir);

                        string name = Path.GetFileNameWithoutExtension(file);
                        string cleanName = Regex.Replace(name, @"@\d+x$", "", RegexOptions.IgnoreCase);
                        string baseName = Regex.Replace(cleanName, @"[-_]\d+$", "");

                        if (!string.IsNullOrEmpty(baseName))
                            options.Add(baseName);
                    }

                    var sorted = options.OrderBy(o => o.Equals("comboburst", StringComparison.OrdinalIgnoreCase) ? string.Empty : o).ToArray();
                    Items = sorted;
                }
            }
        }
    }
}
