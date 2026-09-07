// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Game.Overlays.Dialog;

namespace osu.Game.Skinning
{
    public partial class PresetDeleteDialog : DeletionDialog
    {
        public PresetDeleteDialog(SkinPreset preset, SkinPresetManager manager)
        {
            BodyText = preset.Name;
            DangerousAction = () => manager.DeletePreset(preset);
        }
    }
}
