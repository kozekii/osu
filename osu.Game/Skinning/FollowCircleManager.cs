// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO;
using osu.Game.Utils;

namespace osu.Game.Skinning
{
    public class FollowCircleManager
    {
        private const string follow_circles_folder = "followcircles";

        /// <summary>
        /// Upper bound on a single video file that will be held in memory for decoder reuse.
        /// </summary>
        private const long max_cached_video_bytes = 32 * 1024 * 1024;

        /// <summary>
        /// Upper bound on the combined size of all cached follow circle videos.
        /// </summary>
        private const long max_total_cached_video_bytes = 128 * 1024 * 1024;

        public BindableList<FollowCircleItem> AvailableFollowCircles { get; } = new BindableList<FollowCircleItem>();

        public Bindable<FollowCircleMode> Mode { get; } = new Bindable<FollowCircleMode>();

        public Bindable<string> CustomFollowCircleId { get; } = new Bindable<string>();

        public Bindable<bool> CircularMask { get; } = new Bindable<bool>();

        public Bindable<bool> AboveCursor { get; } = new Bindable<bool>();

        private readonly Storage storage;
        private readonly SkinManager skins;
        private readonly OsuConfigManager config;
        private readonly TextureStore textureStore;
        private readonly Scheduler? scheduler;

        private readonly Dictionary<string, Texture[]> frameCache = new Dictionary<string, Texture[]>();
        private readonly Dictionary<string, byte[]> videoDataCache = new Dictionary<string, byte[]>();
        private readonly Dictionary<Guid, ISkin> skinInstanceCache = new Dictionary<Guid, ISkin>();

        private Dictionary<Guid, Live<SkinInfo>> skinInfoIndex = new Dictionary<Guid, Live<SkinInfo>>();

        private long cachedVideoBytes;

        private int cycleCounter = -1;

        private int refreshVersion;
        private bool refreshInFlight;
        private bool refreshQueued;

        public FollowCircleManager(Storage storage, SkinManager skins, OsuConfigManager config, Scheduler? scheduler = null)
        {
            this.storage = storage;
            this.skins = skins;
            this.config = config;
            this.scheduler = scheduler;

            var resourceProvider = (IStorageResourceProvider)skins;
            textureStore = new TextureStore(resourceProvider.Renderer, resourceProvider.CreateTextureLoaderStore(new DiskFileResourceStore()));

            config.BindWith(OsuSetting.FollowCircleMode, Mode);
            config.BindWith(OsuSetting.CustomFollowCircle, CustomFollowCircleId);
            config.BindWith(OsuSetting.FollowCircleCircularMask, CircularMask);
            config.BindWith(OsuSetting.FollowCircleAboveCursor, AboveCursor);

            skins.CurrentSkin.BindValueChanged(_ => RefreshAvailableFollowCircles(), true);
        }

        public void OpenFolder()
        {
            try
            {
                string path = storage.GetStorageForDirectory(follow_circles_folder).GetFullPath(string.Empty);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                if (RuntimeInfo.OS == RuntimeInfo.Platform.Linux)
                {
                    LinuxPathOpener.OpenFolder(path);
                    return;
                }

                storage.GetStorageForDirectory(follow_circles_folder).PresentExternally();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to open follow circles folder: {ex.Message}", level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Rebuilds the list of selectable follow circles. The scan walks the filesystem and the skin
        /// database, so it runs on a background thread and is coalesced: calling this repeatedly while a
        /// scan is in flight results in exactly one extra scan afterwards.
        /// </summary>
        public void RefreshAvailableFollowCircles()
        {
            if (scheduler == null)
            {
                applyFollowCircles(scanFollowCircles());
                return;
            }

            int version = Interlocked.Increment(ref refreshVersion);

            if (refreshInFlight)
            {
                refreshQueued = true;
                return;
            }

            refreshInFlight = true;

            Task.Run(() =>
            {
                FollowCircleScanResult result;

                try
                {
                    result = scanFollowCircles();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to scan follow circles: {ex.Message}", level: LogLevel.Error);
                    result = new FollowCircleScanResult(new List<FollowCircleItem>(), new Dictionary<Guid, Live<SkinInfo>>());
                }

                try
                {
                    scheduler.Add(() =>
                    {
                        refreshInFlight = false;

                        // A newer refresh may have been requested while this scan was running.
                        if (version == refreshVersion)
                            applyFollowCircles(result);
                        else
                            refreshQueued = true;

                        if (refreshQueued)
                        {
                            refreshQueued = false;
                            RefreshAvailableFollowCircles();
                        }
                    });
                }
                catch (ObjectDisposedException)
                {
                    // Game shutting down mid-scan; nothing to apply.
                }
            });
        }

        private FollowCircleScanResult scanFollowCircles()
        {
            var list = new List<FollowCircleItem>();
            var skinIndex = new Dictionary<Guid, Live<SkinInfo>>();

            // 1. Default (Active Skin)
            string activeSkinName = skins.CurrentSkinInfo.Value?.PerformRead(s => s.Name) ?? "Default";
            list.Add(new FollowCircleItem
            {
                Id = "default_skin",
                Name = $"Active Skin ({activeSkinName})",
                Type = FollowCircleType.DefaultSkin,
            });

            scanUserFolder(list);
            scanSkinFoldersOnDisk(list, activeSkinName);
            scanActiveSkinFiles(list);
            scanInstalledSkins(list, skinIndex);

            return new FollowCircleScanResult(list, skinIndex);
        }

        private void applyFollowCircles(FollowCircleScanResult result)
        {
            var items = result.Items;

            // Retain cached frames for items which survived the refresh; only drop entries which are gone.
            var liveIds = new HashSet<string>(items.Select(i => i.Id));
            foreach (string staleId in frameCache.Keys.Where(id => !liveIds.Contains(id)).ToList())
                frameCache.Remove(staleId);

            skinInfoIndex = result.SkinInfoIndex;

            AvailableFollowCircles.Clear();
            AvailableFollowCircles.AddRange(items);

            if (string.IsNullOrEmpty(CustomFollowCircleId.Value) || !items.Any(i => i.Id == CustomFollowCircleId.Value))
                CustomFollowCircleId.Value = items.FirstOrDefault()?.Id ?? string.Empty;
        }

        /// <summary>
        /// Scans the user-managed <c>followcircles</c> storage folder for images, videos and frame-sequence subfolders.
        /// </summary>
        private void scanUserFolder(List<FollowCircleItem> list)
        {
            try
            {
                var fcStorage = storage.GetStorageForDirectory(follow_circles_folder);
                string basePath = fcStorage.GetFullPath(string.Empty);

                if (!Directory.Exists(basePath))
                    return;

                var dirInfo = new DirectoryInfo(basePath);

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
            catch (Exception ex)
            {
                Logger.Log($"Error scanning followcircles user storage: {ex.Message}", level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Scans the active skin's folder on disk (for installs run under wine).
        /// </summary>
        private void scanSkinFoldersOnDisk(List<FollowCircleItem> list, string skinName)
        {
            if (string.IsNullOrEmpty(skinName))
                return;

            foreach (string root in getWineSkinRoots())
            {
                try
                {
                    string skinPath = Path.Combine(root, skinName);
                    if (!Directory.Exists(skinPath))
                        continue;

                    var sDir = new DirectoryInfo(skinPath);

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

                    string[] subFolderCandidates = { "alt circles", "followcircles", "follow circle", "circles" };

                    foreach (string candidate in subFolderCandidates)
                    {
                        string candidatePath = Path.Combine(skinPath, candidate);
                        if (!Directory.Exists(candidatePath))
                            continue;

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
                catch (Exception ex)
                {
                    Logger.Log($"Error scanning skin folder on disk: {ex.Message}", level: LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Pulls video assets out of the active skin's indexed files.
        /// </summary>
        private void scanActiveSkinFiles(List<FollowCircleItem> list)
        {
            try
            {
                var activeSkin = skins.CurrentSkin.Value;

                if (activeSkin?.SkinInfo == null)
                    return;

                var videoFiles = activeSkin.SkinInfo.PerformRead(s =>
                    s.Files.Where(f => isVideoExtension(Path.GetExtension(f.Filename)))
                           .Select(f => new { f.Filename, StoragePath = f.File.GetStoragePath() })
                           .ToList());

                if (videoFiles == null)
                    return;

                foreach (var vFile in videoFiles)
                {
                    string id = $"realm_video_{vFile.Filename}";
                    if (list.Any(i => i.Id == id))
                        continue;

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
            catch (Exception ex)
            {
                Logger.Log($"Error scanning active skin Realm files: {ex.Message}", level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Lists every other installed skin as a potential follow circle source.
        /// </summary>
        private void scanInstalledSkins(List<FollowCircleItem> list, Dictionary<Guid, Live<SkinInfo>> skinIndex)
        {
            try
            {
                Guid? activeId = skins.CurrentSkinInfo.Value?.ID;

                foreach (var s in skins.GetAllUsableSkins())
                {
                    if (s.ID == activeId || s.ID == SkinInfo.RANDOM_SKIN)
                        continue;

                    string sName = s.PerformRead(skin => skin.Name);

                    list.Add(new FollowCircleItem
                    {
                        Id = $"skin_ref_{s.ID}",
                        Name = $"[Skin] {sName}",
                        Type = FollowCircleType.SkinReference,
                        SkinId = s.ID,
                    });

                    skinIndex[s.ID] = s;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error adding usable skins as follow circles: {ex.Message}", level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Candidate skin directories for pre-Lazer installs running under wine. Only scanned on Linux,
        /// and only directories which actually exist are returned.
        /// </summary>
        private static IEnumerable<string> getWineSkinRoots()
        {
            var roots = new List<string>();

            if (RuntimeInfo.OS != RuntimeInfo.Platform.Linux)
                return roots;

            try
            {
                string? winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");

                if (!string.IsNullOrEmpty(winePrefix))
                {
                    string usersDir = Path.Combine(winePrefix, "drive_c", "users");

                    if (Directory.Exists(usersDir))
                    {
                        foreach (string userDir in Directory.GetDirectories(usersDir))
                        {
                            string candidate = Path.Combine(userDir, "AppData", "Roaming", "osu!", "Skins");
                            if (Directory.Exists(candidate))
                                roots.Add(candidate);
                        }
                    }
                }

                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/osu-wine/osu!/Skins");

                if (Directory.Exists(legacy))
                    roots.Add(legacy);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error discovering wine skin directories: {ex.Message}", level: LogLevel.Error);
            }

            return roots;
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

        /// <summary>
        /// Resolves (and caches) the skin instance backing a <see cref="FollowCircleType.SkinReference"/> item.
        /// Lives on the game thread; the expensive part of the lookup is done during the background scan.
        /// </summary>
        public ISkin? GetSkinReference(FollowCircleItem item)
        {
            if (!item.SkinId.HasValue)
                return null;

            if (skinInstanceCache.TryGetValue(item.SkinId.Value, out var cached))
                return cached;

            if (!skinInfoIndex.TryGetValue(item.SkinId.Value, out var live))
                return null;

            try
            {
                var skin = live.PerformRead(skins.GetSkin);

                if (skin == null)
                    return null;

                skinInstanceCache[item.SkinId.Value] = skin;
                return skin;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to resolve skin for follow circle: {ex.Message}", level: LogLevel.Error);
                return null;
            }
        }

        public Func<Stream?>? GetStreamProvider(FollowCircleItem item)
        {
            if (item.Type != FollowCircleType.Video)
                return null;

            if (!string.IsNullOrEmpty(item.FilePath))
            {
                string path = item.FilePath;
                return () => openVideoStream(item.Id,
                    () => File.Exists(path) ? File.ReadAllBytes(path) : null,
                    () => File.Exists(path) ? File.OpenRead(path) : null);
            }

            if (!string.IsNullOrEmpty(item.StoragePath))
            {
                var provider = (IStorageResourceProvider)skins;
                string storagePath = item.StoragePath;

                return () => openVideoStream(item.Id,
                    () => readAllBytes(provider.Files.GetStream(storagePath)),
                    () => provider.Files.GetStream(storagePath));
            }

            return null;
        }

        /// <summary>
        /// Hands out a fresh stream over cached video bytes so every decoder can re-read the file without
        /// touching the disk again.
        /// </summary>
        private Stream? openVideoStream(string id, Func<byte[]?> byteLoader, Func<Stream?> streamProvider)
        {
            if (videoDataCache.TryGetValue(id, out var cached))
                return new MemoryStream(cached, writable: false);

            byte[]? data = byteLoader();

            if (data == null)
                return streamProvider();

            if (data.Length <= max_cached_video_bytes && cachedVideoBytes + data.Length <= max_total_cached_video_bytes)
            {
                videoDataCache[id] = data;
                cachedVideoBytes += data.Length;
            }

            return new MemoryStream(data, writable: false);
        }

        private static byte[]? readAllBytes(Stream? stream)
        {
            if (stream == null)
                return null;

            try
            {
                using (stream)
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch
            {
                return null;
            }
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

            // Only cache a fully successful load; a partially loaded sequence should be retried.
            if (item.FramePaths == null || arr.Length == item.FramePaths.Count)
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

        private readonly struct FollowCircleScanResult
        {
            public readonly List<FollowCircleItem> Items;
            public readonly Dictionary<Guid, Live<SkinInfo>> SkinInfoIndex;

            public FollowCircleScanResult(List<FollowCircleItem> items, Dictionary<Guid, Live<SkinInfo>> skinInfoIndex)
            {
                Items = items;
                SkinInfoIndex = skinInfoIndex;
            }
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
