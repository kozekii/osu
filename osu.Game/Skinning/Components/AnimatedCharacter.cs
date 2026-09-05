// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning.Components
{
    /// <summary>
    /// A skinnable animated character overlay that reacts dynamically to gameplay events,
    /// beatmap tempo (BPM pulse), combo milestones, kiai time, and health changes.
    /// </summary>
    public partial class AnimatedCharacter : BeatSyncedContainer, ISerialisableDrawable
    {
        public bool UsesFixedAnchor { get; set; }

        [SettingSource("Animation / Character Name", "The name or prefix of the character frames in the skin folder.", SettingControlType = typeof(CharacterSelectorControl))]
        public Bindable<string> AnimationName { get; } = new Bindable<string>(string.Empty);

        [SettingSource("Opacity", "Transparency level for the character.", SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> CharacterOpacity { get; } = new BindableFloat(1.0f)
        {
            MinValue = 0.05f,
            MaxValue = 1.0f,
            Precision = 0.05f,
        };

        [SettingSource("Frame Rate (FPS)", "Playback speed for animated frame sequences.")]
        public BindableNumber<double> FrameRate { get; } = new BindableDouble(12)
        {
            MinValue = 1,
            MaxValue = 60,
            Precision = 1,
        };

        [SettingSource("Loop Animation", "Whether the animation should continuously loop.")]
        public BindableBool LoopAnimation { get; } = new BindableBool(true);

        [SettingSource("Beat Pulse", "Subtly bounce the character in sync with the beatmap's BPM.")]
        public BindableBool EnableBeatPulse { get; } = new BindableBool(true);

        [SettingSource("Pulse Intensity", "How intensely the character bounces on each beat.", SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> PulseIntensity { get; } = new BindableFloat(0.04f)
        {
            MinValue = 0f,
            MaxValue = 0.25f,
            Precision = 0.01f,
        };

        [SettingSource("Flip Horizontally", "Mirrors the character to face the opposite direction.")]
        public BindableBool FlipHorizontally { get; } = new BindableBool(false);

        [SettingSource("Kiai Mode Effects", "Adds a dynamic celebratory glow and energetic bouncing during Kiai time.")]
        public BindableBool EnableKiaiEffects { get; } = new BindableBool(true);

        [Resolved]
        private SkinManager skinManager { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

        [Resolved]
        private TextureStore textures { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private ScoreProcessor? scoreProcessor { get; set; }

        [Resolved(canBeNull: true)]
        private HealthProcessor? healthProcessor { get; set; }

        [Resolved(canBeNull: true)]
        private IGameplayClock? gameplayClock { get; set; }

        [Resolved(canBeNull: true)]
        private GameplayUnderlayContainer? underlayContainer { get; set; }

        private Container contentContainer = null!;
        private Drawable? activeVisual;
        private int lastMilestoneCombo;

        public AnimatedCharacter()
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = contentContainer = new Container
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AnimationName.BindValueChanged(_ => Scheduler.AddOnce(reloadVisuals));
            FrameRate.BindValueChanged(_ => updateAnimationSpeed());
            LoopAnimation.BindValueChanged(_ => updateLooping());
            FlipHorizontally.BindValueChanged(flip => contentContainer.Scale = new Vector2(flip.NewValue ? -1 : 1, 1), true);

            skinManager.CurrentSkinInfo.BindValueChanged(_ => Scheduler.AddOnce(reloadVisuals));

            if (skinSource != null)
                skinSource.SourceChanged += onSkinSourceChanged;

            if (scoreProcessor != null)
                scoreProcessor.NewJudgement += onNewJudgement;
        }

        private void onSkinSourceChanged() => Scheduler.AddOnce(reloadVisuals);

        protected override void LoadComplete()
        {
            base.LoadComplete();
            reloadVisuals();

            CharacterOpacity.BindValueChanged(o => Alpha = o.NewValue, true);
        }

        private void reloadVisuals()
        {
            if (!IsLoaded || contentContainer == null)
                return;

            contentContainer.Clear();
            activeVisual = null;

            string rawName = AnimationName.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                contentContainer.Add(activeVisual = new SkinnableSprite.SpriteNotFound("character"));
                return;
            }

            // Clean name: strip extension, @2x, and trailing frame indices
            string cleanName = Path.GetFileNameWithoutExtension(rawName);
            cleanName = Regex.Replace(cleanName, @"@\d+x$", "", RegexOptions.IgnoreCase);
            string basePrefix = Regex.Replace(cleanName, @"[-_]\d+$", "");

            // 1. Try native GetAnimation
            var animDrawable = skinSource?.GetAnimation(basePrefix, animatable: true, looping: LoopAnimation.Value, frameLength: 1000.0 / Math.Max(1, FrameRate.Value))
                               ?? skinManager.CurrentSkin.Value.GetAnimation(basePrefix, animatable: true, looping: LoopAnimation.Value, frameLength: 1000.0 / Math.Max(1, FrameRate.Value));

            if (animDrawable != null)
            {
                animDrawable.Anchor = Anchor.Centre;
                animDrawable.Origin = Anchor.Centre;
                contentContainer.Add(activeVisual = animDrawable);
                return;
            }

            // 2. Multi-source Frame Sequence Search
            var discoveredTextures = new List<Texture>();

            for (int i = 0; i < 60; i++)
            {
                Texture? tex = fetchTexture($"{basePrefix}-{i}")
                               ?? fetchTexture($"{basePrefix}_{i}")
                               ?? fetchTexture($"{cleanName}-{i}")
                               ?? fetchTexture($"{cleanName}_{i}");

                if (tex != null)
                    discoveredTextures.Add(tex);
                else if (i > 0)
                    break;
            }

            if (discoveredTextures.Count > 1)
            {
                var customAnim = new TextureAnimation
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Loop = LoopAnimation.Value,
                    DefaultFrameLength = 1000.0 / Math.Max(1, FrameRate.Value),
                };

                foreach (var tex in discoveredTextures)
                    customAnim.AddFrame(tex);

                contentContainer.Add(activeVisual = customAnim);
                return;
            }

            // 3. Single Texture Sprite
            Texture? singleTexture = fetchTexture(cleanName)
                                     ?? fetchTexture(basePrefix)
                                     ?? fetchTexture(rawName);

            if (singleTexture != null)
            {
                contentContainer.Add(activeVisual = new Sprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Texture = singleTexture,
                });
                return;
            }

            contentContainer.Add(activeVisual = new SkinnableSprite.SpriteNotFound(cleanName));
        }

        private Texture? fetchTexture(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string clean = Path.GetFileNameWithoutExtension(name);
            clean = Regex.Replace(clean, @"@\d+x$", "", RegexOptions.IgnoreCase);

            Texture? tex = null;

            if (skinSource != null)
            {
                tex = skinSource.GetTexture(clean) ?? skinSource.GetTexture(name);
                if (tex != null) return tex;

                foreach (var s in skinSource.AllSources)
                {
                    tex = s.GetTexture(clean) ?? s.GetTexture(name);
                    if (tex != null) return tex;
                }
            }

            var currentSkin = skinManager?.CurrentSkin.Value;
            if (currentSkin != null)
            {
                tex = currentSkin.GetTexture(clean) ?? currentSkin.GetTexture(name);
                if (tex != null) return tex;
            }

            return textures?.Get(clean) ?? textures?.Get(name);
        }

        private void updateAnimationSpeed()
        {
            if (activeVisual is TextureAnimation anim)
                anim.DefaultFrameLength = 1000.0 / Math.Max(1, FrameRate.Value);
        }

        private void updateLooping()
        {
            if (activeVisual is TextureAnimation anim)
                anim.Loop = LoopAnimation.Value;
        }

        protected override void OnNewBeat(int beatIndex, TimingControlPoint timingPoint, EffectControlPoint effectPoint, ChannelAmplitudes amplitudes)
        {
            base.OnNewBeat(beatIndex, timingPoint, effectPoint, amplitudes);

            if (activeVisual == null)
                return;

            if (EnableBeatPulse.Value && PulseIntensity.Value > 0)
            {
                double beatDuration = timingPoint.BeatLength;
                float intensity = PulseIntensity.Value;

                if (effectPoint.KiaiMode && EnableKiaiEffects.Value)
                    intensity *= 1.8f;

                contentContainer.ScaleTo(new Vector2(FlipHorizontally.Value ? -1 : 1, 1) * (1f + intensity), 40, Easing.OutQuad)
                                .Then()
                                .ScaleTo(new Vector2(FlipHorizontally.Value ? -1 : 1, 1), beatDuration * 0.7, Easing.OutQuad);
            }

            if (effectPoint.KiaiMode && EnableKiaiEffects.Value)
            {
                contentContainer.FlashColour(Colour4.FromHex("FFE4E6"), 150, Easing.Out);
            }
        }

        private void onNewJudgement(JudgementResult result)
        {
            if (scoreProcessor == null || activeVisual == null)
                return;

            if (!result.IsHit)
            {
                contentContainer.FlashColour(Colour4.FromHex("EF4444").Opacity(0.6f), 200, Easing.Out);
                contentContainer.MoveToY(6, 40, Easing.OutQuad).Then().MoveToY(0, 150, Easing.OutQuad);
                return;
            }

            int currentCombo = scoreProcessor.Combo.Value;
            if (currentCombo > 0 && currentCombo % 100 == 0 && currentCombo > lastMilestoneCombo)
            {
                lastMilestoneCombo = currentCombo;
                contentContainer.ScaleTo(new Vector2(FlipHorizontally.Value ? -1.15f : 1.15f, 1.15f), 100, Easing.OutBack)
                                .Then()
                                .ScaleTo(new Vector2(FlipHorizontally.Value ? -1 : 1, 1), 300, Easing.OutElastic);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (scoreProcessor != null)
                scoreProcessor.NewJudgement -= onNewJudgement;

            if (skinSource != null)
                skinSource.SourceChanged -= onSkinSourceChanged;
        }

        public partial class CharacterSelectorControl : SettingsDropdown<string>
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
