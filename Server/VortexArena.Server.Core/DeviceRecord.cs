#nullable enable
namespace VortexArena.Server.Core;

/// <summary><c>devices.json</c>'daki tek bir cihaz satırı: <c>deviceId → { name, number }</c> (§2).
/// <para>
/// <b>Alan değil property</b>: bu tip protokol DTO'su değildir, sunucunun kendi config dosyasıdır
/// ve <c>System.Text.Json</c>'a <c>IncludeFields</c> olmadan (camelCase policy ile) yazılır.
/// </para>
/// <para>
/// <b>Numara benzersizliği burada yaşar:</b> "iki cihaz aynı numarayı taşımaz" değişmez kuralı
/// çevrimiçi oyuncular arasında değil, bu dosyadaki TÜM kayıtlar arasında geçerlidir — bir gözlük
/// numarasını yıllar boyunca korusun diye. Sahiplik sorgusu hep bu haritadan yapılır: hiç
/// bağlanmamış (bellekte <c>PlayerState</c>'i olmayan) bir cihaz da numara tutuyor olabilir.
/// </para></summary>
public sealed class DeviceRecord
{
    /// <summary>Ad havuzundan atanan ya da <c>set_identity</c> ile değiştirilen ad. Benzersiz
    /// DEĞİLDİR (havuz 20 isimdir, 21. cihazdan sonra tekrar eder).</summary>
    public string Name { get; set; } = "";

    /// <summary>Forma numarası 1..99; <c>0</c> = atanmamış (v1 dosyasından yükseltilen kayıtlar ve
    /// numara havuzu dolduğunda). Tek benzersiz olmayan değer budur.</summary>
    public int Number { get; set; }
}
