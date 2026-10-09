// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Skinning
{
    public enum FollowCircleType
    {
        DefaultSkin,
        Video,
        AnimationFrames,
        SingleImage,
        SkinReference,
    }

    public class FollowCircleItem : IEquatable<FollowCircleItem>
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public FollowCircleType Type { get; set; }
        public string? FilePath { get; set; }
        public string? SkinFilename { get; set; }
        public string? StoragePath { get; set; }
        public Guid? SkinId { get; set; }
        public int FrameCount { get; set; } = 1;
        public double FrameRate { get; set; } = 60.0;
        public List<string>? FramePaths { get; set; }

        public override string ToString() => Name;

        public bool Equals(FollowCircleItem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id;
        }

        public override bool Equals(object? obj) => Equals(obj as FollowCircleItem);
        public override int GetHashCode() => Id.GetHashCode();
    }
}
