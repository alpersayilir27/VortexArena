using System;
using System.Collections.Generic;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace VortexArena.App.Admin
{
    /// <summary>Id and operator-facing name of a Windows audio output endpoint.</summary>
    public readonly struct AudioOutputDevice
    {
        /// <summary>MMDevice endpoint id (<c>"{0.0.0.00000000}.{guid}"</c> form).</summary>
        public readonly string Id;

        /// <summary>Name shown in the panel (<c>PKEY_Device_FriendlyName</c>).</summary>
        public readonly string Name;

        public AudioOutputDevice(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
        }

        /// <summary>Is the id set — an empty record means "no device".</summary>
        public bool IsValid => !string.IsNullOrEmpty(Id);
    }

    /// <summary>
    /// Lists Windows audio <b>output</b> (render) endpoints and switches the default device, from
    /// the admin spectator's preferences panel.
    /// <para>⚠️ <b>Compiles on every platform</b> but only works on Windows (this asmdef also ships
    /// to the Quest player): elsewhere <see cref="Supported"/> is false, the list comes back empty
    /// and <see cref="SetDefault"/> returns false — callers need no platform check.</para>
    /// <para>⚠️ No COM error escapes: device selection is a comfort feature and must not crash the
    /// spectator. Failures log a warning and return a safe value.</para>
    /// </summary>
    public static class WindowsAudioDevices
    {
        /// <summary>Can devices be listed/selected on this platform (Windows only).</summary>
        public static bool Supported
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            get => true;
#else
            get => false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // ---- MMDevice API constants -------------------------------------------------------

        private const int EDataFlowRender = 0;   // eRender — output endpoints
        private const int ERoleConsole = 0;      // eConsole
        private const int ERoleMultimedia = 1;   // eMultimedia
        private const int DeviceStateActive = 0x1;
        private const int StgmRead = 0;

        /// <summary>VT_LPWSTR — PROPVARIANT type carrying the device name.</summary>
        private const ushort VtLpwstr = 31;

        private static readonly Guid ClsidMMDeviceEnumerator =
            new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        private static readonly Guid ClsidPolicyConfigClient =
            new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");

        /// <summary>PKEY_Device_FriendlyName — property key holding the endpoint name.</summary>
        private static readonly PropertyKey PkeyDeviceFriendlyName = new PropertyKey(
            new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public int PropertyId;

            public PropertyKey(Guid formatId, int propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }
        }

        /// <summary>
        /// PROPVARIANT. The union starts at byte 8 (after <c>vt</c> + three reserved
        /// <c>ushort</c>s); a string value is an <c>LPWSTR</c> pointer.
        /// <para>⚠️ <b>The struct SIZE must match the native one exactly</b>, which is why the
        /// unused <see cref="unionTail"/> field stays and is never deleted: <c>GetValue</c> writes
        /// into memory we allocate, so a too-small struct makes the call overwrite neighbouring
        /// memory and <b>crash the editor/game instantly</b>. The largest union member is a
        /// count + pointer pair — 24 bytes on x64, 16 on x86; sequential layout fits both.</para>
        /// <para>⚠️ Without <see cref="PropVariantClear"/> after reading, the pointer's memory
        /// leaks.</para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;

            /// <summary>First word of the union — the string pointer under VT_LPWSTR.</summary>
            public IntPtr pointerValue;

            /// <summary>Rest of the union. Never read; only keeps the struct size correct.</summary>
            public IntPtr unionTail;
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IntPtr client);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out int count);

            [PreserveSig]
            int Item(int index, out IMMDevice device);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object instance);

            [PreserveSig]
            int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);

            [PreserveSig]
            int GetId(out IntPtr id);

            [PreserveSig]
            int GetState(out int state);
        }

        [ComImport]
        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out int count);

            [PreserveSig]
            int GetAt(int index, out PropertyKey key);

            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant value);

            [PreserveSig]
            int SetValue(ref PropertyKey key, ref PropVariant value);

            [PreserveSig]
            int Commit();
        }

        /// <summary>
        /// Windows' undocumented default-device interface (what the "Set as default" button in the
        /// sound settings uses).
        /// <para>⚠️ <b>The ten placeholder methods below are never deleted.</b> COM dispatches by
        /// vtable order, not by name: <c>SetDefaultEndpoint</c> is method 11 (index 10). Remove the
        /// placeholders and the call lands on a different method and crashes the process.</para>
        /// </summary>
        [ComImport]
        [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int Placeholder0();
            [PreserveSig] int Placeholder1();
            [PreserveSig] int Placeholder2();
            [PreserveSig] int Placeholder3();
            [PreserveSig] int Placeholder4();
            [PreserveSig] int Placeholder5();
            [PreserveSig] int Placeholder6();
            [PreserveSig] int Placeholder7();
            [PreserveSig] int Placeholder8();
            [PreserveSig] int Placeholder9();

            [PreserveSig]
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        }

#endif

        /// <summary>
        /// Writes the active output endpoints into <paramref name="into"/> (cleared first).
        /// <para>⚠️ A fresh enumerator per call, <b>no static cache</b>: devices come and go, and a
        /// stale list would let the operator pick a speaker that is not there.</para>
        /// </summary>
        public static void Collect(List<AudioOutputDevice> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator == null)
                {
                    return;
                }

                if (enumerator.EnumAudioEndpoints(EDataFlowRender, DeviceStateActive, out collection) != 0
                    || collection == null)
                {
                    return;
                }

                if (collection.GetCount(out int count) != 0)
                {
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        if (collection.Item(i, out device) != 0 || device == null)
                        {
                            continue;
                        }

                        string id = ReadId(device);
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        string name = ReadFriendlyName(device);
                        // An endpoint with no resolvable name stays listed under its id.
                        into.Add(new AudioOutputDevice(id, string.IsNullOrEmpty(name) ? id : name));
                    }
                    finally
                    {
                        Release(device);
                    }
                }
            }
            catch (Exception e)
            {
                into.Clear();
                Warn("cihaz listesi alınamadı", e);
            }
            finally
            {
                Release(collection);
                Release(enumerator);
            }
#endif
        }

        /// <summary>Id of the current default output endpoint; <c>""</c> when there is no device
        /// (<c>E_NOTFOUND</c>) or on error.</summary>
        public static string GetDefaultId()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator == null)
                {
                    return string.Empty;
                }

                // No device at all returns E_NOTFOUND — not a fault, just an empty id.
                if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleConsole, out device) != 0
                    || device == null)
                {
                    return string.Empty;
                }

                return ReadId(device);
            }
            catch (Exception e)
            {
                Warn("varsayılan cihaz okunamadı", e);
                return string.Empty;
            }
            finally
            {
                Release(device);
                Release(enumerator);
            }
#else
            return string.Empty;
#endif
        }

        /// <summary>
        /// Makes the given endpoint the Windows default output; true on success.
        /// <para>⚠️ Only the <c>eConsole</c> and <c>eMultimedia</c> roles are written;
        /// <c>eCommunications</c> is <b>deliberately skipped</b> — it is a separate Windows setting
        /// and overwriting it would silently move the operator's mic/VoIP setup to another device.</para>
        /// </summary>
        public static bool SetDefault(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return false;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            object raw = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(ClsidPolicyConfigClient);
                if (type == null)
                {
                    return false;
                }

                raw = Activator.CreateInstance(type);
                if (!(raw is IPolicyConfig policy))
                {
                    return false;
                }

                bool ok = policy.SetDefaultEndpoint(deviceId, ERoleConsole) == 0;
                ok &= policy.SetDefaultEndpoint(deviceId, ERoleMultimedia) == 0;
                return ok;
            }
            catch (Exception e)
            {
                Warn("varsayılan cihaz değiştirilemedi", e);
                return false;
            }
            finally
            {
                Release(raw);
            }
#else
            return false;
#endif
        }

        /// <summary>Friendly name of the endpoint with the given id; <c>""</c> when it is missing
        /// or disabled — the cheapest check whether a stored choice is still valid.</summary>
        public static string NameOf(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return string.Empty;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator == null)
                {
                    return string.Empty;
                }

                if (enumerator.GetDevice(deviceId, out device) != 0 || device == null)
                {
                    return string.Empty;
                }

                return ReadFriendlyName(device);
            }
            catch (Exception e)
            {
                Warn("cihaz adı okunamadı", e);
                return string.Empty;
            }
            finally
            {
                Release(device);
                Release(enumerator);
            }
#else
            return string.Empty;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        private static IMMDeviceEnumerator CreateEnumerator()
        {
            Type type = Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator);
            if (type == null)
            {
                return null;
            }

            return Activator.CreateInstance(type) as IMMDeviceEnumerator;
        }

        /// <summary>Reads the endpoint id; ⚠️ the caller must free the buffer COM allocated.</summary>
        private static string ReadId(IMMDevice device)
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                if (device.GetId(out ptr) != 0 || ptr == IntPtr.Zero)
                {
                    return string.Empty;
                }

                return Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }

        private static string ReadFriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                if (device.OpenPropertyStore(StgmRead, out store) != 0 || store == null)
                {
                    return string.Empty;
                }

                PropertyKey key = PkeyDeviceFriendlyName;
                if (store.GetValue(ref key, out PropVariant value) != 0)
                {
                    return string.Empty;
                }

                try
                {
                    // ⚠️ Check the type FIRST: outside VT_LPWSTR the union's first word is a raw
                    // number, not a pointer, and decoding it as text crashes the process.
                    return value.vt != VtLpwstr || value.pointerValue == IntPtr.Zero
                        ? string.Empty
                        : Marshal.PtrToStringUni(value.pointerValue) ?? string.Empty;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Release(store);
            }
        }

        private static void Release(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
            {
                return;
            }

            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (Exception)
            {
                // A release failure is none of the caller's business; the object is done with.
            }
        }

        private static void Warn(string what, Exception e)
        {
            Debug.LogWarning($"[WindowsAudioDevices] {what}: {e.Message}");
        }

#endif
    }
}
