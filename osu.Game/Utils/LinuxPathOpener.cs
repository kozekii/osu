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
    /// Handles broken/isolated PATH environments and enforces proper desktop styling (QT_QPA_PLATFORMTHEME=kde).
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

            // Ensure Qt applications like Dolphin load the user's KDE theme from kdeglobals rather than default white
            psi.Environment["QT_QPA_PLATFORMTHEME"] = "kde";

            string preamble = $"export PATH=\"{standardPath}:$PATH\"\nexport QT_QPA_PLATFORMTHEME=\"kde\"\n";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(preamble + script);

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
                // Note: Dolphin requires QT_QPA_PLATFORMTHEME=kde to match the user's dark theme from kdeglobals instead of blinding white!
                // Each candidate is launched sequentially (first one present wins) so we never spawn multiple managers at once.
                string script = $@"
target='{escapedPath}'
DM=""$(xdg-mime query default inode/directory 2>/dev/null)""
case ""$DM"" in
    *dolphin*)  PREFERRED=dolphin ;;
    *nautilus*) PREFERRED=nautilus ;;
    *thunar*)   PREFERRED=thunar ;;
    *nemo*)     PREFERRED=nemo ;;
    *pcmanfm*)  PREFERRED=pcmanfm ;;
    *)          PREFERRED= ;;
esac
for c in ""$PREFERRED"" dolphin nautilus thunar nemo pcmanfm; do
    [ -n ""$c"" ] || continue
    if command -v ""$c"" >/dev/null 2>&1; then
        QT_QPA_PLATFORMTHEME=kde ""$c"" ""$target"" >/dev/null 2>&1 &
        exit 0
    fi
done
command -v gio >/dev/null 2>&1 && {{ gio open ""$target"" >/dev/null 2>&1 & exit 0; }}
xdg-open ""$target"" >/dev/null 2>&1 &
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

                // Launch a single manager sequentially (first present wins). Managers that support
                // highlighting a file use --select; the rest fall back to opening the parent folder.
                string script = $@"
target='{escapedPath}'
parent='{escapedParent}'
DM=""$(xdg-mime query default inode/directory 2>/dev/null)""
case ""$DM"" in
    *dolphin*)  PREFERRED=dolphin ;;
    *nautilus*) PREFERRED=nautilus ;;
    *nemo*)     PREFERRED=nemo ;;
    *thunar*)   PREFERRED=thunar ;;
    *pcmanfm*)  PREFERRED=pcmanfm ;;
    *)          PREFERRED= ;;
esac
launch() {{
    case ""$1"" in
        dolphin)  QT_QPA_PLATFORMTHEME=kde dolphin --select ""$target"" >/dev/null 2>&1 & ;;
        nautilus) nautilus --select ""$target"" >/dev/null 2>&1 & ;;
        nemo)     nemo --no-desktop ""$target"" >/dev/null 2>&1 & ;;
        thunar)   thunar ""$parent"" >/dev/null 2>&1 & ;;
        pcmanfm)  pcmanfm ""$parent"" >/dev/null 2>&1 & ;;
    esac
}}
for c in ""$PREFERRED"" dolphin nautilus nemo thunar pcmanfm; do
    [ -n ""$c"" ] || continue
    if command -v ""$c"" >/dev/null 2>&1; then launch ""$c""; exit 0; fi
done
command -v gio >/dev/null 2>&1 && {{ gio open ""$parent"" >/dev/null 2>&1 & exit 0; }}
xdg-open ""$parent"" >/dev/null 2>&1 &
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
command -v gio >/dev/null 2>&1 && {{ gio open ""$target"" >/dev/null 2>&1 & exit 0; }}
xdg-open ""$target"" >/dev/null 2>&1 &
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
