// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Skinning
{
    public enum FollowCircleMode
    {
        [Description("Default (Skin)")]
        Default,

        [Description("Cycle (Rotate per slider)")]
        Cycle,

        [Description("Random (Randomize per slider)")]
        Random,

        [Description("Selected")]
        Selected,
    }
}
