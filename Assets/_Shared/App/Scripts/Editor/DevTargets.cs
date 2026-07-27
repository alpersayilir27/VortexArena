using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Adlandırılmış sunucu hedefi — <c>dev-targets.json</c> içindeki bir girdi.
    /// <para>
    /// <b>Boş <see cref="ip"/> = adres YOK</b> demektir; <see cref="DevSession.HasAddress"/>
    /// false döner ve istemci üretimdeki keşif zincirini (PlayerPrefs &gt; beacon &gt;
    /// StreamingAssets/arena.json) kullanır. "Kesif (beacon)" hedefi bilinçli olarak böyledir:
    /// beacon keşfini editörde denemenin yolu bu.
    /// </para>
    /// </summary>
    [Serializable]
    public class DevTarget
    {
        /// <summary>Hedefin adı — popup'ta görünen ve <see cref="DevSession.TargetName"/>'e yazılan anahtar.</summary>
        public string name;

        /// <summary>Sunucu IP'si. <b>Boş bırakılırsa keşif zinciri devralır</b> (adres yazılmaz).</summary>
        public string ip;

        /// <summary>WS kontrol portu; JSON'da 0/eksikse <see cref="ArenaProtocol.CONTROL_PORT"/>'a düzeltilir.</summary>
        public int port;

        /// <summary>Bu hedef somut bir adres mi (yoksa keşif kipi mi)?</summary>
        public bool HasAddress => !string.IsNullOrWhiteSpace(ip) && port > 0;

        /// <summary>Popup etiketi: <c>Local (127.0.0.1:47821)</c> / <c>Kesif (beacon) (keşif)</c>.</summary>
        public string Label => HasAddress ? $"{name} ({ip}:{port})" : $"{name} (keşif)";
    }

    /// <summary>JSON kök şeması — <see cref="JsonUtility"/> için düz alanlar.</summary>
    [Serializable]
    internal class DevTargetsFile
    {
        public string defaultTarget;
        public string defaultRole;
        public DevTarget[] targets;
    }

    /// <summary>
    /// Repo kökündeki <c>dev-targets.json</c> okuyucusu — dev penceresindeki hedef listesinin
    /// kaynağı.
    ///
    /// <para><b>Neden iki katmanlı config?</b> Hedeflerin adlandırılmış listesi (bu dosya)
    /// repo'da COMMIT'lidir: ekip "Local", "Kesif (beacon)", işletme PC'leri gibi adresleri bir
    /// kez yazar, herkes aynı listeyi görür. Buna karşılık o listeden HANGİ hedefin seçili
    /// olduğu KİŞİSELDİR ve <c>EditorPrefs</c>'te durur (<see cref="DevSession"/>). Seçim de
    /// commit'lenirse klasik "checked-in user settings" tuzağına düşeriz: herkes birbirinin
    /// IP'sini ezer, <c>git status</c> hep kirli kalır ve her rol/IP denemesi sahte bir diff
    /// üretir. Bu ayrım sayesinde <b>rol/hedef değiştirmek hiçbir dosyayı kirletmez</b>.</para>
    ///
    /// <para><b>Boş <c>ip</c> = keşif zinciri.</b> JSON'a yorum konamadığı için buraya yazılıyor:
    /// <c>"Kesif (beacon)"</c> girdisinin ip'si bilinçli olarak boştur → adres YAZILMAZ, istemci
    /// üretimdeki keşif zincirini (PlayerPrefs &gt; beacon &gt; StreamingAssets/arena.json)
    /// kullanır.</para>
    ///
    /// <para><b>Dosya yoksa/bozuksa kırılmaz:</b> bellekte <c>Local</c> + <c>Kesif (beacon)</c>
    /// varsayılanı üretilir ve bir kez uyarı loglanır. Repo'ya ASLA yazılmaz — dev aracı, ekip
    /// dosyasını sessizce oluşturup commit kirletmemeli.</para>
    /// </summary>
    public static class DevTargets
    {
        /// <summary>Katalog dosyasının adı (repo kökünde).</summary>
        public const string FileName = "dev-targets.json";

        private static List<DevTarget> targets;
        private static string defaultTargetName = "";
        private static string defaultRole = AppSession.RoleAdmin;
        private static bool fileFound;

        /// <summary>Katalog dosyasının tam yolu (repo kökü + <see cref="FileName"/>).</summary>
        public static string FilePath => Path.Combine(RepoRoot, FileName);

        /// <summary>Son yüklemede dosya gerçekten bulundu ve okunabildi mi?</summary>
        public static bool FileFound
        {
            get
            {
                EnsureLoaded();
                return fileFound;
            }
        }

        /// <summary>Adlandırılmış hedefler (en az bir eleman; dosya yoksa gömülü varsayılanlar).</summary>
        public static IReadOnlyList<DevTarget> Targets
        {
            get
            {
                EnsureLoaded();
                return targets;
            }
        }

        /// <summary>JSON'daki <c>defaultTarget</c> — listede yoksa ilk hedefin adına düşer.</summary>
        public static string DefaultTargetName
        {
            get
            {
                EnsureLoaded();
                return defaultTargetName;
            }
        }

        /// <summary>JSON'daki <c>defaultRole</c> — "player" | "admin" (geçersizse "admin").</summary>
        public static string DefaultRole
        {
            get
            {
                EnsureLoaded();
                return defaultRole;
            }
        }

        /// <summary>Ada göre hedef bulur (Ordinal, birebir). Bulamazsa false + null.</summary>
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

        /// <summary>Önbelleği atar ve dosyayı diskten tekrar okur (pencerede "Tazele").</summary>
        public static void Reload()
        {
            targets = null;
            EnsureLoaded();
        }

        // ------------------------------------------------------------------ yükleme

        /// <summary>Repo kökü = <c>Assets</c>'in üst klasörü (Application.dataPath'in parent'ı).</summary>
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
                // Gömülü varsayılan: dosyaya YAZMIYORUZ, yalnız bellekte tutuyoruz.
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

        /// <summary>Adı olmayan girdiyi atar; ip'yi kırpar, port &lt;= 0 ise kontrol portuna düzeltir.</summary>
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
