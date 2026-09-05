// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Utils;
using osuTK;

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

        [Resolved]
        private TextureStore textures { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private ScoreProcessor? scoreProcessor { get; set; }

        [Resolved(canBeNull: true)]
        private GameplayUnderlayContainer? underlayContainer { get; set; }

        private readonly List<Texture> availableTextures = new List<Texture>();
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

            availableTextures.Clear();

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

            // Search ONLY the files actually present in the active user skin
            List<string>? matchingFiles = currentSkinInfo.PerformRead(s =>
                s.Files
                 .Where(f => SupportedExtensions.IMAGE_EXTENSIONS.Contains(Path.GetExtension(f.Filename).ToLowerInvariant()))
                 .Select(f => Path.GetFileNameWithoutExtension(f.Filename))
                 .Where(f =>
                 {
                     string clean = Regex.Replace(f, @"@\d+x$", "", RegexOptions.IgnoreCase);
                     string baseName = Regex.Replace(clean, @"[-_]\d+$", "");
                     return string.Equals(clean, prefix, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(baseName, prefix, StringComparison.OrdinalIgnoreCase)
                            || clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                 })
                 .Distinct()
                 .OrderBy(f => f)
                 .ToList()
            );

            if (matchingFiles != null && matchingFiles.Count > 0)
            {
                foreach (var key in matchingFiles)
                {
                    string cleanKey = Regex.Replace(key, @"@\d+x$", "", RegexOptions.IgnoreCase);
                    var tex = currentSkin.GetTexture(cleanKey) ?? currentSkin.GetTexture(key);
                    if (tex != null && !availableTextures.Contains(tex))
                        availableTextures.Add(tex);
                }
            }

            // Fallback for custom prefix if specific prefix wasn't found in skin
            if (availableTextures.Count == 0 && prefix != "comboburst")
            {
                var fallback = currentSkin.GetTexture("comboburst");
                if (fallback != null)
                    availableTextures.Add(fallback);
            }
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
            if (availableTextures.Count == 0)
                return;

            int index;
            if (RandomizeCharacter.Value)
            {
                index = RNG.Next(availableTextures.Count);
            }
            else
            {
                index = currentTextureIndex % availableTextures.Count;
                currentTextureIndex++;
            }

            var texture = availableTextures[index];

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

            var sample = skinSource?.GetSample(new osu.Game.Audio.SampleInfo("comboburst"))
                         ?? skinManager?.CurrentSkin.Value.GetSample(new osu.Game.Audio.SampleInfo("comboburst"));
            sample?.Play();
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
                          .Select(f => Path.GetFileNameWithoutExtension(f.Filename))
                          .Distinct()).ToArray();

                if (availableFiles != null && availableFiles.Length > 0)
                {
                    var cleanOptions = availableFiles
                                       .Select(f => Regex.Replace(f, @"@\d+x$", "", RegexOptions.IgnoreCase))
                                       .Select(f => Regex.Replace(f, @"[-_]\d+$", ""))
                                       .Distinct()
                                       .OrderBy(f => f);

                    Items = cleanOptions.ToArray();
                }
            }
        }
    }
}
