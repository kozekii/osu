// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.IO;

namespace osu.Game.Skinning
{
    public class FollowCircleManager
    {
        private const string follow_circles_folder = "followcircles";

        public BindableList<FollowCircleItem> AvailableFollowCircles { get; } = new BindableList<FollowCircleItem>();

        public Bindable<FollowCircleMode> Mode { get; } = new Bindable<FollowCircleMode>();

        public Bindable<string> CustomFollowCircleId { get; } = new Bindable<string>();

        public Bindable<bool> CircularMask { get; } = new Bindable<bool>();

        private readonly Storage storage;
        private readonly SkinManager skins;
        private readonly OsuConfigManager config;
        private readonly TextureStore textureStore;

        private readonly Dictionary<string, Texture[]> frameCache = new Dictionary<string, Texture[]>();
        private int cycleCounter = -1;

        public FollowCircleManager(Storage storage, SkinManager skins, OsuConfigManager config)
        {
            this.storage = storage;
            this.skins = skins;
            this.config = config;

            var resourceProvider = (IStorageResourceProvider)skins;
            textureStore = new TextureStore(resourceProvider.Renderer, resourceProvider.CreateTextureLoaderStore(new DiskFileResourceStore()));

            config.BindWith(OsuSetting.FollowCircleMode, Mode);
            config.BindWith(OsuSetting.CustomFollowCircle, CustomFollowCircleId);
            config.BindWith(OsuSetting.FollowCircleCircularMask, CircularMask);

            skins.CurrentSkin.BindValueChanged(_ => RefreshAvailableFollowCircles(), true);
        }

        public void OpenFolder()
        {
            try
            {
                string path = storage.GetStorageForDirectory(follow_circles_folder).GetFullPath(string.Empty);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to open follow circles folder: {ex.Message}", level: LogLevel.Error);
            }
        }

        public void RefreshAvailableFollowCircles()
        {
            AvailableFollowCircles.Clear();
            frameCache.Clear();

            var list = new List<FollowCircleItem>();

            // 1. Default (Active Skin)
            list.Add(new FollowCircleItem
            {
                Id = "default_skin",
                Name = $"Active Skin ({skins.CurrentSkinInfo.Value?.Value?.Name ?? "Default"})",
                Type = FollowCircleType.DefaultSkin,
            });

            // 2. Discover in user storage directory "followcircles"
            try
            {
                var fcStorage = storage.GetStorageForDirectory(follow_circles_folder);
                string basePath = fcStorage.GetFullPath(string.Empty);

                if (Directory.Exists(basePath))
                {
                    var dirInfo = new DirectoryInfo(basePath);

                    // Video files in followcircles folder
                    foreach (var file in dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly))
                    {
                        if (isVideoExtension(file.Extension))
                        {
                            list.Add(new FollowCircleItem
                            {
                                Id = $"user_video_{file.Name}",
                                Name = $"[Video] {file.Name}",
                                Type = FollowCircleType.Video,
                                FilePath = file.FullName,
                            });
                        }
                        else if (isImageExtension(file.Extension))
                        {
                            list.Add(new FollowCircleItem
                            {
                                Id = $"user_img_{file.Name}",
                                Name = $"[Image] {file.Name}",
                                Type = FollowCircleType.SingleImage,
                                FilePath = file.FullName,
                            });
                        }
                    }

                    // Subdirectories in followcircles folder (animation frame sequences)
                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        var images = subDir.GetFiles("*.*", SearchOption.TopDirectoryOnly)
                                           .Where(f => isImageExtension(f.Extension))
                                           .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                                           .Select(f => f.FullName)
                                           .ToList();

                        if (images.Count > 0)
                        {
                            list.Add(new FollowCircleItem
                            {
                                Id = $"user_dir_{subDir.Name}",
                                Name = $"[Folder] {subDir.Name} ({images.Count} frames)",
                                Type = images.Count > 1 ? FollowCircleType.AnimationFrames : FollowCircleType.SingleImage,
                                FrameCount = images.Count,
                                FramePaths = images,
                                FilePath = images[0],
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error scanning followcircles user storage: {ex.Message}", level: LogLevel.Error);
            }

            // 3. Scan active skin's files on local disk if present (e.g. wine skins directory or export)
            try
            {
                string skinName = skins.CurrentSkinInfo.Value?.Value?.Name ?? string.Empty;
                if (!string.IsNullOrEmpty(skinName))
                {
                    string wineSkinsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/osu-wine/osu!/Skins");

                    if (Directory.Exists(wineSkinsDir))
                    {
                        string skinPath = Path.Combine(wineSkinsDir, skinName);
                        if (Directory.Exists(skinPath))
                        {
                            var sDir = new DirectoryInfo(skinPath);

                            // Videos in root of skin
                            foreach (var file in sDir.GetFiles("*.*", SearchOption.TopDirectoryOnly))
                            {
                                if (isVideoExtension(file.Extension))
                                {
                                    list.Add(new FollowCircleItem
                                    {
                                        Id = $"skin_video_{file.Name}",
                                        Name = $"[Skin Video] {file.Name}",
                                        Type = FollowCircleType.Video,
                                        FilePath = file.FullName,
                                    });
                                }
                            }

                            // Subdirectories like "alt circles" or "followcircles"
                            string[] subFolderCandidates = { "alt circles", "followcircles", "follow circle", "circles" };

                            foreach (var candidate in subFolderCandidates)
                            {
                                string candidatePath = Path.Combine(skinPath, candidate);
                                if (Directory.Exists(candidatePath))
                                {
                                    var parentSubDir = new DirectoryInfo(candidatePath);

                                    foreach (var altDir in parentSubDir.GetDirectories())
                                    {
                                        var images = altDir.GetFiles("*.*", SearchOption.TopDirectoryOnly)
                                                           .Where(f => isImageExtension(f.Extension))
                                                           .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                                                           .Select(f => f.FullName)
                                                           .ToList();

                                        if (images.Count > 0)
                                        {
                                            list.Add(new FollowCircleItem
                                            {
                                                Id = $"skin_alt_{altDir.Name}",
                                                Name = $"[Skin Alt] {altDir.Name} ({images.Count} frames)",
                                                Type = images.Count > 1 ? FollowCircleType.AnimationFrames : FollowCircleType.SingleImage,
                                                FrameCount = images.Count,
                                                FramePaths = images,
                                                FilePath = images[0],
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error scanning active skin folder on disk: {ex.Message}", level: LogLevel.Error);
            }

            // 4. Scan active skin's indexed Realm files for video files
            try
            {
                var activeSkin = skins.CurrentSkin.Value;
                if (activeSkin != null)
                {
                    var videoFiles = activeSkin.SkinInfo.PerformRead(s =>
                        s.Files.Where(f => isVideoExtension(Path.GetExtension(f.Filename)))
                               .Select(f => new { f.Filename, StoragePath = f.File.GetStoragePath() })
                               .ToList());

                    if (videoFiles != null)
                    {
                        foreach (var vFile in videoFiles)
                        {
                            string id = $"realm_video_{vFile.Filename}";
                            if (!list.Any(i => i.Id == id))
                            {
                                list.Add(new FollowCircleItem
                                {
                                    Id = id,
                                    Name = $"[Skin Video] {Path.GetFileName(vFile.Filename)}",
                                    Type = FollowCircleType.Video,
                                    SkinFilename = vFile.Filename,
                                    StoragePath = vFile.StoragePath,
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error scanning active skin Realm files: {ex.Message}", level: LogLevel.Error);
            }

            // 5. Add custom follow circles from other installed skins
            try
            {
                foreach (var s in skins.GetAllUsableSkins())
                {
                    if (s.ID == skins.CurrentSkinInfo.Value?.ID || s.ID == SkinInfo.RANDOM_SKIN)
                        continue;

                    list.Add(new FollowCircleItem
                    {
                        Id = $"skin_ref_{s.ID}",
                        Name = $"[Skin] {s.Value?.Name ?? "Unknown"}",
                        Type = FollowCircleType.SkinReference,
                        SkinId = s.ID,
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error adding usable skins as follow circles: {ex.Message}", level: LogLevel.Error);
            }

            AvailableFollowCircles.AddRange(list);

            if (string.IsNullOrEmpty(CustomFollowCircleId.Value) || !AvailableFollowCircles.Any(i => i.Id == CustomFollowCircleId.Value))
            {
                CustomFollowCircleId.Value = AvailableFollowCircles.FirstOrDefault()?.Id ?? string.Empty;
            }
        }

        public FollowCircleItem? GetNextFollowCircleItem()
        {
            var pool = AvailableFollowCircles.Where(i => i.Type != FollowCircleType.DefaultSkin).ToList();
            if (pool.Count == 0)
                pool = AvailableFollowCircles.ToList();

            if (pool.Count == 0)
                return null;

            switch (Mode.Value)
            {
                case FollowCircleMode.Cycle:
                    int next = Interlocked.Increment(ref cycleCounter);
                    return pool[Math.Abs(next) % pool.Count];

                case FollowCircleMode.Random:
                    return pool[RNG.Next(pool.Count)];

                case FollowCircleMode.Selected:
                    var selected = AvailableFollowCircles.FirstOrDefault(i => i.Id == CustomFollowCircleId.Value);
                    return selected ?? pool[0];

                default:
                case FollowCircleMode.Default:
                    return AvailableFollowCircles.FirstOrDefault(i => i.Type == FollowCircleType.DefaultSkin);
            }
        }

        public Func<Stream?>? GetStreamProvider(FollowCircleItem item)
        {
            if (item.Type == FollowCircleType.Video)
            {
                if (!string.IsNullOrEmpty(item.FilePath))
                    return () => File.Exists(item.FilePath) ? File.OpenRead(item.FilePath) : null;

                if (!string.IsNullOrEmpty(item.StoragePath))
                {
                    var provider = (IStorageResourceProvider)skins;
                    return () => provider.Files.GetStream(item.StoragePath);
                }
            }

            return null;
        }

        public IReadOnlyList<Texture> GetTextures(FollowCircleItem item)
        {
            if (frameCache.TryGetValue(item.Id, out var cached))
                return cached;

            var textures = new List<Texture>();

            if (item.Type == FollowCircleType.AnimationFrames && item.FramePaths != null)
            {
                foreach (string p in item.FramePaths)
                {
                    var tex = textureStore.Get(p);
                    if (tex != null)
                        textures.Add(tex);
                }
            }
            else if (item.Type == FollowCircleType.SingleImage && !string.IsNullOrEmpty(item.FilePath))
            {
                var tex = textureStore.Get(item.FilePath);
                if (tex != null)
                    textures.Add(tex);
            }

            var arr = textures.ToArray();
            frameCache[item.Id] = arr;
            return arr;
        }

        private static bool isVideoExtension(string ext)
        {
            string clean = ext.TrimStart('.').ToLowerInvariant();
            return clean == "mp4" || clean == "webm" || clean == "avi" || clean == "mov" || clean == "mkv";
        }

        private static bool isImageExtension(string ext)
        {
            string clean = ext.TrimStart('.').ToLowerInvariant();
            return clean == "png" || clean == "jpg" || clean == "jpeg";
        }

        private class DiskFileResourceStore : IResourceStore<byte[]>
        {
            public byte[] Get(string name)
            {
                try
                {
                    return File.Exists(name) ? File.ReadAllBytes(name) : null!;
                }
                catch
                {
                    return null!;
                }
            }

            public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Get(name));
            }

            public Stream? GetStream(string name)
            {
                try
                {
                    return File.Exists(name) ? File.OpenRead(name) : null;
                }
                catch
                {
                    return null;
                }
            }

            public IEnumerable<string> GetAvailableResources() => Enumerable.Empty<string>();

            public void Dispose()
            {
            }
        }
    }
}
