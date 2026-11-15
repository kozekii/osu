// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Skinning
{
    [Serializable]
    public class SkinPreset : IEquatable<SkinPreset>
    {
        /// <summary>
        /// Current on-disk schema version. Bump when the serialised shape changes so future
        /// loaders can migrate old <c>skin-presets.json</c> files.
        /// </summary>
        public const int CURRENT_SCHEMA_VERSION = 2;

        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("skin_id")]
        public Guid SkinId { get; set; }

        [JsonProperty("skin_name")]
        public string SkinName { get; set; } = string.Empty;

        [JsonProperty("hitsound_skin_id")]
        public Guid? HitsoundSkinId { get; set; }

        [JsonProperty("hitsound_skin_name")]
        public string HitsoundSkinName { get; set; } = string.Empty;

        [JsonProperty("cursor_skin_id")]
        public Guid? CursorSkinId { get; set; }

        [JsonProperty("cursor_skin_name")]
        public string CursorSkinName { get; set; } = string.Empty;

        [JsonProperty("gameplay_cursor_size")]
        public float? GameplayCursorSize { get; set; }

        [JsonProperty("menu_cursor_size")]
        public float? MenuCursorSize { get; set; }

        [JsonProperty("cursor_rotation")]
        public bool? CursorRotation { get; set; }

        [JsonProperty("auto_cursor_size")]
        public bool? AutoCursorSize { get; set; }

        /// <summary>
        /// Serialised layout JSON blobs for each <see cref="GlobalSkinnableContainers"/> target
        /// (e.g. "MainHUDComponents", "Playfield", "SongSelect"), saving characters, text, counters, etc.
        /// </summary>
        [JsonProperty("layouts_json")]
        public Dictionary<string, string> LayoutsJson { get; set; } = new Dictionary<string, string>();

        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// When the preset was last captured from the current skin selection. Separate from
        /// <see cref="CreatedAt"/> so overwriting a preset doesn't rewrite its identity.
        /// </summary>
        [JsonProperty("last_saved_at")]
        public DateTimeOffset? LastSavedAt { get; set; }

        [JsonProperty("schema_version")]
        public int SchemaVersion { get; set; } = CURRENT_SCHEMA_VERSION;

        public override string ToString() => Name;

        public bool Equals(SkinPreset other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;

            return Equals((SkinPreset)obj);
        }

        public override int GetHashCode() => Id.GetHashCode();
    }
}
