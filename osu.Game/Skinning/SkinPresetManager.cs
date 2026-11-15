// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Database;

namespace osu.Game.Skinning
{
    public class SkinPresetManager
    {
        private const string preset_filename = "skin-presets.json";
        private const string preset_backup_filename = "skin-presets.backup.json";

        public BindableList<SkinPreset> Presets { get; } = new BindableList<SkinPreset>();

        public Bindable<SkinPreset> CurrentPreset { get; } = new Bindable<SkinPreset>();

        private readonly Storage storage;
        private readonly SkinManager skins;
        private readonly OsuConfigManager config;

        public SkinPresetManager(Storage storage, SkinManager skins, OsuConfigManager config)
        {
            this.storage = storage;
            this.skins = skins;
            this.config = config;

            loadPresets();
        }

        private void loadPresets()
        {
            Presets.Clear();

            try
            {
                if (!storage.Exists(preset_filename))
                    return;

                bool migrated = false;
                bool hasUnsupportedPreset = false;

                using (var stream = storage.GetStream(preset_filename, FileAccess.Read, FileMode.Open))
                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var list = JsonConvert.DeserializeObject<List<SkinPreset>>(json);

                    if (list != null)
                    {
                        foreach (var preset in list)
                        {
                            if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                                continue;

                            if (preset.SchemaVersion > SkinPreset.CURRENT_SCHEMA_VERSION)
                            {
                                // Written by a newer build. Skipping is lossless as long as we don't
                                // save over the original file, which is backed up below.
                                Logger.Log($"Skipping skin preset '{preset.Name}' with unsupported schema version {preset.SchemaVersion}.");
                                hasUnsupportedPreset = true;
                                continue;
                            }

                            if (migratePreset(preset))
                                migrated = true;

                            Presets.Add(preset);
                        }
                    }
                }

                if (hasUnsupportedPreset)
                    backupPresetFile();

                if (migrated)
                    savePresets();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load skin presets: {ex.Message}", level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Brings a preset loaded from an older file up to <see cref="SkinPreset.CURRENT_SCHEMA_VERSION"/>.
        /// Returns whether anything changed and therefore needs writing back.
        /// </summary>
        private static bool migratePreset(SkinPreset preset)
        {
            bool changed = false;

            if (preset.SchemaVersion < 1)
            {
                preset.SchemaVersion = 1;
                changed = true;
            }

            if (preset.SchemaVersion < 2)
            {
                // Before schema 2, CreatedAt doubled as the last-captured timestamp.
                preset.LastSavedAt ??= preset.CreatedAt;
                preset.SchemaVersion = 2;
                changed = true;
            }

            return changed;
        }

        private void backupPresetFile()
        {
            try
            {
                using (var source = storage.GetStream(preset_filename, FileAccess.Read, FileMode.Open))
                using (var destination = storage.GetStream(preset_backup_filename, FileAccess.Write, FileMode.Create))
                    source.CopyTo(destination);

                Logger.Log($"Skin presets written by a newer build were preserved in {preset_backup_filename}.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to back up skin presets: {ex.Message}", level: LogLevel.Error);
            }
        }

        private void savePresets()
        {
            try
            {
                using (var stream = storage.GetStream(preset_filename, FileAccess.Write, FileMode.Create))
                using (var writer = new StreamWriter(stream))
                {
                    string json = JsonConvert.SerializeObject(Presets.ToList(), Formatting.Indented);
                    writer.Write(json);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save skin presets: {ex.Message}", level: LogLevel.Error);
            }
        }

        public SkinPreset CreatePresetFromCurrent(string name)
        {
            var preset = new SkinPreset
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
            };

            captureCurrentInto(preset);
            return preset;
        }

        /// <summary>
        /// Captures the current skin, auxiliary (hitsound/cursor) skins, cursor settings and the full
        /// skin-editor layout snapshot into the supplied preset. Does not touch <see cref="SkinPreset.Id"/> or Name.
        /// </summary>
        private void captureCurrentInto(SkinPreset target)
        {
            var currentSkin = skins.CurrentSkinInfo.Value;
            var currentHitsound = skins.CurrentHitsoundSkinInfo.Value;
            var currentCursor = skins.CurrentCursorSkinInfo.Value;

            target.SkinId = currentSkin?.ID ?? SkinInfo.ARGON_SKIN;
            target.SkinName = currentSkin?.ToString() ?? string.Empty;
            target.HitsoundSkinId = currentHitsound?.ID;
            target.HitsoundSkinName = currentHitsound?.ToString() ?? string.Empty;
            target.CursorSkinId = currentCursor?.ID;
            target.CursorSkinName = currentCursor?.ToString() ?? string.Empty;
            target.GameplayCursorSize = config.Get<float>(OsuSetting.GameplayCursorSize);
            target.MenuCursorSize = config.Get<float>(OsuSetting.MenuCursorSize);
            target.CursorRotation = config.Get<bool>(OsuSetting.CursorRotation);
            target.AutoCursorSize = config.Get<bool>(OsuSetting.AutoCursorSize);

            var now = DateTimeOffset.UtcNow;
            target.LastSavedAt = now;

            if (target.CreatedAt == default)
                target.CreatedAt = now;

            target.SchemaVersion = SkinPreset.CURRENT_SCHEMA_VERSION;

            // Capture full skin editor layout customizations (characters, text, counters, HUD, etc.)
            target.LayoutsJson.Clear();
            var skinInstance = skins.CurrentSkin.Value;
            if (skinInstance != null)
            {
                foreach (var kvp in skinInstance.LayoutInfos)
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(kvp.Value, new JsonSerializerSettings { Formatting = Formatting.Indented });
                        target.LayoutsJson[kvp.Key.ToString()] = json;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to serialize layout for {kvp.Key}: {ex.Message}", level: LogLevel.Error);
                    }
                }
            }
        }

        public void SavePreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            string trimmedName = name.Trim();
            var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, trimmedName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                captureCurrentInto(existing);
                savePresets();
                CurrentPreset.Value = existing;
            }
            else
            {
                var preset = CreatePresetFromCurrent(trimmedName);
                Presets.Add(preset);
                savePresets();
                CurrentPreset.Value = preset;
            }
        }

        /// <summary>
        /// Always creates a brand new preset, guaranteeing a unique name. Never overwrites an existing preset.
        /// </summary>
        public SkinPreset SaveNewPreset(string name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim();

            string uniqueName = baseName;
            int suffix = 2;
            while (Presets.Any(p => string.Equals(p.Name, uniqueName, StringComparison.OrdinalIgnoreCase)))
                uniqueName = $"{baseName} ({suffix++})";

            var preset = CreatePresetFromCurrent(uniqueName);
            Presets.Add(preset);
            savePresets();
            CurrentPreset.Value = preset;
            return preset;
        }

        public void OverwritePreset(SkinPreset preset)
        {
            if (preset == null)
                return;

            var matching = Presets.FirstOrDefault(p => p.Id == preset.Id);
            if (matching == null)
                return;

            captureCurrentInto(matching);
            savePresets();
            CurrentPreset.Value = matching;
        }

        public void RenamePreset(SkinPreset preset, string newName)
        {
            if (preset == null || string.IsNullOrWhiteSpace(newName))
                return;

            var matching = Presets.FirstOrDefault(p => p.Id == preset.Id);
            if (matching != null)
            {
                matching.Name = newName.Trim();
                savePresets();

                int index = Presets.IndexOf(matching);
                if (index != -1)
                {
                    Presets.RemoveAt(index);
                    Presets.Insert(index, matching);
                }

                if (CurrentPreset.Value?.Id == matching.Id)
                    CurrentPreset.Value = matching;
            }
        }

        public void DeletePreset(SkinPreset preset)
        {
            if (preset == null)
                return;

            var matching = Presets.FirstOrDefault(p => p.Id == preset.Id);
            if (matching != null)
            {
                Presets.Remove(matching);
                savePresets();
            }

            if (CurrentPreset.Value?.Id == preset.Id)
                CurrentPreset.Value = null;
        }

        public void ApplyPreset(SkinPreset preset)
        {
            if (preset == null)
                return;

            // Apply visual skin.
            var targetSkin = skins.GetAllUsableSkins().FirstOrDefault(s => s.ID == preset.SkinId);
            bool switchingSkin = targetSkin != null && skins.CurrentSkinInfo.Value?.ID != targetSkin.ID;

            if (switchingSkin)
                skins.CurrentSkinInfo.Value = targetSkin!;

            // Layout customizations (characters, text, HUD components, etc.) are captured from a specific
            // skin and must be written back to that same skin instance. When a skin switch is required,
            // CurrentSkin.Value is only updated asynchronously, so defer the layout write until it lands -
            // otherwise we clobber the previously-active skin instance's in-memory layout and persist that
            // corruption to disk via SkinManager.Save().
            if (preset.LayoutsJson != null && preset.LayoutsJson.Count > 0)
            {
                if (switchingSkin)
                    applyLayoutsAfterSkinSwitch(preset);
                else
                    applyLayouts(preset);
            }

            // Apply hitsound skin
            if (preset.HitsoundSkinId.HasValue && preset.HitsoundSkinId.Value != SkinInfo.USE_CURRENT_SKIN)
            {
                var targetHitsound = skins.GetAllUsableHitsoundSkins().FirstOrDefault(s => s.ID == preset.HitsoundSkinId.Value);
                skins.CurrentHitsoundSkinInfo.Value = targetHitsound ?? skins.GetAllUsableHitsoundSkins().FirstOrDefault(s => s.ID == SkinInfo.USE_CURRENT_SKIN);
            }
            else
            {
                skins.CurrentHitsoundSkinInfo.Value = skins.GetAllUsableHitsoundSkins().FirstOrDefault(s => s.ID == SkinInfo.USE_CURRENT_SKIN);
            }

            // Apply cursor skin
            if (preset.CursorSkinId.HasValue && preset.CursorSkinId.Value != SkinInfo.USE_CURRENT_SKIN)
            {
                var targetCursor = skins.GetAllUsableCursorSkins().FirstOrDefault(s => s.ID == preset.CursorSkinId.Value);
                skins.CurrentCursorSkinInfo.Value = targetCursor ?? skins.GetAllUsableCursorSkins().FirstOrDefault(s => s.ID == SkinInfo.USE_CURRENT_SKIN);
            }
            else
            {
                skins.CurrentCursorSkinInfo.Value = skins.GetAllUsableCursorSkins().FirstOrDefault(s => s.ID == SkinInfo.USE_CURRENT_SKIN);
            }

            // Apply cursor settings
            if (preset.GameplayCursorSize.HasValue)
                config.SetValue(OsuSetting.GameplayCursorSize, preset.GameplayCursorSize.Value);

            if (preset.MenuCursorSize.HasValue)
                config.SetValue(OsuSetting.MenuCursorSize, preset.MenuCursorSize.Value);

            if (preset.CursorRotation.HasValue)
                config.SetValue(OsuSetting.CursorRotation, preset.CursorRotation.Value);

            if (preset.AutoCursorSize.HasValue)
                config.SetValue(OsuSetting.AutoCursorSize, preset.AutoCursorSize.Value);

            CurrentPreset.Value = preset;
        }

        private void applyLayouts(SkinPreset preset)
        {
            var activeSkin = skins.CurrentSkin.Value;

            // Guard: only write to the skin instance this preset was captured from.
            if (activeSkin == null || activeSkin.SkinInfo?.ID != preset.SkinId)
                return;

            activeSkin.LayoutInfos.Clear();

            foreach (var kvp in preset.LayoutsJson)
            {
                if (Enum.TryParse<GlobalSkinnableContainers>(kvp.Key, out var container))
                {
                    try
                    {
                        var layoutInfo = JsonConvert.DeserializeObject<SkinLayoutInfo>(kvp.Value);
                        if (layoutInfo != null)
                            activeSkin.LayoutInfos[container] = layoutInfo;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to deserialize layout for {kvp.Key}: {ex.Message}", level: LogLevel.Error);
                    }
                }
            }

            skins.Save(activeSkin);
            skins.CurrentSkin.TriggerChange();
        }

        private void applyLayoutsAfterSkinSwitch(SkinPreset preset)
        {
            // If the target instance is somehow already active, apply immediately.
            if (skins.CurrentSkin.Value?.SkinInfo?.ID == preset.SkinId)
            {
                applyLayouts(preset);
                return;
            }

            // CurrentSkin.Value updates asynchronously after CurrentSkinInfo changes; wait for the target
            // skin instance before writing layouts so we mutate the correct skin.
            void onSkinChanged(ValueChangedEvent<Skin> e)
            {
                if (e.NewValue?.SkinInfo?.ID != preset.SkinId)
                    return;

                skins.CurrentSkin.ValueChanged -= onSkinChanged;
                applyLayouts(preset);
            }

            skins.CurrentSkin.ValueChanged += onSkinChanged;
        }
    }
}
