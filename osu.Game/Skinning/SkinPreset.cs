// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using Newtonsoft.Json;

namespace osu.Game.Skinning
{
    [Serializable]
    public class SkinPreset : IEquatable<SkinPreset>
    {
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

        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

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
