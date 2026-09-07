// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Localisation;
using osu.Game.Overlays.OSD;

namespace osu.Game.Skinning
{
    public partial class SkinPresetToast : Toast
    {
        public SkinPresetToast(LocalisableString value, string presetName)
            : base(SkinSettingsStrings.SkinSectionHeader, value)
        {
            ExtraText = presetName;
        }
    }
}
