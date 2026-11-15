// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Skinning.Default;
using osu.Game.Rulesets.Osu.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Osu.Skinning
{
    public partial class DynamicFollowCircle : CompositeDrawable
    {
        /// <summary>
        /// How far ahead of a slider's start time its follow circle content is created.
        /// Sliders are all created up front when a beatmap loads, so materialising eagerly would
        /// open one video decoder per slider before the first note is even played.
        /// </summary>
        private const double prewarm_time = 1000;

        /// <summary>
        /// Delay after a slider's end time before its content is torn down and decoders are released.
        /// </summary>
        private const double release_delay = 2000;

        [Resolved(CanBeNull = true)]
        private FollowCircleManager? followCircleManager { get; set; }

        private Container contentContainer = null!;
        private DrawableSlider? drawableSlider;

        private bool materialised;

        public DynamicFollowCircle()
        {
            Origin = Anchor.Centre;
            Anchor = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(DrawableHitObject? hitObject)
        {
            drawableSlider = hitObject as DrawableSlider;

            InternalChild = contentContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (drawableSlider != null)
                drawableSlider.HitObjectApplied += onHitObjectApplied;

            if (followCircleManager != null)
            {
                // Named handlers so the subscriptions can be released in Dispose. These bindables live on a
                // game-wide manager, so a leaked handler would keep every disposed (pooled) slider alive and
                // invoke updateFollowCircle() on it forever.
                followCircleManager.Mode.ValueChanged += onModeChanged;
                followCircleManager.CustomFollowCircleId.ValueChanged += onCustomFollowCircleChanged;
                followCircleManager.CircularMask.ValueChanged += onCircularMaskChanged;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (drawableSlider?.HitObject == null)
                return;

            double startTime = drawableSlider.HitObject.StartTime;
            double endTime = drawableSlider.HitObject.GetEndTime();

            if (!materialised)
            {
                if (Time.Current >= startTime - prewarm_time)
                    materialise();
            }
            else if (Time.Current > endTime + release_delay)
            {
                release();
            }
        }

        private void materialise()
        {
            materialised = true;
            updateFollowCircle();
        }

        private void release()
        {
            materialised = false;
            contentContainer.Clear();
        }

        private void onModeChanged(ValueChangedEvent<FollowCircleMode> _) => onManagerSettingChanged();
        private void onCustomFollowCircleChanged(ValueChangedEvent<string> _) => onManagerSettingChanged();
        private void onCircularMaskChanged(ValueChangedEvent<bool> _) => onManagerSettingChanged();

        private void onManagerSettingChanged()
        {
            // If the circle hasn't been materialised yet it will pick up the current settings on creation.
            if (materialised)
                updateFollowCircle();
        }

        private void onHitObjectApplied(DrawableHitObject d)
        {
            // When a slider is pooled and reused for a new hitobject, cycle/random must pick a new item.
            if (materialised && followCircleManager != null
                              && (followCircleManager.Mode.Value == FollowCircleMode.Cycle || followCircleManager.Mode.Value == FollowCircleMode.Random))
            {
                updateFollowCircle();
            }
        }

        private void updateFollowCircle()
        {
            contentContainer.Clear();

            if (followCircleManager != null && followCircleManager.Mode.Value != FollowCircleMode.Default)
            {
                var item = followCircleManager.GetNextFollowCircleItem();

                if (item != null)
                {
                    Drawable? customDrawable = createDrawableForItem(item);
                    if (customDrawable != null)
                    {
                        contentContainer.Child = customDrawable;
                        return;
                    }
                }
            }

            // Fallback to standard skin lookup
            contentContainer.Child = new SkinnableDrawable(new OsuSkinComponentLookup(OsuSkinComponents.SliderFollowCircle), _ => new DefaultFollowCircle())
            {
                Origin = Anchor.Centre,
                Anchor = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };
        }

        private Drawable? createDrawableForItem(FollowCircleItem item)
        {
            switch (item.Type)
            {
                case FollowCircleType.Video:
                    var streamProvider = followCircleManager?.GetStreamProvider(item);
                    if (streamProvider != null)
                    {
                        bool mask = followCircleManager?.CircularMask.Value ?? false;
                        return new LegacyFollowCircle(new VideoFollowCircleContent(streamProvider, mask));
                    }
                    break;

                case FollowCircleType.AnimationFrames:
                case FollowCircleType.SingleImage:
                    var textures = followCircleManager?.GetTextures(item);
                    if (textures != null && textures.Count > 0)
                    {
                        return new LegacyFollowCircle(new CustomFramesFollowCircleContent(textures, 1000.0 / item.FrameRate));
                    }
                    break;

                case FollowCircleType.SkinReference:
                    var skin = followCircleManager?.GetSkinReference(item);
                    if (skin != null)
                    {
                        var comp = skin.GetDrawableComponent(new OsuSkinComponentLookup(OsuSkinComponents.SliderFollowCircle));
                        if (comp != null)
                            return comp;
                    }
                    break;

                default:
                case FollowCircleType.DefaultSkin:
                    break;
            }

            return null;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (drawableSlider != null)
                drawableSlider.HitObjectApplied -= onHitObjectApplied;

            if (followCircleManager != null)
            {
                followCircleManager.Mode.ValueChanged -= onModeChanged;
                followCircleManager.CustomFollowCircleId.ValueChanged -= onCustomFollowCircleChanged;
                followCircleManager.CircularMask.ValueChanged -= onCircularMaskChanged;
            }
        }
    }
}
