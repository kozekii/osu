// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;
using osu.Framework;
using osu.Framework.Logging;

namespace osu.Game.Utils
{
    /// <summary>
    /// Helper for reliably opening directories and files in native file managers on Linux (Wayland, Hyprland, KDE Plasma, GNOME, etc.).
    /// Handles broken/isolated PATH environments and avoids headless DBus daemon traps.
    /// </summary>
    public static class LinuxPathOpener
    {
        private static ProcessStartInfo createShellProcessInfo(string script)
        {
            var psi = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Ensure standard system paths are in PATH even if the parent environment is stripped down
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string standardPath = "/usr/local/bin:/usr/bin:/bin:/usr/local/sbin:/usr/sbin";
            psi.Environment["PATH"] = $"{standardPath}:{currentPath}";

            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"export PATH=\"{standardPath}:$PATH\"\n{script}");

            return psi;
        }

        /// <summary>
        /// Opens a folder directly in the user's native file manager.
        /// </summary>
        /// <param name="path">The directory path to open.</param>
        /// <returns>True if the process was successfully launched.</returns>
        public static bool OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (RuntimeInfo.OS != RuntimeInfo.Platform.Linux)
                return false;

            try
            {
                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    try
                    {
                        Directory.CreateDirectory(path);
                    }
                    catch
                    {
                        // Ignore directory creation failure
                    }
                }

                string fullPath = Path.GetFullPath(path);
                string escapedPath = fullPath.Replace("'", "'\\''");

                // Check default file manager via xdg-mime first, then fall back through known managers and standard launchers.
                // Notice: We avoid raw FileManager1 DBus call because headless background daemons (like nautilus --gapplication-service)
                // can silently swallow the DBus call under other window managers (like Hyprland) without creating any window!
                string script = $@"
target='{escapedPath}'
case ""$(xdg-mime query default inode/directory 2>/dev/null)"" in
    *dolphin*) dolphin ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *nautilus*) nautilus ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *thunar*) thunar ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *nemo*) nemo ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *pcmanfm*) pcmanfm ""$target"" >/dev/null 2>&1 & exit 0 ;;
esac
dolphin ""$target"" >/dev/null 2>&1 & exit 0 || nautilus ""$target"" >/dev/null 2>&1 & exit 0 || thunar ""$target"" >/dev/null 2>&1 & exit 0 || nemo ""$target"" >/dev/null 2>&1 & exit 0 || pcmanfm ""$target"" >/dev/null 2>&1 & exit 0 || gio open ""$target"" >/dev/null 2>&1 & exit 0 || xdg-open ""$target"" >/dev/null 2>&1 &
";

                var psi = createShellProcessInfo(script);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"LinuxPathOpener failed to open folder '{path}': {ex}", LoggingTarget.Runtime, LogLevel.Debug);
                return false;
            }
        }

        /// <summary>
        /// Highlights/selects a file inside its parent directory in the native file manager (or opens the directory if it's a folder).
        /// </summary>
        /// <param name="path">The file or folder path to present.</param>
        /// <returns>True if the process was successfully launched.</returns>
        public static bool Present(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (RuntimeInfo.OS != RuntimeInfo.Platform.Linux)
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path);

                if (Directory.Exists(fullPath) || !File.Exists(fullPath))
                    return OpenFolder(fullPath);

                string escapedPath = fullPath.Replace("'", "'\\''");
                string parentDir = Path.GetDirectoryName(fullPath) ?? fullPath;
                string escapedParent = parentDir.Replace("'", "'\\''");

                string script = $@"
target='{escapedPath}'
parent='{escapedParent}'
case ""$(xdg-mime query default inode/directory 2>/dev/null)"" in
    *dolphin*) dolphin --select ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *nautilus*) nautilus --select ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *nemo*) nemo --no-desktop ""$target"" >/dev/null 2>&1 & exit 0 ;;
    *thunar*) thunar ""$parent"" >/dev/null 2>&1 & exit 0 ;;
    *pcmanfm*) pcmanfm ""$parent"" >/dev/null 2>&1 & exit 0 ;;
esac
dolphin --select ""$target"" >/dev/null 2>&1 & exit 0 || nautilus --select ""$target"" >/dev/null 2>&1 & exit 0 || nemo --no-desktop ""$target"" >/dev/null 2>&1 & exit 0 || thunar ""$parent"" >/dev/null 2>&1 & exit 0 || pcmanfm ""$parent"" >/dev/null 2>&1 & exit 0 || gio open ""$parent"" >/dev/null 2>&1 & exit 0 || xdg-open ""$parent"" >/dev/null 2>&1 &
";

                var psi = createShellProcessInfo(script);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"LinuxPathOpener failed to present '{path}': {ex}", LoggingTarget.Runtime, LogLevel.Debug);
                return false;
            }
        }

        /// <summary>
        /// Opens a file externally with its default application.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>True if the process was successfully launched.</returns>
        public static bool Open(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (RuntimeInfo.OS != RuntimeInfo.Platform.Linux)
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path);

                if (Directory.Exists(fullPath))
                    return OpenFolder(fullPath);

                string escapedPath = fullPath.Replace("'", "'\\''");

                string script = $@"
target='{escapedPath}'
gio open ""$target"" >/dev/null 2>&1 & exit 0 || xdg-open ""$target"" >/dev/null 2>&1 &
";

                var psi = createShellProcessInfo(script);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"LinuxPathOpener failed to open '{path}': {ex}", LoggingTarget.Runtime, LogLevel.Debug);
                return false;
            }
        }
    }
}
