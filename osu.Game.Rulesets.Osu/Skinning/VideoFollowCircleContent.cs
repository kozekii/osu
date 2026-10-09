// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Video;
using osu.Framework.Timing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Skinning
{
    public partial class VideoFollowCircleContent : CompositeDrawable
    {
        private readonly Func<Stream?> streamProvider;
        private readonly bool circularMask;
        private Video? video;
        private ManualClock? manualClock;

        public VideoFollowCircleContent(Func<Stream?> streamProvider, bool circularMask = false)
        {
            this.streamProvider = streamProvider;
            this.circularMask = circularMask;

            // Follow circles are 2x the hitcircle dimensions in legacy skins (since they are scaled down by 0.5x on creation in LegacyFollowCircle)
            Size = OsuHitObject.OBJECT_DIMENSIONS * 2;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var stream = streamProvider();
            if (stream == null)
                return;

            video = new Video(stream, false)
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fill,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Clock = new FramedClock(manualClock = new ManualClock()),
                Loop = true,
            };

            if (circularMask)
            {
                InternalChild = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Masking = true,
                    Child = video,
                };
            }
            else
            {
                InternalChild = video;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (manualClock != null && Clock.ElapsedFrameTime < 100)
            {
                manualClock.CurrentTime += Clock.ElapsedFrameTime;
            }
        }
    }
}
