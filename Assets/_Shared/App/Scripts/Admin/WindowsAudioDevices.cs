using System;
using System.Collections.Generic;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace VortexArena.App.Admin
{
    /// <summary>Bir Windows ses çıkış ucunun kimliği ve operatöre gösterilen adı.</summary>
    public readonly struct AudioOutputDevice
    {
        /// <summary>MMDevice uç kimliği (<c>"{0.0.0.00000000}.{guid}"</c> biçimi).</summary>
        public readonly string Id;

        /// <summary>Panelde görünen ad (<c>PKEY_Device_FriendlyName</c>).</summary>
        public readonly string Name;

        public AudioOutputDevice(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
        }

        /// <summary>Kimliği dolu mu — boş kayıt "cihaz yok" demektir.</summary>
        public bool IsValid => !string.IsNullOrEmpty(Id);
    }

    /// <summary>
    /// Windows'un ses <b>çıkış</b> (render) uçlarını listeler ve varsayılan cihazı değiştirir.
    /// Admin gözlemcisinin tercih panelinden kullanılır: operatör duyuruların hangi hoparlörden
    /// çalacağını Windows ayarlarına gitmeden seçebilsin.
    /// <para>
    /// ⚠️ Sınıf <b>tüm platformlarda derlenir</b> ama iş yalnız Windows'ta yapılır (bu asmdef
    /// Quest oyuncusuna da giriyor): Windows dışında <see cref="Supported"/> false'tur, liste boş
    /// döner ve <see cref="SetDefault"/> false verir. Çağıran taraf platform kontrolü yazmasın.
    /// </para>
    /// <para>
    /// ⚠️ Hiçbir COM hatası dışarı sızmaz — ses cihazı seçimi bir konfor özelliğidir, gözlemciyi
    /// çökertmemeli. Hata bir uyarı satırı olarak konsola düşer ve güvenli değer döner.
    /// </para>
    /// </summary>
    public static class WindowsAudioDevices
    {
        /// <summary>Bu platformda cihaz listeleme/seçme mümkün mü (yalnız Windows).</summary>
        public static bool Supported
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            get => true;
#else
            get => false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        // ---- MMDevice API sabitleri -------------------------------------------------------

        private const int EDataFlowRender = 0;   // eRender  — çıkış uçları
        private const int ERoleConsole = 0;      // eConsole
        private const int ERoleMultimedia = 1;   // eMultimedia
        private const int DeviceStateActive = 0x1;
        private const int StgmRead = 0;

        /// <summary>VT_LPWSTR — cihaz adının geldiği PROPVARIANT türü.</summary>
        private const ushort VtLpwstr = 31;

        private static readonly Guid ClsidMMDeviceEnumerator =
            new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        private static readonly Guid ClsidPolicyConfigClient =
            new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");

        /// <summary>PKEY_Device_FriendlyName — uç adının okunacağı özellik anahtarı.</summary>
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
        /// PROPVARIANT. Veri birleşimi 8. bayttan başlar (öncesi <c>vt</c> + üç ayrılmış
        /// <c>ushort</c>) ve string değer bir <c>LPWSTR</c> işaretçisidir.
        /// <para>
        /// ⚠️ <b>Yapının BOYUTU native karşılığıyla birebir olmak zorundadır</b> — bu yüzden
        /// kullanılmayan <see cref="unionTail"/> alanı da durur ve SİLİNMEZ. <c>GetValue</c>
        /// çıktıyı bizim ayırdığımız belleğe yazar: yapı küçük kalırsa (birleşimin yalnız ilk
        /// işaretçisi bildirilirse) çağrı komşu belleği ezer ve <b>editörü/oyunu anında
        /// çökertir</b>. Birleşimin en büyük üyesi bir sayaç + işaretçi çiftidir, yani x64'te
        /// toplam 24, x86'da 16 bayt; ardışık yerleşim ikisini de kendiliğinden tutturur.
        /// </para>
        /// <para>Okuduktan sonra <see cref="PropVariantClear"/> çağrılmazsa işaretçinin belleği
        /// sızar.</para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;

            /// <summary>Birleşimin ilk sözcüğü — VT_LPWSTR'de metnin işaretçisi.</summary>
            public IntPtr pointerValue;

            /// <summary>Birleşimin kalanı. Okunmaz; yalnız yapının boyutunu doğru tutar.</summary>
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
        /// Windows'un belgelenmemiş varsayılan-cihaz arayüzü (ses ayarlarındaki "Varsayılan yap"
        /// düğmesinin arkası).
        /// <para>
        /// ⚠️ <b>Aşağıdaki on yer tutucu metot SİLİNMEZ.</b> COM çağrısı ada göre değil vtable
        /// sırasına göre yapılır: <c>SetDefaultEndpoint</c> bu arayüzün 11. metodudur (indeks 10).
        /// Yer tutucular kaldırılırsa çağrı bambaşka bir metoda gider ve süreç çöker.
        /// </para>
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
        /// Etkin ses çıkış uçlarını <paramref name="into"/> listesine yazar; liste önce temizlenir.
        /// <para>
        /// ⚠️ Her çağrıda yeni bir numaralandırıcı kurulur, <b>statik önbellek tutulmaz</b>:
        /// cihazlar takılıp çıkarılıyor ve bayat bir liste operatöre var olmayan bir hoparlörü
        /// seçtirir.
        /// </para>
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
                        // Adı çözülemeyen uç listeden düşmez: kimliğiyle de olsa seçilebilmeli.
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

        /// <summary>
        /// Şu anki varsayılan çıkış ucunun kimliği; hiç ses cihazı yoksa (<c>E_NOTFOUND</c>) ya da
        /// hata olursa <c>""</c>.
        /// </summary>
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

                // Cihaz hiç yoksa E_NOTFOUND döner — bu bir arıza değil, sessizce boş kimliktir.
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
        /// Verilen ucu Windows'un varsayılan çıkış cihazı yapar; başarıysa true.
        /// <para>
        /// ⚠️ Yalnız <c>eConsole</c> ve <c>eMultimedia</c> rolleri yazılır; <c>eCommunications</c>
        /// <b>bilerek atlanır</b>. Windows'ta "Varsayılan Cihaz" ile "Varsayılan İletişim Cihazı"
        /// ayrı ayarlardır — ikincisini de ezmek operatörün mikrofon/VoIP kurulumunu sessizce
        /// başka bir cihaza taşırdı.
        /// </para>
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

        /// <summary>
        /// Kimliği verilen ucun görünen adı; cihaz yoksa/kapalıysa <c>""</c>. Kayıtlı bir seçimin
        /// hâlâ geçerli olup olmadığını sınamanın en ucuz yolu budur.
        /// </summary>
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

        /// <summary>Uç kimliğini okur; COM'un ayırdığı tamponu çağıran serbest bırakmak zorundadır.</summary>
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
                    // ⚠️ Tür ÖNCE sınanır: birleşimin ilk sözcüğü VT_LPWSTR dışında bir türde
                    // işaretçi değil ham sayı taşır ve onu metin diye çözmek süreci çökertir.
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
                // Bırakma hatası çağıranı ilgilendirmez; nesne zaten kullanılmayacak.
            }
        }

        private static void Warn(string what, Exception e)
        {
            Debug.LogWarning($"[WindowsAudioDevices] {what}: {e.Message}");
        }

#endif
    }
}
