// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Scoring;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Skinning;

namespace osu.Game.Screens.RankingV2
{
    public partial class SoloResultsScreenV2 : ScreenWithBeatmapBackground, IResultsScreenV2, IKeyBindingHandler<GlobalAction>
    {
        protected const float BACKGROUND_BLUR = 10;

        public override bool DisallowExternalBeatmapRulesetChanges => true;

        public override bool? AllowGlobalTrackControl => true;

        protected override OverlayActivation InitialOverlayActivationMode => OverlayActivation.UserTriggered;

        /// <summary>
        /// Whether the user can retry the beatmap from the results screen.
        /// </summary>
        public bool AllowRetry { get; init; }

        /// <summary>
        /// Whether the user can watch the replay of the completed play from the results screen.
        /// </summary>
        public bool AllowWatchingReplay { get; init; } = true;

        /// <summary>
        /// Whether the provided score is for a local user's play.
        /// </summary>
        public bool IsLocalPlay { get; init; }

        /// <summary>
        /// Whether the entrance animation sequence should play when entering the screen.
        /// </summary>
        public bool AnimateOnEnter { get; init; } = true;

        [Cached(typeof(IBindable<IScoreInfo>))]
        private Bindable<IScoreInfo> score = new Bindable<IScoreInfo>();

        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        [Resolved]
        private Player? player { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        [Resolved]
        private ScoreManager? scoreManager { get; set; }

        [Resolved]
        private BeatmapDifficultyCache? difficultyCache { get; set; }

        private SkinnableContainer mainScreen = null!;
        private Sample? popInSample;
        private PoolableSkinnableSample? rankApplauseSound;
        private CancellationTokenSource? ppCancellationSource;
        private bool skipExitTransition;
        private bool hasAnimatedOnEnter;

        public SoloResultsScreenV2(IScoreInfo score)
        {
            this.score.Value = score;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            popInSample = audio.Samples.Get(@"UI/overlay-pop-in");

            InternalChild = new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = mainScreen = new SkinnableContainer(new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.Results))
                {
                    RelativeSizeAxes = Axes.Both,
                },
            };

            mainScreen.OnComponentsLoaded += onMainScreenComponentsLoaded;

            bool allowHotkeyRetry = (AllowWatchingReplay && player is ReplayPlayer) || (player != null && AllowRetry);

            if (allowHotkeyRetry)
            {
                AddInternal(new HotkeyRetryOverlay
                {
                    Action = () =>
                    {
                        if (!this.IsCurrentScreen()) return;

                        skipExitTransition = true;
                        player?.Restart(true);
                    },
                });
            }
        }

        private void onMainScreenComponentsLoaded(Drawable _)
        {
            if (AnimateOnEnter && !hasAnimatedOnEnter)
            {
                hasAnimatedOnEnter = true;
                StartAnimating();
            }
        }

        public void StartAnimating() => mainScreen.StartAnimationSequence();

        public void FinishAnimating()
        {
            hasAnimatedOnEnter = true;
            mainScreen.FinishAnimationSequence();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            score.BindValueChanged(_ => updateState(), true);

            bool shouldFlair = AnimateOnEnter && player != null && !score.Value.User.IsBot;
            Scheduler.AddDelayed(() => OverlayActivationMode.Value = OverlayActivation.All, shouldFlair ? 3000 : 0);

            if (shouldFlair)
                playApplause(score.Value.Rank);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            ApplyToBackground(b =>
            {
                b.BlurAmount.Value = BACKGROUND_BLUR;
                b.FadeColour(OsuColour.Gray(0.5f), 250);
            });

            popInSample?.Play();

            if (AnimateOnEnter && mainScreen.ComponentsLoaded && !hasAnimatedOnEnter)
            {
                hasAnimatedOnEnter = true;
                StartAnimating();
            }
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (base.OnExiting(e))
                return true;

            if (score.Value is ScoreInfo scoreInfo)
                scoreInfo.HitEvents.Clear();

            if (!skipExitTransition)
                this.FadeOut(100);

            rankApplauseSound?.Stop();
            return false;
        }

        private void updateState()
        {
            // `SkinnableModDisplay` binds to the global mods bindable to read mods to display
            // setting it here seems less bad than adjusting that component to receive an `IScoreInfo` and magically decide what to show.
            var ruleset = score.Value.Ruleset.CreateInstance();
            Mods.Value = score.Value.Mods.Select(m => m.ToMod(ruleset)).ToArray();

            if (score.Value is ScoreInfo scoreInfo)
            {
                populateMissingDataAsync(scoreInfo);
            }
        }

        private void populateMissingDataAsync(ScoreInfo scoreInfo)
        {
            ppCancellationSource?.Cancel();
            ppCancellationSource = new CancellationTokenSource();
            var token = ppCancellationSource.Token;

            bool needsPP = !scoreInfo.PP.HasValue && scoreInfo.BeatmapInfo != null && difficultyCache != null;
            bool needsHealthGraph = scoreInfo.HealthGraph.Count == 0 && scoreManager != null;

            if (!needsPP && !needsHealthGraph)
                return;

            Task.Run(async () =>
            {
                bool changed = false;

                if (needsHealthGraph && scoreManager != null)
                {
                    try
                    {
                        var databasedScore = scoreManager.GetScore(scoreInfo);

                        if (databasedScore?.ScoreInfo.HealthGraph.Count > 0)
                        {
                            scoreInfo.HealthGraph = databasedScore.ScoreInfo.HealthGraph;
                            changed = true;
                        }
                    }
                    catch
                    {
                        // Replay file may not exist locally; ignore.
                    }
                }

                if (needsPP && difficultyCache != null && scoreInfo.BeatmapInfo != null)
                {
                    try
                    {
                        var attributes = await difficultyCache.GetDifficultyAsync(scoreInfo.BeatmapInfo, scoreInfo.Ruleset, scoreInfo.Mods, token).ConfigureAwait(false);
                        var performanceCalculator = scoreInfo.Ruleset.CreateInstance().CreatePerformanceCalculator();

                        if (attributes?.DifficultyAttributes != null && performanceCalculator != null)
                        {
                            var result = await performanceCalculator.CalculateAsync(scoreInfo, attributes.Value.DifficultyAttributes, token).ConfigureAwait(false);
                            scoreInfo.PP = result.Total;
                            changed = true;
                        }
                    }
                    catch
                    {
                        // Ignore PP calculation cancellation or errors.
                    }
                }

                if (changed && !token.IsCancellationRequested)
                {
                    Schedule(() =>
                    {
                        if (!token.IsCancellationRequested)
                            score.TriggerChange();
                    });
                }
            }, token);
        }

        private void playApplause(ScoreRank rank)
        {
            const double applause_volume = 0.8f;

            if (!this.IsCurrentScreen())
                return;

            rankApplauseSound?.Dispose();

            var applauseSamples = new List<string>();

            if (rank >= ScoreRank.B)
                applauseSamples.Insert(0, @"applause");

            switch (rank)
            {
                default:
                case ScoreRank.D:
                    applauseSamples.Add(@"Results/applause-d");
                    break;

                case ScoreRank.C:
                    applauseSamples.Add(@"Results/applause-c");
                    break;

                case ScoreRank.B:
                    applauseSamples.Add(@"Results/applause-b");
                    break;

                case ScoreRank.A:
                    applauseSamples.Add(@"Results/applause-a");
                    break;

                case ScoreRank.S:
                case ScoreRank.SH:
                case ScoreRank.X:
                case ScoreRank.XH:
                    applauseSamples.Add(@"Results/applause-s");
                    break;
            }

            LoadComponentAsync(rankApplauseSound = new PoolableSkinnableSample(new SampleInfo(applauseSamples.ToArray())), s =>
            {
                if (!this.IsCurrentScreen() || s != rankApplauseSound)
                    return;

                AddInternal(rankApplauseSound);

                rankApplauseSound.VolumeTo(applause_volume);
                rankApplauseSound.Play();
            });
        }

        private void retry()
        {
            if (!this.IsCurrentScreen() || player == null || !AllowRetry)
                return;

            skipExitTransition = true;
            player.Restart();
        }

        private void watchReplay()
        {
            if (!this.IsCurrentScreen() || !AllowWatchingReplay)
                return;

            if (player is ReplayPlayer)
            {
                skipExitTransition = true;
                player.Restart();
                return;
            }

            game?.PresentScore(score.Value, ScorePresentType.Gameplay);
        }

        private void presentClassicResults(bool expandStatistics)
        {
            if (!this.IsCurrentScreen() || score.Value is not ScoreInfo scoreInfo)
                return;

            var classicResults = new SoloResultsScreen(scoreInfo)
            {
                AllowRetry = AllowRetry,
                AllowWatchingReplay = AllowWatchingReplay,
                IsLocalPlay = IsLocalPlay,
            };

            if (expandStatistics)
            {
                classicResults.OnLoadComplete += _ =>
                {
                    if (classicResults is ResultsScreen rs)
                        rs.OnPressed(new KeyBindingPressEvent<GlobalAction>(GetContainingInputManager()!.CurrentState, GlobalAction.Select));
                };
            }

            this.Push(classicResults);
        }

        private void presentBeatmap()
        {
            int? beatmapSetOnlineId = score.Value.Beatmap?.BeatmapSet?.OnlineID;

            if (beatmapSetOnlineId > 0)
                game?.ShowBeatmapSet(beatmapSetOnlineId.Value);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            switch (e.Action)
            {
                case GlobalAction.QuickExit:
                    if (this.IsCurrentScreen())
                    {
                        this.Exit();
                        return true;
                    }

                    break;

                case GlobalAction.Select:
                    presentClassicResults(true);
                    return true;

                case GlobalAction.ExportReplay:
                    if (score.Value is ScoreInfo scoreInfo && scoreManager != null)
                    {
                        scoreManager.Export(scoreInfo);
                        return true;
                    }

                    break;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override void Dispose(bool isDisposing)
        {
            ppCancellationSource?.Cancel();
            base.Dispose(isDisposing);
        }

        #region IResultsScreenV2

        Bindable<ScreenFooterContent?> IResultsScreenV2.FooterContent => FooterContent;

        Action? IResultsScreenV2.Retry => AllowRetry && player != null ? retry : null;

        Action? IResultsScreenV2.WatchReplay => AllowWatchingReplay ? watchReplay : null;

        Action? IResultsScreenV2.PresentRanking => () => presentClassicResults(false);

        Action? IResultsScreenV2.PresentExtendedStatistics => () => presentClassicResults(true);

        Action? IResultsScreenV2.PresentBeatmap => score.Value.Beatmap?.BeatmapSet?.OnlineID > 0 ? presentBeatmap : null;

        #endregion
    }
}
