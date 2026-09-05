// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// A system mod automatically applied to scores set on custom development client builds.
    /// </summary>
    public class ModDevClient : Mod, IApplicableMod
    {
        public sealed override string Name => "Development Client";
        public sealed override string Acronym => "DEV";
        public sealed override IconUsage? Icon => FontAwesome.Solid.Code;
        public sealed override LocalisableString Description => "Played on a custom development client build.";
        public sealed override ModType Type => ModType.System;
        public sealed override bool ValidForMultiplayer => false;
        public sealed override bool ValidForMultiplayerAsFreeMod => false;
        public sealed override bool AlwaysValidForSubmission => true;
    }
}
