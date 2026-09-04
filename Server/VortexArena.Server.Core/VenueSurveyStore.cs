#nullable enable
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Writes a controller-taken venue survey (§10.11) next to the server exe as
/// <c>&lt;Venue&gt;_dimensions.json</c>.
/// <para>
/// ⚠️ <b>The file must stay readable by <c>ArenaDimensions.Parse</c></b> (Unity side, JsonUtility):
/// the survey DTOs mirror that schema field for field, so the message is serialized AS IS — no
/// second, hand-written writer that could drift from the schema.
/// </para>
/// <para>
/// ⚠️ The previous file is copied to <c>&lt;Venue&gt;_dimensions.prev.json</c> before the write:
/// a survey is taken on site with no undo, and an accidental second run must not destroy the
/// measurement that already works.
/// </para></summary>
public static class VenueSurveyStore
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        IncludeFields = true, // the DTOs expose public FIELDS (§7)
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep Turkish venue names readable
    };

    /// <summary>BOM would make Unity's JsonUtility choke on the first character.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Validates and writes the survey. Returns the full path, or <c>null</c> with the
    /// reason in <paramref name="error"/>.</summary>
    public static string? Save(string venue, VenueSurveyDimensions? dims, out string error)
    {
        error = "";

        if (dims == null)
        {
            error = "Ölçüm verisi boş.";
            return null;
        }

        var plane = dims.plane;
        if (plane == null || plane.Length < ArenaProtocol.SURVEY_MIN_PLANE_POINTS)
        {
            error = $"Zemin halkası en az {ArenaProtocol.SURVEY_MIN_PLANE_POINTS} köşe içermeli " +
                    $"(gelen: {plane?.Length ?? 0}).";
            return null;
        }
        if (!IsFiniteRing(plane))
        {
            error = "Zemin halkasında geçersiz sayı var.";
            return null;
        }

        var calibration = dims.calibration;
        if (calibration?.a == null || calibration.b == null)
        {
            error = "Kalibrasyon noktaları eksik.";
            return null;
        }
        if (!IsFinite(calibration.a) || !IsFinite(calibration.b))
        {
            error = "Kalibrasyon noktalarında geçersiz sayı var.";
            return null;
        }

        var dx = calibration.b.x - calibration.a.x;
        var dy = calibration.b.y - calibration.a.y;
        var span = MathF.Sqrt((dx * dx) + (dy * dy));
        if (span < ArenaProtocol.SURVEY_MIN_CALIBRATION_SPAN)
        {
            error = $"A-B mesafesi çok kısa: {span:F2} m " +
                    $"(en az {ArenaProtocol.SURVEY_MIN_CALIBRATION_SPAN:F2} m).";
            return null;
        }

        if (!float.IsFinite(dims.defaultColumnHeight) || !float.IsFinite(dims.topViewHeight))
        {
            error = "Yükseklik alanlarında geçersiz sayı var.";
            return null;
        }

        // A column with fewer than 3 corners produces no prism: dropped here instead of travelling
        // into the file, where the reader would silently filter it anyway.
        dims.columns = FilterColumns(dims.columns);

        var safeName = SafeFileName(venue);
        var path = Path.Combine(AppContext.BaseDirectory, safeName + ArenaProtocol.SURVEY_FILE_SUFFIX);
        var previousPath = Path.ChangeExtension(path, null) + ".prev.json";
        var tempPath = path + ".tmp";

        string json;
        try
        {
            var node = JsonSerializer.SerializeToNode(dims, FileJsonOptions);
            if (node == null)
            {
                error = "Ölçüm verisi JSON'a çevrilemedi.";
                return null;
            }

            // Unity's ToJson omits it too: a written 0 would add a meaningless line to every file.
            if (node is JsonObject root && dims.topViewHeight <= 0f) root.Remove("topViewHeight");
            json = node.ToJsonString(FileJsonOptions);
        }
        catch (Exception ex)
        {
            error = "Ölçüm verisi JSON'a çevrilemedi: " + ex.Message;
            return null;
        }

        try
        {
            File.WriteAllText(tempPath, json, Utf8NoBom);
            if (File.Exists(path)) File.Copy(path, previousPath, overwrite: true);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            error = "Dosya yazılamadı: " + ex.Message;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* temp leftover is harmless */ }
            return null;
        }

        Console.WriteLine($"[Survey] '{safeName}' mekan ölçümü kaydedildi → {path} " +
                          $"(köşe {plane.Length}, kolon {dims.columns.Length}).");
        return path;
    }

    private static VenueSurveyColumn[] FilterColumns(VenueSurveyColumn[]? columns)
    {
        if (columns == null || columns.Length == 0) return Array.Empty<VenueSurveyColumn>();

        var kept = new List<VenueSurveyColumn>(columns.Length);
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            var points = column?.points;
            if (column == null || points == null || points.Length < ArenaProtocol.SURVEY_MIN_PLANE_POINTS ||
                !IsFiniteRing(points) || !float.IsFinite(column.height))
            {
                Console.WriteLine($"[Survey] kolon {i + 1} atlandı — geçersiz köşe verisi " +
                                  $"({points?.Length ?? 0} nokta).");
                continue;
            }

            column.name ??= "";
            kept.Add(column);
        }

        return kept.ToArray();
    }

    private static bool IsFiniteRing(VenueSurveyPoint[] ring)
    {
        for (var i = 0; i < ring.Length; i++)
        {
            if (!IsFinite(ring[i])) return false;
        }

        return true;
    }

    private static bool IsFinite(VenueSurveyPoint? point) =>
        point != null && float.IsFinite(point.x) && float.IsFinite(point.y);

    /// <summary>Venue name → file name: the venue id comes from config and may carry anything.</summary>
    private static string SafeFileName(string? venue)
    {
        var name = venue ?? "";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim();
        return name.Length == 0 ? "Venue" : name;
    }
}
