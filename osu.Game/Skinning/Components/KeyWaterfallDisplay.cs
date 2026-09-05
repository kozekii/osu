// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning.Components
{
    public enum WaterfallDirection
    {
        Upward,
        Downward,
    }

    /// <summary>
    /// A visual key overlay stream (waterfall) that displays real-time scrolling bars
    /// representing the exact timing and duration of every key press.
    /// </summary>
    public partial class KeyWaterfallDisplay : CompositeDrawable, ISerialisableDrawable
    {
        public bool UsesFixedAnchor { get; set; }

        [SettingSource("Scroll Direction", "Direction that key press duration bars scroll towards.")]
        public Bindable<WaterfallDirection> Direction { get; } = new Bindable<WaterfallDirection>(WaterfallDirection.Upward);

        [SettingSource("Scroll Speed (px/s)", "Speed at which duration bars travel along the stream.")]
        public BindableNumber<double> ScrollSpeed { get; } = new BindableDouble(400)
        {
            MinValue = 100,
            MaxValue = 1200,
            Precision = 25,
        };

        [SettingSource("Stream Height", "Length of the scrolling key press track.")]
        public BindableNumber<float> StreamHeight { get; } = new BindableFloat(220)
        {
            MinValue = 100,
            MaxValue = 600,
            Precision = 10,
        };

        [SettingSource("Key Width", "Width of each key lane.")]
        public BindableNumber<float> KeyWidth { get; } = new BindableFloat(30)
        {
            MinValue = 16,
            MaxValue = 60,
            Precision = 2,
        };

        [SettingSource("Key Spacing", "Horizontal space between key lanes.")]
        public BindableNumber<float> KeySpacing { get; } = new BindableFloat(6)
        {
            MinValue = 2,
            MaxValue = 20,
            Precision = 1,
        };

        [SettingSource("Show Key Labels", "Display the key name (e.g. K1, K2) at the base.")]
        public BindableBool ShowKeyLabels { get; } = new BindableBool(true);

        [SettingSource("Show Press Counts", "Display total press counter on each key.")]
        public BindableBool ShowPressCounts { get; } = new BindableBool(true);

        [SettingSource("Corner Radius", "Roundness of the scrolling press bars.")]
        public BindableNumber<float> BarCornerRadius { get; } = new BindableFloat(4)
        {
            MinValue = 0,
            MaxValue = 12,
            Precision = 1,
        };

        [Resolved(canBeNull: true)]
        private InputCountController? controller { get; set; }

        private readonly FillFlowContainer<WaterfallLane> laneFlow;
        private readonly IBindableList<InputTrigger> triggers = new BindableList<InputTrigger>();

        private static readonly Colour4[] default_lane_colours = new[]
        {
            Colour4.FromHex("A855F7"), // Purple (K1)
            Colour4.FromHex("EC4899"), // Pink (K2)
            Colour4.FromHex("06B6D4"), // Cyan (M1)
            Colour4.FromHex("10B981"), // Green (M2)
            Colour4.FromHex("F59E0B"), // Amber (K5)
            Colour4.FromHex("3B82F6"), // Blue (K6)
            Colour4.FromHex("EF4444"), // Red (K7)
        };

        public KeyWaterfallDisplay()
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = laneFlow = new FillFlowContainer<WaterfallLane>
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            KeySpacing.BindValueChanged(s => laneFlow.Spacing = new Vector2(s.NewValue, 0), true);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (controller != null)
            {
                triggers.BindTo(controller.Triggers);
                triggers.BindCollectionChanged(onTriggersChanged, true);
            }
            else
            {
                // Preview mode in Skin Editor when not in active gameplay
                createPreviewLanes();
            }
        }

        private void onTriggersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            laneFlow.Clear();

            int index = 0;
            foreach (var trigger in triggers)
            {
                var laneColour = default_lane_colours[index % default_lane_colours.Length];
                laneFlow.Add(new WaterfallLane(trigger, laneColour, this));
                index++;
            }
        }

        private void createPreviewLanes()
        {
            laneFlow.Clear();
            var dummyTriggers = new[]
            {
                new KeyCounterKeyboardTrigger(osuTK.Input.Key.Z),
                new KeyCounterKeyboardTrigger(osuTK.Input.Key.X),
            };

            for (int i = 0; i < dummyTriggers.Length; i++)
            {
                var laneColour = default_lane_colours[i % default_lane_colours.Length];
                laneFlow.Add(new WaterfallLane(dummyTriggers[i], laneColour, this));
            }
        }

        public partial class WaterfallLane : CompositeDrawable
        {
            private readonly InputTrigger trigger;
            private readonly Colour4 laneColour;
            private readonly KeyWaterfallDisplay parent;

            private Container streamArea = null!;
            private Container keyBase = null!;
            private Box keyBackground = null!;
            private Box keyGlow = null!;
            private OsuSpriteText keyLabel = null!;
            private OsuSpriteText keyCount = null!;

            private WaterfallBar? currentActiveBar;

            public WaterfallLane(InputTrigger trigger, Colour4 laneColour, KeyWaterfallDisplay parent)
            {
                this.trigger = trigger;
                this.laneColour = laneColour;
                this.parent = parent;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Width = parent.KeyWidth.Value;
                Height = parent.StreamHeight.Value + 42; // Stream + Key button height

                InternalChildren = new Drawable[]
                {
                    streamArea = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = parent.StreamHeight.Value,
                        Masking = true,
                        Anchor = parent.Direction.Value == WaterfallDirection.Upward ? Anchor.TopLeft : Anchor.BottomLeft,
                        Origin = parent.Direction.Value == WaterfallDirection.Upward ? Anchor.TopLeft : Anchor.BottomLeft,
                        Y = parent.Direction.Value == WaterfallDirection.Upward ? 0 : 42,
                    },
                    keyBase = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 36,
                        Anchor = parent.Direction.Value == WaterfallDirection.Upward ? Anchor.BottomLeft : Anchor.TopLeft,
                        Origin = parent.Direction.Value == WaterfallDirection.Upward ? Anchor.BottomLeft : Anchor.TopLeft,
                        CornerRadius = 6,
                        Masking = true,
                        Children = new Drawable[]
                        {
                            keyBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Colour4.FromHex("1E1E2E").Opacity(0.85f),
                            },
                            keyGlow = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = laneColour,
                                Alpha = 0,
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 1),
                                Children = new Drawable[]
                                {
                                    keyLabel = new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                        Text = trigger.Name,
                                    },
                                    keyCount = new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
                                        Colour = laneColour,
                                        Text = "0",
                                    },
                                }
                            }
                        }
                    }
                };

                parent.KeyWidth.BindValueChanged(w => Width = w.NewValue);
                parent.StreamHeight.BindValueChanged(h =>
                {
                    streamArea.Height = h.NewValue;
                    Height = h.NewValue + 42;
                });

                parent.ShowKeyLabels.BindValueChanged(l => keyLabel.FadeTo(l.NewValue ? 1 : 0, 150), true);
                parent.ShowPressCounts.BindValueChanged(c => keyCount.FadeTo(c.NewValue ? 1 : 0, 150), true);

                trigger.OnActivate += onActivate;
                trigger.OnDeactivate += onDeactivate;
                trigger.ActivationCount.BindValueChanged(c => keyCount.Text = c.NewValue.ToString());
            }

            private void onActivate(bool forwardPlayback)
            {
                keyGlow.FadeTo(0.85f, 40, Easing.OutQuad);
                keyBase.ScaleTo(0.92f, 40, Easing.OutQuad);

                bool isUpward = parent.Direction.Value == WaterfallDirection.Upward;

                currentActiveBar = new WaterfallBar(laneColour, parent.BarCornerRadius.Value, isUpward)
                {
                    Anchor = isUpward ? Anchor.BottomLeft : Anchor.TopLeft,
                    Origin = isUpward ? Anchor.BottomLeft : Anchor.TopLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                };

                streamArea.Add(currentActiveBar);
            }

            private void onDeactivate(bool forwardPlayback)
            {
                keyGlow.FadeOut(120, Easing.OutQuad);
                keyBase.ScaleTo(1.0f, 100, Easing.OutElastic);

                if (currentActiveBar != null)
                {
                    currentActiveBar.IsActive = false;
                    currentActiveBar = null;
                }
            }

            protected override void Update()
            {
                base.Update();

                double deltaSec = Time.Elapsed / 1000.0;
                float speed = (float)parent.ScrollSpeed.Value;
                bool isUpward = parent.Direction.Value == WaterfallDirection.Upward;
                float streamHeight = parent.StreamHeight.Value;

                foreach (var child in streamArea.Children.OfType<WaterfallBar>().ToArray())
                {
                    if (child.IsActive)
                    {
                        // Growing bar while key is held down
                        child.Height += (float)(speed * deltaSec);
                    }
                    else
                    {
                        // Moving detached bar along stream
                        float deltaY = (float)(speed * deltaSec);
                        child.Y += isUpward ? -deltaY : deltaY;

                        // Fade out near the end of the track
                        float traveled = Math.Abs(child.Y);
                        if (traveled > streamHeight * 0.75f)
                        {
                            float fadeProgress = (traveled - streamHeight * 0.75f) / (streamHeight * 0.25f);
                            child.Alpha = Math.Max(0, 1.0f - fadeProgress);
                        }

                        if (traveled >= streamHeight + child.Height)
                        {
                            child.Expire();
                        }
                    }
                }
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                trigger.OnActivate -= onActivate;
                trigger.OnDeactivate -= onDeactivate;
            }
        }

        public partial class WaterfallBar : CompositeDrawable
        {
            public bool IsActive = true;

            public WaterfallBar(Colour4 colour, float cornerRadius, bool isUpward)
            {
                Masking = true;
                CornerRadius = cornerRadius;

                InternalChild = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colour,
                };
            }
        }
    }
}
