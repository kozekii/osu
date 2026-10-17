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
    /// Helper for reliably opening directories and files in native file managers on Linux (Wayland, KDE Plasma 6, GNOME, etc.).
    /// Standard framework xdg-open invocations often fail or hang under modern desktop sessions.
    /// </summary>
    public static class LinuxPathOpener
    {
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
                string uri = new Uri(fullPath).AbsoluteUri;
                string escapedPath = fullPath.Replace("'", "'\\''");

                // Priority pipeline:
                // 1. Freedesktop FileManager1 DBus (native on KDE Dolphin, GNOME Nautilus, Nemo, Thunar)
                // 2. gio open (GLib GIO desktop integration)
                // 3. Direct dolphin execution (KDE default)
                // 4. Direct nautilus execution (GNOME default)
                // 5. Direct thunar execution (XFCE default)
                // 6. xdg-open fallback
                string command = $"dbus-send --session --dest=org.freedesktop.FileManager1 --type=method_call --reply-timeout=2000 /org/freedesktop/FileManager1 org.freedesktop.FileManager1.ShowFolders array:string:\\\"{uri}\\\" string:\\\"\\\" >/dev/null 2>&1 "
                               + $"|| gio open '{escapedPath}' >/dev/null 2>&1 "
                               + $"|| dolphin '{escapedPath}' >/dev/null 2>&1 "
                               + $"|| nautilus '{escapedPath}' >/dev/null 2>&1 "
                               + $"|| thunar '{escapedPath}' >/dev/null 2>&1 "
                               + $"|| xdg-open '{escapedPath}' >/dev/null 2>&1 &";

                var psi = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(command);

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

                string uri = new Uri(fullPath).AbsoluteUri;
                string escapedPath = fullPath.Replace("'", "'\\''");
                string parentDir = Path.GetDirectoryName(fullPath) ?? fullPath;
                string escapedParent = parentDir.Replace("'", "'\\''");

                // Priority pipeline for presenting/selecting items:
                // 1. Freedesktop FileManager1 ShowItems (highlights item in Dolphin/Nautilus)
                // 2. dolphin --select
                // 3. nautilus --select
                // 4. gio open parent folder
                // 5. thunar parent folder
                // 6. xdg-open parent folder
                string command = $"dbus-send --session --dest=org.freedesktop.FileManager1 --type=method_call --reply-timeout=2000 /org/freedesktop/FileManager1 org.freedesktop.FileManager1.ShowItems array:string:\\\"{uri}\\\" string:\\\"\\\" >/dev/null 2>&1 "
                               + $"|| dolphin --select '{escapedPath}' >/dev/null 2>&1 "
                               + $"|| nautilus --select '{escapedPath}' >/dev/null 2>&1 "
                               + $"|| gio open '{escapedParent}' >/dev/null 2>&1 "
                               + $"|| thunar '{escapedParent}' >/dev/null 2>&1 "
                               + $"|| xdg-open '{escapedParent}' >/dev/null 2>&1 &";

                var psi = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(command);

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
        /// Opens a file or folder externally with its default application or file manager.
        /// </summary>
        /// <param name="path">The file or folder path.</param>
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

                // Priority pipeline for opening a file:
                // 1. gio open (standard GIO launcher for default desktop app)
                // 2. xdg-open
                string command = $"gio open '{escapedPath}' >/dev/null 2>&1 || xdg-open '{escapedPath}' >/dev/null 2>&1 &";

                var psi = new ProcessStartInfo("/bin/sh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(command);

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
