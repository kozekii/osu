// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning.Components
{
    /// <summary>
    /// An ultra-clean, minimal stream/live counter with zero rolling animations,
    /// displaying real-time Score, Accuracy, Combo, Live PP, and Unstable Rate (UR).
    /// </summary>
    public partial class MinimalStreamDisplay : CompositeDrawable, ISerialisableDrawable
    {
        public bool UsesFixedAnchor { get; set; }

        [SettingSource("Layout Direction", "Whether stats are laid out horizontally in a single strip or stacked vertically.")]
        public Bindable<FillDirection> LayoutDirection { get; } = new Bindable<FillDirection>(FillDirection.Horizontal);

        [SettingSource("Show Labels", "Display short title labels before each number (e.g. 'ACC 99.4%').")]
        public BindableBool ShowLabels { get; } = new BindableBool(false);

        [SettingSource("Show Score", "Display current score.")]
        public BindableBool ShowScore { get; } = new BindableBool(true);

        [SettingSource("Show Accuracy", "Display current accuracy percentage.")]
        public BindableBool ShowAccuracy { get; } = new BindableBool(true);

        [SettingSource("Show Combo", "Display current combo.")]
        public BindableBool ShowCombo { get; } = new BindableBool(true);

        [SettingSource("Show Max Combo", "Append the max combo reached (e.g. 1420x / 2150x).")]
        public BindableBool ShowMaxCombo { get; } = new BindableBool(false);

        [SettingSource("Show Live PP", "Display real-time estimated Performance Points (PP).")]
        public BindableBool ShowPP { get; } = new BindableBool(true);

        [SettingSource("Show Unstable Rate", "Display real-time Unstable Rate (UR).")]
        public BindableBool ShowUR { get; } = new BindableBool(true);

        [SettingSource("Show Grade / Rank", "Display current live grade letter (SS, S, A, etc.).")]
        public BindableBool ShowGrade { get; } = new BindableBool(false);

        [SettingSource("Font Size", "Text size for the counter readout.")]
        public BindableNumber<float> TextSize { get; } = new BindableFloat(20f)
        {
            MinValue = 12f,
            MaxValue = 48f,
            Precision = 1f,
        };

        [SettingSource("Separator", "Separator character between stats in horizontal mode.")]
        public Bindable<string> Separator { get; } = new Bindable<string>(" • ");

        [SettingSource("Background Opacity", "Dark plate opacity behind the counter.", SettingControlType = typeof(SettingsPercentageSlider<float>))]
        public BindableNumber<float> BackgroundOpacity { get; } = new BindableFloat(0.55f)
        {
            MinValue = 0f,
            MaxValue = 1f,
            Precision = 0.05f,
        };

        [SettingSource("Corner Radius", "Roundness of the background plate corners.")]
        public BindableNumber<float> BackgroundCornerRadius { get; } = new BindableFloat(8f)
        {
            MinValue = 0f,
            MaxValue = 24f,
            Precision = 1f,
        };

        [Resolved(canBeNull: true)]
        private ScoreProcessor? scoreProcessor { get; set; }

        [Resolved(canBeNull: true)]
        private GameplayState? gameplayState { get; set; }

        private Box backgroundBox = null!;
        private Container plateContainer = null!;
        private FillFlowContainer itemsContainer = null!;
        private OsuSpriteText displayText = null!;

        [CanBeNull]
        private List<TimedDifficultyAttributes>? timedAttributes;
        private readonly CancellationTokenSource loadCancellationSource = new CancellationTokenSource();
        private PerformanceCalculator? performanceCalculator;
        private ScoreInfo? scoreInfo;
        private HitEventExtensions.UnstableRateCalculationResult? unstableRateResult;
        private int currentPP;
        private double? currentUR;

        public MinimalStreamDisplay()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load([CanBeNull] BeatmapDifficultyCache? difficultyCache)
        {
            InternalChild = plateContainer = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = BackgroundCornerRadius.Value,
                Children = new Drawable[]
                {
                    backgroundBox = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = BackgroundOpacity.Value,
                    },
                    itemsContainer = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = LayoutDirection.Value,
                        Padding = new MarginPadding(8),
                        Spacing = new Vector2(8, 4),
                        Child = displayText = new OsuSpriteText
                        {
                            Font = OsuFont.Torus.With(size: TextSize.Value, weight: FontWeight.SemiBold, fixedWidth: true),
                            Colour = Color4.White,
                        }
                    }
                }
            };

            if (gameplayState != null && difficultyCache != null)
            {
                performanceCalculator = gameplayState.Ruleset.CreatePerformanceCalculator();
                var clonedMods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();
                scoreInfo = new ScoreInfo(gameplayState.Score.ScoreInfo.BeatmapInfo, gameplayState.Score.ScoreInfo.Ruleset) { Mods = clonedMods };

                var gameplayWorkingBeatmap = new GameplayWorkingBeatmap(gameplayState.Beatmap);
                difficultyCache.GetTimedDifficultyAttributesAsync(gameplayWorkingBeatmap, gameplayState.Ruleset, clonedMods, loadCancellationSource.Token)
                               .ContinueWith(task => Schedule(() =>
                               {
                                   timedAttributes = task.GetResultSafely();
                                   updateDisplay();
                               }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            TextSize.BindValueChanged(size => displayText.Font = displayText.Font.With(size: size.NewValue), true);
            BackgroundOpacity.BindValueChanged(opacity => backgroundBox.Alpha = opacity.NewValue, true);
            BackgroundCornerRadius.BindValueChanged(radius => plateContainer.CornerRadius = radius.NewValue, true);
            LayoutDirection.BindValueChanged(_ => updateDisplay(), true);
            ShowScore.BindValueChanged(_ => updateDisplay(), true);
            ShowAccuracy.BindValueChanged(_ => updateDisplay(), true);
            ShowCombo.BindValueChanged(_ => updateDisplay(), true);
            ShowMaxCombo.BindValueChanged(_ => updateDisplay(), true);
            ShowPP.BindValueChanged(_ => updateDisplay(), true);
            ShowUR.BindValueChanged(_ => updateDisplay(), true);
            ShowGrade.BindValueChanged(_ => updateDisplay(), true);
            ShowLabels.BindValueChanged(_ => updateDisplay(), true);
            Separator.BindValueChanged(_ => updateDisplay(), true);

            if (scoreProcessor != null)
            {
                scoreProcessor.Combo.BindValueChanged(_ => updateDisplay());
                scoreProcessor.TotalScore.BindValueChanged(_ => updateDisplay());
                scoreProcessor.Accuracy.BindValueChanged(_ => updateDisplay());
                scoreProcessor.Rank.BindValueChanged(_ => updateDisplay());
                scoreProcessor.NewJudgement += onNewJudgement;
                scoreProcessor.JudgementReverted += onNewJudgement;
            }

            updateDisplay();
        }

        private void onNewJudgement(JudgementResult judgement)
        {
            if (HitEventExtensions.AffectsUnstableRate(judgement.HitObject, judgement.Type))
            {
                unstableRateResult = scoreProcessor?.HitEvents.CalculateUnstableRate(unstableRateResult);
                currentUR = unstableRateResult?.Result;
            }

            if (performanceCalculator != null && scoreProcessor != null && scoreInfo != null && timedAttributes != null)
            {
                var attrib = getAttributeAtTime(judgement);
                if (attrib != null)
                {
                    scoreProcessor.PopulateScore(scoreInfo);
                    currentPP = (int)Math.Round(performanceCalculator.Calculate(scoreInfo, attrib).Total, MidpointRounding.AwayFromZero);
                }
            }

            Scheduler.AddOnce(updateDisplay);
        }

        [CanBeNull]
        private DifficultyAttributes? getAttributeAtTime(JudgementResult judgement)
        {
            if (timedAttributes == null || timedAttributes.Count == 0)
                return null;

            int attribIndex = timedAttributes.BinarySearch(new TimedDifficultyAttributes(judgement.HitObject.GetEndTime(), null));
            if (attribIndex < 0)
                attribIndex = ~attribIndex - 1;

            return timedAttributes[Math.Clamp(attribIndex, 0, timedAttributes.Count - 1)].Attributes;
        }

        private void updateDisplay()
        {
            if (displayText == null)
                return;

            var parts = new List<string>();

            bool isPreview = scoreProcessor == null;

            long score = isPreview ? 12450290 : scoreProcessor!.TotalScore.Value;
            double accuracy = isPreview ? 0.9945 : scoreProcessor!.Accuracy.Value;
            int combo = isPreview ? 1420 : scoreProcessor!.Combo.Value;
            int maxCombo = isPreview ? 2150 : scoreProcessor!.HighestCombo.Value;
            int pp = isPreview ? 452 : currentPP;
            double? ur = isPreview ? 85.20 : currentUR;
            ScoreRank rank = isPreview ? ScoreRank.S : scoreProcessor!.Rank.Value;

            if (ShowScore.Value)
            {
                string label = ShowLabels.Value ? "SCORE " : string.Empty;
                parts.Add($"{label}{score:N0}");
            }

            if (ShowAccuracy.Value)
            {
                string label = ShowLabels.Value ? "ACC " : string.Empty;
                parts.Add($"{label}{accuracy * 100:0.00}%");
            }

            if (ShowCombo.Value)
            {
                string label = ShowLabels.Value ? "COMBO " : string.Empty;
                if (ShowMaxCombo.Value)
                    parts.Add($"{label}{combo:N0}x / {maxCombo:N0}x");
                else
                    parts.Add($"{label}{combo:N0}x");
            }

            if (ShowPP.Value)
            {
                string label = ShowLabels.Value ? "PP " : string.Empty;
                parts.Add($"{label}{pp:N0}pp");
            }

            if (ShowUR.Value)
            {
                string label = ShowLabels.Value ? "UR " : string.Empty;
                string urStr = ur.HasValue ? $"{ur.Value:0.00}" : "-";
                parts.Add($"{label}{urStr} UR");
            }

            if (ShowGrade.Value)
            {
                parts.Add(rank.ToString());
            }

            if (parts.Count == 0)
            {
                displayText.Text = string.Empty;
                return;
            }

            if (LayoutDirection.Value == FillDirection.Horizontal)
            {
                string sep = Separator.Value ?? " • ";
                displayText.Text = string.Join(sep, parts);
            }
            else
            {
                displayText.Text = string.Join(Environment.NewLine, parts);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (scoreProcessor != null)
            {
                scoreProcessor.NewJudgement -= onNewJudgement;
                scoreProcessor.JudgementReverted -= onNewJudgement;
            }

            loadCancellationSource.Cancel();
        }

        private class GameplayWorkingBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap gameplayBeatmap;

            public GameplayWorkingBeatmap(IBeatmap gameplayBeatmap)
                : base(gameplayBeatmap.BeatmapInfo, null)
            {
                this.gameplayBeatmap = gameplayBeatmap;
            }

            public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
                => gameplayBeatmap;

            protected override IBeatmap GetBeatmap() => gameplayBeatmap;

            public override Texture GetBackground() => throw new NotImplementedException();

            protected override Track GetBeatmapTrack() => throw new NotImplementedException();

            protected internal override ISkin GetSkin() => throw new NotImplementedException();

            public override Stream GetStream(string storagePath) => throw new NotImplementedException();
        }
    }
}
