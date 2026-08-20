using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// A named server target — one entry in <c>dev-targets.json</c>.
    /// <para>
    /// ⚠️ An <b>empty <see cref="ip"/> means NO address</b>: <see cref="DevSession.HasAddress"/> is
    /// false and the client falls back to the production discovery chain (PlayerPrefs &gt; beacon
    /// &gt; StreamingAssets/arena.json). "Kesif (beacon)" is deliberately such an entry.
    /// </para>
    /// </summary>
    [Serializable]
    public class DevTarget
    {
        /// <summary>Target name — popup label and the key written to <see cref="DevSession.TargetName"/>.</summary>
        public string name;

        /// <summary>Server IP. <b>Empty hands over to the discovery chain</b> (no address written).</summary>
        public string ip;

        /// <summary>WS control port; 0/missing is corrected to <see cref="ArenaProtocol.CONTROL_PORT"/>.</summary>
        public int port;

        /// <summary>Concrete address, or discovery mode?</summary>
        public bool HasAddress => !string.IsNullOrWhiteSpace(ip) && port > 0;

        /// <summary>Popup label: <c>Local (127.0.0.1:47821)</c> / <c>Kesif (beacon) (keşif)</c>.</summary>
        public string Label => HasAddress ? $"{name} ({ip}:{port})" : $"{name} (keşif)";
    }

    /// <summary>JSON root schema — flat fields for <see cref="JsonUtility"/>.</summary>
    [Serializable]
    internal class DevTargetsFile
    {
        public string defaultTarget;
        public string defaultRole;
        public DevTarget[] targets;
    }

    /// <summary>
    /// Reader for <c>dev-targets.json</c> in the repo root — source of the dev window's target list.
    ///
    /// <para><b>Why two layers:</b> the named target list (this file) is COMMITTED so the team
    /// shares it, but WHICH target is selected is personal and lives in <c>EditorPrefs</c>
    /// (<see cref="DevSession"/>). Committing the selection would be the classic "checked-in user
    /// settings" trap: everyone overwrites everyone's IP and <c>git status</c> stays dirty. With
    /// this split, <b>changing role/target dirties no file</b>.</para>
    ///
    /// <para>⚠️ <b>Empty <c>ip</c> = discovery chain</b> (noted here because JSON has no comments):
    /// <c>"Kesif (beacon)"</c> is deliberately address-less, so the client uses
    /// PlayerPrefs &gt; beacon &gt; StreamingAssets/arena.json.</para>
    ///
    /// <para>Missing/corrupt file: in-memory <c>Local</c> + <c>Kesif (beacon)</c> defaults plus one
    /// warning. ⚠️ NEVER written to the repo — a dev tool must not silently create the team's file
    /// and dirty commits.</para>
    /// </summary>
    public static class DevTargets
    {
        /// <summary>Catalog file name (in the repo root).</summary>
        public const string FileName = "dev-targets.json";

        private static List<DevTarget> targets;
        private static string defaultTargetName = "";
        private static string defaultRole = AppSession.RoleAdmin;
        private static bool fileFound;

        /// <summary>Catalog file full path (repo root + <see cref="FileName"/>).</summary>
        public static string FilePath => Path.Combine(RepoRoot, FileName);

        /// <summary>Was the file found and readable on the last load?</summary>
        public static bool FileFound
        {
            get
            {
                EnsureLoaded();
                return fileFound;
            }
        }

        /// <summary>Named targets (never empty; built-in defaults when there is no file).</summary>
        public static IReadOnlyList<DevTarget> Targets
        {
            get
            {
                EnsureLoaded();
                return targets;
            }
        }

        /// <summary>JSON <c>defaultTarget</c>, or the first target's name when it is not listed.</summary>
        public static string DefaultTargetName
        {
            get
            {
                EnsureLoaded();
                return defaultTargetName;
            }
        }

        /// <summary>JSON <c>defaultRole</c> — "player" | "admin" ("admin" when invalid).</summary>
        public static string DefaultRole
        {
            get
            {
                EnsureLoaded();
                return defaultRole;
            }
        }

        /// <summary>Finds a target by name (Ordinal, exact); false + null when not found.</summary>
        public static bool TryFind(string name, out DevTarget target)
        {
            target = null;
            EnsureLoaded();

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (string.Equals(targets[i].name, name, StringComparison.Ordinal))
                {
                    target = targets[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Drops the cache and re-reads the file (the window's "Tazele").</summary>
        public static void Reload()
        {
            targets = null;
            EnsureLoaded();
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Repo root = the parent of <c>Assets</c> (Application.dataPath's parent).</summary>
        private static string RepoRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
        }

        private static void EnsureLoaded()
        {
            if (targets != null)
            {
                return;
            }

            targets = new List<DevTarget>();
            fileFound = false;

            string path = FilePath;
            string json = null;
            string failure = null;

            try
            {
                if (File.Exists(path))
                {
                    json = File.ReadAllText(path);
                    fileFound = true;
                }
                else
                {
                    failure = "dosya yok";
                }
            }
            catch (Exception ex)
            {
                failure = "okunamadı: " + ex.Message;
            }

            DevTargetsFile parsed = null;
            if (fileFound)
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    failure = "dosya boş";
                }
                else
                {
                    try
                    {
                        parsed = JsonUtility.FromJson<DevTargetsFile>(json);
                        if (parsed == null)
                        {
                            failure = "JSON ayrıştırılamadı (kök nesne yok)";
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = "JSON bozuk: " + ex.Message;
                    }
                }
            }

            if (parsed != null && parsed.targets != null)
            {
                for (int i = 0; i < parsed.targets.Length; i++)
                {
                    DevTarget entry = Normalize(parsed.targets[i]);
                    if (entry != null)
                    {
                        targets.Add(entry);
                    }
                }

                if (targets.Count == 0)
                {
                    failure = "'targets' dizisi boş ya da tüm girdilerin adı eksik";
                }
            }

            if (targets.Count == 0)
            {
                // Built-in default: kept in memory only, never written to the file.
                targets.Add(new DevTarget { name = "Local", ip = "127.0.0.1", port = ArenaProtocol.CONTROL_PORT });
                targets.Add(new DevTarget { name = "Kesif (beacon)", ip = "", port = ArenaProtocol.CONTROL_PORT });

                Debug.LogWarning(
                    $"[DevTargets] '{path}' kullanılamadı ({failure ?? "bilinmeyen sebep"}) — gömülü " +
                    "varsayılan hedeflerle devam ediliyor (Local + Kesif (beacon)). Dosya OLUŞTURULMADI; " +
                    "kalıcı hedef listesi istiyorsanız repo köküne elle ekleyin.");

                parsed = null;
            }

            defaultTargetName = ResolveDefaultTargetName(parsed);
            defaultRole = ResolveDefaultRole(parsed);
        }

        /// <summary>Drops nameless entries; trims the ip and corrects port &lt;= 0 to the control port.</summary>
        private static DevTarget Normalize(DevTarget source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.name))
            {
                return null;
            }

            return new DevTarget
            {
                name = source.name.Trim(),
                ip = string.IsNullOrWhiteSpace(source.ip) ? "" : source.ip.Trim(),
                port = source.port > 0 ? source.port : ArenaProtocol.CONTROL_PORT
            };
        }

        private static string ResolveDefaultTargetName(DevTargetsFile parsed)
        {
            string wanted = parsed != null ? parsed.defaultTarget : null;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                string trimmed = wanted.Trim();
                for (int i = 0; i < targets.Count; i++)
                {
                    if (string.Equals(targets[i].name, trimmed, StringComparison.Ordinal))
                    {
                        return trimmed;
                    }
                }

                Debug.LogWarning(
                    $"[DevTargets] defaultTarget '{trimmed}' hedef listesinde yok — " +
                    $"ilk hedef ('{targets[0].name}') varsayılan sayıldı.");
            }

            return targets[0].name;
        }

        private static string ResolveDefaultRole(DevTargetsFile parsed)
        {
            string wanted = parsed != null ? parsed.defaultRole : null;
            if (string.IsNullOrWhiteSpace(wanted))
            {
                return AppSession.RoleAdmin;
            }

            return string.Equals(wanted.Trim(), AppSession.RolePlayer, StringComparison.OrdinalIgnoreCase)
                ? AppSession.RolePlayer
                : AppSession.RoleAdmin;
        }
    }
}
