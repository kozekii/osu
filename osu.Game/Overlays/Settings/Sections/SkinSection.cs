// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.OSD;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Skinning;
using osuTK;
using Realms;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Overlays.Settings.Sections
{
    public partial class SkinSection : SettingsSection
    {
        private static readonly SkinPreset no_preset = new SkinPreset { Id = Guid.Empty, Name = @"<Select a preset...>" };

        private PresetDropdown presetDropdown;
        private SkinDropdown skinDropdown;
        private SkinDropdown hitsoundSkinDropdown;
        private SkinDropdown cursorSkinDropdown;

        public override LocalisableString Header => SkinSettingsStrings.SkinSectionHeader;

        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = OsuIcon.SkinB
        };

        public override IEnumerable<LocalisableString> FilterTerms => base.FilterTerms.Concat(new LocalisableString[] { "skins", "hitsounds", "cursor", "cursors", "preset", "presets" });

        private readonly List<Live<SkinInfo>> dropdownItems = new List<Live<SkinInfo>>();
        private readonly List<Live<SkinInfo>> hitsoundDropdownItems = new List<Live<SkinInfo>>();
        private readonly List<Live<SkinInfo>> cursorDropdownItems = new List<Live<SkinInfo>>();

        [Resolved]
        private SkinManager skins { get; set; }

        [Resolved]
        private SkinPresetManager presetManager { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; }

        private IDisposable realmSubscription;

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load([CanBeNull] SkinEditorOverlay skinEditor)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(presetDropdown = new PresetDropdown
                {
                    AlwaysShowSearchBar = true,
                    AllowNonContiguousMatching = true,
                    Caption = "Skin preset",
                }),
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children = new Drawable[]
                    {
                        new LoadPresetButton(presetDropdown.Current) { Padding = new MarginPadding { Right = 2.5f }, RelativeSizeAxes = Axes.X, Width = 0.5f },
                        new OverwritePresetButton(presetDropdown.Current) { Padding = new MarginPadding { Left = 2.5f }, RelativeSizeAxes = Axes.X, Width = 0.5f },
                    }
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children = new Drawable[]
                    {
                        new SavePresetButton { Padding = new MarginPadding { Right = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new RenamePresetButton(presetDropdown.Current) { Padding = new MarginPadding { Horizontal = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new DeletePresetButton(presetDropdown.Current) { Padding = new MarginPadding { Left = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                    }
                },
                new SettingsItemV2(skinDropdown = new SkinDropdown
                {
                    AlwaysShowSearchBar = true,
                    AllowNonContiguousMatching = true,
                    Caption = SkinSettingsStrings.CurrentSkin,
                    Current = skins.CurrentSkinInfo,
                }),
                new SettingsItemV2(hitsoundSkinDropdown = new SkinDropdown
                {
                    AlwaysShowSearchBar = true,
                    AllowNonContiguousMatching = true,
                    Caption = "Hitsound skin",
                    Current = skins.CurrentHitsoundSkinInfo,
                }),
                new SettingsItemV2(cursorSkinDropdown = new SkinDropdown
                {
                    AlwaysShowSearchBar = true,
                    AllowNonContiguousMatching = true,
                    Caption = "Cursor skin",
                    Current = skins.CurrentCursorSkinInfo,
                }),
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Child = new CursorPreview(),
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children = new Drawable[]
                    {
                        // This is all super-temporary until we move skin settings to their own panel / overlay.
                        new RenameSkinButton { Padding = new MarginPadding { Right = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new ExportSkinButton { Padding = new MarginPadding { Horizontal = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new DeleteSkinButton { Padding = new MarginPadding { Left = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                    }
                },
                new SettingsButtonV2
                {
                    Text = SkinSettingsStrings.SkinLayoutEditor,
                    Action = () => skinEditor?.ToggleVisibility(),
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            presetManager.Presets.BindCollectionChanged((_, _) => updatePresets(), true);

            presetManager.CurrentPreset.BindValueChanged(preset =>
            {
                presetDropdown.Current.Value = preset.NewValue ?? no_preset;
            }, true);

            realmSubscription = realm.RegisterForNotifications(_ => realm.Realm.All<SkinInfo>()
                                                                         .Where(s => !s.DeletePending)
                                                                         .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase), skinsChanged);

            skinDropdown.Current.BindValueChanged(skin =>
            {
                if (skin.NewValue.ID == SkinInfo.RANDOM_SKIN)
                {
                    // before selecting random, set the skin back to the previous selection.
                    // this is done because at this point it will be random_skin_info, and would
                    // cause SelectRandomSkin to be unable to skip the previous selection.
                    skins.CurrentSkinInfo.Value = skin.OldValue;
                    skins.SelectRandomSkin();
                }

                onManualSkinConfigChange();
            });

            hitsoundSkinDropdown.Current.BindValueChanged(_ => onManualSkinConfigChange());
            cursorSkinDropdown.Current.BindValueChanged(_ => onManualSkinConfigChange());
        }

        private void onManualSkinConfigChange()
        {
            if (presetManager.CurrentPreset.Value != null)
                presetManager.CurrentPreset.Value = null;
        }

        private void updatePresets()
        {
            var list = new List<SkinPreset> { no_preset };
            list.AddRange(presetManager.Presets);

            Schedule(() =>
            {
                presetDropdown.Items = list;
                presetDropdown.Current.Value = presetManager.CurrentPreset.Value ?? no_preset;
            });
        }

        private void skinsChanged(IRealmCollection<SkinInfo> sender, ChangeSet changes)
        {
            // This can only mean that realm is recycling, else we would see the protected skins.
            // Because we are using `Live<>` in this class, we don't need to worry about this scenario too much.
            if (!sender.Any())
                return;
            // For simplicity repopulate the full list.
            dropdownItems.Clear();
            dropdownItems.AddRange(skins.GetAllUsableSkins());

            hitsoundDropdownItems.Clear();
            hitsoundDropdownItems.AddRange(skins.GetAllUsableHitsoundSkins());

            cursorDropdownItems.Clear();
            cursorDropdownItems.AddRange(skins.GetAllUsableCursorSkins());

            Schedule(() =>
            {
                skinDropdown.Items = dropdownItems;
                hitsoundSkinDropdown.Items = hitsoundDropdownItems;
                cursorSkinDropdown.Items = cursorDropdownItems;
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            realmSubscription?.Dispose();
        }

        private partial class PresetDropdown : FormDropdown<SkinPreset>
        {
            protected override LocalisableString GenerateItemText(SkinPreset item) => item?.Name ?? string.Empty;
        }

        private partial class SkinDropdown : FormDropdown<Live<SkinInfo>>
        {
            protected override LocalisableString GenerateItemText(Live<SkinInfo> item) => item.ToString();
        }

        public partial class LoadPresetButton : SettingsButtonV2
        {
            [Resolved]
            private SkinPresetManager presetManager { get; set; }

            [Resolved(CanBeNull = true)]
            private OnScreenDisplay onScreenDisplay { get; set; }

            private readonly IBindable<SkinPreset> selectedPreset;

            public LoadPresetButton(IBindable<SkinPreset> selectedPreset)
            {
                this.selectedPreset = selectedPreset;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = "Load preset";
                Action = () =>
                {
                    if (selectedPreset.Value != null && selectedPreset.Value.Id != Guid.Empty)
                    {
                        presetManager.ApplyPreset(selectedPreset.Value);
                        onScreenDisplay?.Display(new SkinPresetToast("Preset loaded", selectedPreset.Value.Name));
                    }
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                selectedPreset.BindValueChanged(preset =>
                {
                    Enabled.Value = preset.NewValue != null && preset.NewValue.Id != Guid.Empty;
                }, true);
            }
        }

        public partial class OverwritePresetButton : SettingsButtonV2
        {
            [Resolved]
            private SkinPresetManager presetManager { get; set; }

            [Resolved(CanBeNull = true)]
            private OnScreenDisplay onScreenDisplay { get; set; }

            private readonly IBindable<SkinPreset> selectedPreset;

            public OverwritePresetButton(IBindable<SkinPreset> selectedPreset)
            {
                this.selectedPreset = selectedPreset;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = "Overwrite";
                Action = () =>
                {
                    if (selectedPreset.Value != null && selectedPreset.Value.Id != Guid.Empty)
                    {
                        presetManager.OverwritePreset(selectedPreset.Value);
                        onScreenDisplay?.Display(new SkinPresetToast("Preset overwritten", selectedPreset.Value.Name));
                    }
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                selectedPreset.BindValueChanged(preset =>
                {
                    Enabled.Value = preset.NewValue != null && preset.NewValue.Id != Guid.Empty;
                }, true);
            }
        }

        public partial class SavePresetButton : SettingsButtonV2, IHasPopover
        {
            [BackgroundDependencyLoader]
            private void load()
            {
                Text = "Save new...";
                Action = this.ShowPopover;
            }

            public Popover GetPopover() => new SavePresetPopover();
        }

        public partial class SavePresetPopover : OsuPopover
        {
            [Resolved]
            private SkinPresetManager presetManager { get; set; }

            [Resolved]
            private SkinManager skins { get; set; }

            [Resolved(CanBeNull = true)]
            private OnScreenDisplay onScreenDisplay { get; set; }

            private readonly FocusedTextBox textBox;

            public SavePresetPopover()
            {
                AutoSizeAxes = Axes.Both;
                Origin = Anchor.TopCentre;

                RoundedButton saveButton;

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
                        saveButton = new RoundedButton
                        {
                            Height = 40,
                            RelativeSizeAxes = Axes.X,
                            MatchingFilter = true,
                            Text = WebCommonStrings.ButtonsSave,
                        }
                    }
                };

                saveButton.Action += save;
                textBox.OnCommit += (_, _) => save();
            }

            protected override void PopIn()
            {
                string defaultName = skins.CurrentSkinInfo.Value?.Value?.Name ?? "My Preset";
                textBox.Text = defaultName;
                textBox.TakeFocus();

                base.PopIn();
            }

            private void save()
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    presetManager.SavePreset(textBox.Text);
                    onScreenDisplay?.Display(new SkinPresetToast("Preset saved", textBox.Text));
                    PopOut();
                }
            }
        }

        public partial class RenamePresetButton : SettingsButtonV2, IHasPopover
        {
            [Resolved]
            private SkinPresetManager presetManager { get; set; }

            private readonly IBindable<SkinPreset> selectedPreset;

            public RenamePresetButton(IBindable<SkinPreset> selectedPreset)
            {
                this.selectedPreset = selectedPreset;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Rename;
                Action = this.ShowPopover;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                selectedPreset.BindValueChanged(preset =>
                {
                    Enabled.Value = preset.NewValue != null && preset.NewValue.Id != Guid.Empty;
                }, true);
            }

            public Popover GetPopover() => new RenamePresetPopover(selectedPreset.Value, presetManager);
        }

        public partial class DeletePresetButton : DangerousSettingsButtonV2
        {
            [Resolved]
            private SkinPresetManager presetManager { get; set; }

            [Resolved(CanBeNull = true)]
            private IDialogOverlay dialogOverlay { get; set; }

            private readonly IBindable<SkinPreset> selectedPreset;

            public DeletePresetButton(IBindable<SkinPreset> selectedPreset)
            {
                this.selectedPreset = selectedPreset;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = "Delete preset";
                Action = delete;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                selectedPreset.BindValueChanged(preset =>
                {
                    Enabled.Value = preset.NewValue != null && preset.NewValue.Id != Guid.Empty;
                }, true);
            }

            private void delete()
            {
                var current = selectedPreset.Value;
                if (current != null && current.Id != Guid.Empty)
                {
                    dialogOverlay?.Push(new PresetDeleteDialog(current, presetManager));
                }
            }
        }

        public partial class RenameSkinButton : SettingsButtonV2, IHasPopover
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Rename;
                Action = this.ShowPopover;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            public Popover GetPopover()
            {
                return new RenameSkinPopover();
            }
        }

        public partial class ExportSkinButton : SettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Export;
                Action = export;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            private void export()
            {
                try
                {
                    skins.ExportCurrentSkin();
                }
                catch (Exception e)
                {
                    Logger.Log($"Could not export current skin: {e.Message}", level: LogLevel.Error);
                }
            }
        }

        public partial class DeleteSkinButton : DangerousSettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            [Resolved(CanBeNull = true)]
            private IDialogOverlay dialogOverlay { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = WebCommonStrings.ButtonsDelete;
                Action = delete;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            private void delete()
            {
                dialogOverlay?.Push(new SkinDeleteDialog(currentSkin.Value));
            }
        }

        public partial class SkinDeleteDialog : DeletionDialog
        {
            private readonly Skin skin;

            public SkinDeleteDialog(Skin skin)
            {
                this.skin = skin;
                BodyText = skin.SkinInfo.Value.Name;
            }

            [BackgroundDependencyLoader]
            private void load(SkinManager manager)
            {
                DangerousAction = () =>
                {
                    manager.Delete(skin.SkinInfo.Value);
                    manager.CurrentSkinInfo.SetDefault();
                };
            }
        }

        public partial class RenameSkinPopover : OsuPopover
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private readonly FocusedTextBox textBox;

            public RenameSkinPopover()
            {
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
                            PlaceholderText = SkinSettingsStrings.SkinName,
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
                textBox.Text = skins.CurrentSkinInfo.Value.Value.Name;
                textBox.TakeFocus();

                base.PopIn();
            }

            private void rename()
            {
                skins.Rename(skins.CurrentSkinInfo.Value, textBox.Text);
                PopOut();
            }
        }
    }
}
