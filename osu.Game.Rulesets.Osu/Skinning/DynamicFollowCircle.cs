// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Skinning.Default;
using osu.Game.Rulesets.Osu.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Osu.Skinning
{
    public partial class DynamicFollowCircle : CompositeDrawable
    {
        [Resolved(CanBeNull = true)]
        private FollowCircleManager? followCircleManager { get; set; }

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        private Container contentContainer = null!;
        private DrawableSlider? drawableSlider;

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

            updateFollowCircle();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (drawableSlider != null)
                drawableSlider.HitObjectApplied += onHitObjectApplied;

            if (followCircleManager != null)
            {
                followCircleManager.Mode.BindValueChanged(_ => updateFollowCircle());
                followCircleManager.CustomFollowCircleId.BindValueChanged(_ => updateFollowCircle());
                followCircleManager.CircularMask.BindValueChanged(_ => updateFollowCircle());
            }
        }

        private void onHitObjectApplied(DrawableHitObject d)
        {
            // When slider is pooled and reused for a new hitobject, update follow circle if in Cycle or Random mode!
            if (followCircleManager != null && (followCircleManager.Mode.Value == FollowCircleMode.Cycle || followCircleManager.Mode.Value == FollowCircleMode.Random))
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
                    if (item.SkinId.HasValue)
                    {
                        var targetSkin = skins.GetAllUsableSkins().FirstOrDefault(s => s.ID == item.SkinId.Value);
                        if (targetSkin != null)
                        {
                            var skinInstance = targetSkin.PerformRead(skins.GetSkin);
                            if (skinInstance != null)
                            {
                                var comp = skinInstance.GetDrawableComponent(new OsuSkinComponentLookup(OsuSkinComponents.SliderFollowCircle));
                                if (comp != null)
                                    return comp;
                            }
                        }
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
        }
    }
}
