// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Skinning
{
    public partial class CustomFramesFollowCircleContent : CompositeDrawable
    {
        private readonly IReadOnlyList<Texture> textures;
        private readonly double frameLength;

        public CustomFramesFollowCircleContent(IReadOnlyList<Texture> textures, double frameLength = 1000.0 / 60.0)
        {
            this.textures = textures;
            this.frameLength = frameLength;

            Size = OsuHitObject.OBJECT_DIMENSIONS * 2;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (textures.Count == 0)
                return;

            if (textures.Count == 1)
            {
                InternalChild = new Sprite
                {
                    Texture = textures[0],
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }
            else
            {
                var anim = new TextureAnimation
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Loop = true,
                    DefaultFrameLength = frameLength,
                };

                foreach (var t in textures)
                    anim.AddFrame(t);

                InternalChild = anim;
            }
        }
    }
}
