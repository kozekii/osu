// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens.Edit.Components.Menus;

namespace osu.Game.Skinning
{
    public class SkinPresetMenuItem : EditorMenuItem
    {
        public SkinPreset Preset { get; }

        public SkinPresetMenuItem(SkinPreset preset, Action action)
            : base(preset.Name, MenuItemType.Standard, action)
        {
            Preset = preset;
        }
    }
}
