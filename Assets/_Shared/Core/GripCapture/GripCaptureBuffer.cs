using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// Captures of one Play session, kept on disk until the editor writes them into the
    /// <see cref="ItemDefinition"/> on Play exit.
    /// <para>⚠️ Written on every capture, not on exit: a crash in Play must not lose the session.
    /// Lives under <c>Library/</c> (machine-local, never committed).</para>
    /// </summary>
    [Serializable]
    public sealed class GripCaptureBuffer
    {
        public string definitionGuid;
        public List<GripCaptureEntry> entries = new List<GripCaptureEntry>();

        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..",
            "Library", "VortexArena", "GripCapture.json"));

        public static bool Exists => File.Exists(FilePath);

        /// <summary>Reads the buffer; <c>null</c> when missing or unreadable.</summary>
        public static GripCaptureBuffer Load()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var buffer = JsonUtility.FromJson<GripCaptureBuffer>(File.ReadAllText(path));
                if (buffer != null && buffer.entries == null)
                {
                    buffer.entries = new List<GripCaptureEntry>();
                }

                return buffer;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GripCapture] Yakalama tamponu okunamadı ({path}): {e.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void Save()
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        /// <summary>Adds or overwrites the slot (last capture wins).</summary>
        public void Put(GripSocketKind kind, bool rightHand, in ItemGripPose pose)
        {
            var entry = new GripCaptureEntry { kind = kind, rightHand = rightHand, pose = pose };
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].kind == kind && entries[i].rightHand == rightHand)
                {
                    entries[i] = entry;
                    return;
                }
            }

            entries.Add(entry);
        }

        public bool Has(GripSocketKind kind, bool rightHand)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].kind == kind && entries[i].rightHand == rightHand)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasKind(GripSocketKind kind)
        {
            return Has(kind, true) || Has(kind, false);
        }
    }
}
