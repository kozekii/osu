// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Skinning
{
    public partial class RenamePresetPopover : OsuPopover
    {
        private readonly SkinPreset preset;
        private readonly SkinPresetManager presetManager;
        private readonly FocusedTextBox textBox;

        public RenamePresetPopover(SkinPreset preset, SkinPresetManager presetManager)
        {
            this.preset = preset;
            this.presetManager = presetManager;

            AutoSizeAxes = Axes.Both;
            Origin = Anchor.TopCentre;

            RoundedButton renameButton;

            Child = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                Width = 250,
                Spacing = new Vector2(10f),
                Children = new Drawable[]
                {
                    textBox = new FocusedTextBox
                    {
                        PlaceholderText = "Preset name",
                        FontSize = OsuFont.DEFAULT_FONT_SIZE,
                        RelativeSizeAxes = Axes.X,
                        SelectAllOnFocus = true,
                    },
                    renameButton = new RoundedButton
                    {
                        Height = 40,
                        RelativeSizeAxes = Axes.X,
                        MatchingFilter = true,
                        Text = WebCommonStrings.ButtonsSave,
                    }
                }
            };

            renameButton.Action += rename;
            textBox.OnCommit += (_, _) => rename();
        }

        protected override void PopIn()
        {
            textBox.Text = preset?.Name ?? string.Empty;
            textBox.TakeFocus();

            base.PopIn();
        }

        private void rename()
        {
            if (preset != null && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                presetManager.RenamePreset(preset, textBox.Text);
                PopOut();
            }
        }
    }
}
