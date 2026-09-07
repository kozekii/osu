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

                using (var stream = storage.GetStream(preset_filename, FileAccess.Read, FileMode.Open))
                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var list = JsonConvert.DeserializeObject<List<SkinPreset>>(json);

                    if (list != null)
                    {
                        foreach (var preset in list)
                        {
                            if (preset != null && !string.IsNullOrWhiteSpace(preset.Name))
                                Presets.Add(preset);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load skin presets: {ex.Message}", level: LogLevel.Error);
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
            var currentSkin = skins.CurrentSkinInfo.Value;
            var currentHitsound = skins.CurrentHitsoundSkinInfo.Value;
            var currentCursor = skins.CurrentCursorSkinInfo.Value;

            var preset = new SkinPreset
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                SkinId = currentSkin?.ID ?? SkinInfo.ARGON_SKIN,
                SkinName = currentSkin?.ToString() ?? string.Empty,
                HitsoundSkinId = currentHitsound?.ID,
                HitsoundSkinName = currentHitsound?.ToString() ?? string.Empty,
                CursorSkinId = currentCursor?.ID,
                CursorSkinName = currentCursor?.ToString() ?? string.Empty,
                GameplayCursorSize = config.Get<float>(OsuSetting.GameplayCursorSize),
                MenuCursorSize = config.Get<float>(OsuSetting.MenuCursorSize),
                CursorRotation = config.Get<bool>(OsuSetting.CursorRotation),
                AutoCursorSize = config.Get<bool>(OsuSetting.AutoCursorSize),
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Capture full skin editor layout customizations (characters, text, counters, HUD, etc.)
            var skinInstance = skins.CurrentSkin.Value;
            if (skinInstance != null)
            {
                foreach (var kvp in skinInstance.LayoutInfos)
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(kvp.Value, new JsonSerializerSettings { Formatting = Formatting.Indented });
                        preset.LayoutsJson[kvp.Key.ToString()] = json;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to serialize layout for {kvp.Key}: {ex.Message}", level: LogLevel.Error);
                    }
                }
            }

            return preset;
        }

        public void SavePreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            string trimmedName = name.Trim();
            var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, trimmedName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var currentSkin = skins.CurrentSkinInfo.Value;
                var currentHitsound = skins.CurrentHitsoundSkinInfo.Value;
                var currentCursor = skins.CurrentCursorSkinInfo.Value;

                existing.SkinId = currentSkin?.ID ?? SkinInfo.ARGON_SKIN;
                existing.SkinName = currentSkin?.ToString() ?? string.Empty;
                existing.HitsoundSkinId = currentHitsound?.ID;
                existing.HitsoundSkinName = currentHitsound?.ToString() ?? string.Empty;
                existing.CursorSkinId = currentCursor?.ID;
                existing.CursorSkinName = currentCursor?.ToString() ?? string.Empty;
                existing.GameplayCursorSize = config.Get<float>(OsuSetting.GameplayCursorSize);
                existing.MenuCursorSize = config.Get<float>(OsuSetting.MenuCursorSize);
                existing.CursorRotation = config.Get<bool>(OsuSetting.CursorRotation);
                existing.AutoCursorSize = config.Get<bool>(OsuSetting.AutoCursorSize);
                existing.CreatedAt = DateTimeOffset.UtcNow;

                existing.LayoutsJson.Clear();
                var skinInstance = skins.CurrentSkin.Value;
                if (skinInstance != null)
                {
                    foreach (var kvp in skinInstance.LayoutInfos)
                    {
                        try
                        {
                            string json = JsonConvert.SerializeObject(kvp.Value, new JsonSerializerSettings { Formatting = Formatting.Indented });
                            existing.LayoutsJson[kvp.Key.ToString()] = json;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Failed to serialize layout for {kvp.Key}: {ex.Message}", level: LogLevel.Error);
                        }
                    }
                }

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

        public void OverwritePreset(SkinPreset preset)
        {
            if (preset == null)
                return;

            var matching = Presets.FirstOrDefault(p => p.Id == preset.Id);
            if (matching == null)
                return;

            var currentSkin = skins.CurrentSkinInfo.Value;
            var currentHitsound = skins.CurrentHitsoundSkinInfo.Value;
            var currentCursor = skins.CurrentCursorSkinInfo.Value;

            matching.SkinId = currentSkin?.ID ?? SkinInfo.ARGON_SKIN;
            matching.SkinName = currentSkin?.ToString() ?? string.Empty;
            matching.HitsoundSkinId = currentHitsound?.ID;
            matching.HitsoundSkinName = currentHitsound?.ToString() ?? string.Empty;
            matching.CursorSkinId = currentCursor?.ID;
            matching.CursorSkinName = currentCursor?.ToString() ?? string.Empty;
            matching.GameplayCursorSize = config.Get<float>(OsuSetting.GameplayCursorSize);
            matching.MenuCursorSize = config.Get<float>(OsuSetting.MenuCursorSize);
            matching.CursorRotation = config.Get<bool>(OsuSetting.CursorRotation);
            matching.AutoCursorSize = config.Get<bool>(OsuSetting.AutoCursorSize);
            matching.CreatedAt = DateTimeOffset.UtcNow;

            matching.LayoutsJson.Clear();
            var skinInstance = skins.CurrentSkin.Value;
            if (skinInstance != null)
            {
                foreach (var kvp in skinInstance.LayoutInfos)
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(kvp.Value, new JsonSerializerSettings { Formatting = Formatting.Indented });
                        matching.LayoutsJson[kvp.Key.ToString()] = json;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to serialize layout for {kvp.Key}: {ex.Message}", level: LogLevel.Error);
                    }
                }
            }

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

            // Apply visual skin
            var targetSkin = skins.GetAllUsableSkins().FirstOrDefault(s => s.ID == preset.SkinId);
            if (targetSkin != null)
            {
                if (skins.CurrentSkinInfo.Value?.ID != targetSkin.ID)
                    skins.CurrentSkinInfo.Value = targetSkin;
            }

            // Apply layout customizations (characters, text, HUD components, etc.)
            if (preset.LayoutsJson != null && preset.LayoutsJson.Count > 0)
            {
                var activeSkin = skins.CurrentSkin.Value;
                if (activeSkin != null)
                {
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
    }
}
