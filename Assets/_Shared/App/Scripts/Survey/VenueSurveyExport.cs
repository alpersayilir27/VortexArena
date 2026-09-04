using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Protocol;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// Turns captured world points into the venue's dimensions file: frame normalisation, the wire
    /// DTO and the local copy.
    /// <para>
    /// ⚠️ <b>The output frame is the OLD file's frame whenever there is one</b> (the A/B tapes on the
    /// floor do not move; what a re-survey fixes is where the WALLS sit relative to them). Existing
    /// scene art is built on that frame, so re-basing the file would move every arena instead of
    /// correcting it. Only a first survey defines its own frame: A at the origin, B on +Y.
    /// </para>
    /// </summary>
    internal static class VenueSurveyExport
    {
        /// <summary>Above this relative span difference the two frames probably are not the same
        /// tapes; the upload still happens, the operator gets a warning.</summary>
        private const float SpanMismatchRatio = 0.1f;

        /// <summary>Builds the plan. <paramref name="calibration"/> needs 2 points and
        /// <paramref name="corners"/> at least <c>SURVEY_MIN_PLANE_POINTS</c> — the caller gates
        /// that.</summary>
        /// <param name="warning">Operator-facing warning, empty when there is none.</param>
        internal static ArenaDimensions Build(
            IReadOnlyList<Vector3> calibration,
            IReadOnlyList<Vector3> corners,
            IReadOnlyList<IReadOnlyList<Vector3>> columns,
            out string warning)
        {
            warning = "";

            Vector2 newA = Flatten(calibration[0]);
            Vector2 newB = Flatten(calibration[1]);
            Vector2 newDir = newB - newA;

            Vector2 targetDir;
            Vector2 origin;

            if (VenueSurveyContext.HasTemplate)
            {
                Vector2 oldA = VenueSurveyContext.TemplateA;
                Vector2 oldB = VenueSurveyContext.TemplateB;
                targetDir = oldB - oldA;
                origin = oldA;

                float oldSpan = targetDir.magnitude;
                float newSpan = newDir.magnitude;
                if (oldSpan > 0f &&
                    Mathf.Abs(newSpan - oldSpan) > oldSpan * SpanMismatchRatio)
                {
                    warning =
                        $"A-B mesafesi eski dosyadan farklı (yeni {newSpan:0.00} m, eski " +
                        $"{oldSpan:0.00} m) — aynı bantlar ölçüldü mü?";
                }
            }
            else
            {
                // No template: A becomes the origin and B lands on +Y.
                targetDir = Vector2.up;
                origin = Vector2.zero;
            }

            float angle = SignedAngle(newDir, targetDir);
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            var plan = new ArenaDimensions
            {
                name = VenueSurveyContext.VenueName ?? "",
                defaultColumnHeight = VenueSurveyContext.DefaultColumnHeight > 0f
                    ? VenueSurveyContext.DefaultColumnHeight
                    : 3f,
                topViewHeight = VenueSurveyContext.TopViewHeight,
                plane = new Vector2[corners.Count]
            };

            for (int i = 0; i < corners.Count; i++)
            {
                plan.plane[i] = Map(Flatten(corners[i]), newA, origin, cos, sin);
            }

            var built = new List<ArenaDimensions.Column>(columns.Count);
            for (int i = 0; i < columns.Count; i++)
            {
                IReadOnlyList<Vector3> ring = columns[i];
                if (ring == null || ring.Count < Polygon2D.MinPoints)
                {
                    continue;
                }

                var points = new Vector2[ring.Count];
                for (int k = 0; k < ring.Count; k++)
                {
                    points[k] = Map(Flatten(ring[k]), newA, origin, cos, sin);
                }

                built.Add(new ArenaDimensions.Column
                {
                    name = $"Kolon_{built.Count + 1}",

                    // 0 = the file's defaultColumnHeight; the survey measures no height.
                    height = 0f,
                    points = points
                });
            }

            plan.columns = built.ToArray();
            plan.calibration = new ArenaDimensions.CalibrationMarks
            {
                a = Map(newA, newA, origin, cos, sin),
                b = Map(newB, newA, origin, cos, sin)
            };

            return plan;
        }

        /// <summary>Wraps the plan into the upload message (the server writes it to disk as-is).</summary>
        internal static VenueSurveyMsg ToMessage(ArenaDimensions plan)
        {
            var dimensions = new VenueSurveyDimensions
            {
                name = plan.name ?? "",
                plane = ToPoints(plan.plane),
                calibration = new VenueSurveyCalibration
                {
                    a = ToPoint(plan.calibration.a),
                    b = ToPoint(plan.calibration.b)
                },
                defaultColumnHeight = plan.defaultColumnHeight,
                topViewHeight = plan.topViewHeight
            };

            ArenaDimensions.Column[] columns = plan.columns ?? Array.Empty<ArenaDimensions.Column>();
            dimensions.columns = new VenueSurveyColumn[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                dimensions.columns[i] = new VenueSurveyColumn
                {
                    name = columns[i].name ?? "",
                    height = columns[i].height,
                    points = ToPoints(columns[i].points)
                };
            }

            return new VenueSurveyMsg { dimensions = dimensions };
        }

        /// <summary>
        /// Writes a copy next to the app's own data. ⚠️ This is the ONLY output when no server is
        /// connected, so a failure here is reported instead of swallowed.
        /// </summary>
        /// <returns>The written path, or an empty string on failure.</returns>
        internal static string SaveLocalCopy(ArenaDimensions plan)
        {
            try
            {
                string venue = string.IsNullOrWhiteSpace(plan.name) ? "venue" : plan.name.Trim();
                foreach (char invalid in Path.GetInvalidFileNameChars())
                {
                    venue = venue.Replace(invalid, '_');
                }

                string folder = Path.Combine(Application.persistentDataPath, "VenueSurvey");
                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, venue + ArenaProtocol.SURVEY_FILE_SUFFIX);
                File.WriteAllText(path, plan.ToJson());
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VenueSurvey] Yerel kopya yazılamadı: {exception.Message}");
                return "";
            }
        }

        /// <summary>World point → plan XZ (the plan's <c>y</c> is world Z).</summary>
        private static Vector2 Flatten(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        private static Vector2 Map(Vector2 point, Vector2 from, Vector2 to, float cos, float sin)
        {
            Vector2 local = point - from;
            var rotated = new Vector2(
                local.x * cos - local.y * sin,
                local.x * sin + local.y * cos);

            return Round(rotated + to);
        }

        /// <summary>Signed angle that turns <paramref name="u"/> onto <paramref name="v"/> (radians).</summary>
        private static float SignedAngle(Vector2 u, Vector2 v)
        {
            return Mathf.Atan2(u.x * v.y - u.y * v.x, u.x * v.x + u.y * v.y);
        }

        /// <summary>Millimetres: the tracking noise below that is not a measurement.</summary>
        private static Vector2 Round(Vector2 point)
        {
            return new Vector2(
                Mathf.Round(point.x * 1000f) / 1000f,
                Mathf.Round(point.y * 1000f) / 1000f);
        }

        private static VenueSurveyPoint ToPoint(Vector2 point)
        {
            return new VenueSurveyPoint { x = point.x, y = point.y };
        }

        private static VenueSurveyPoint[] ToPoints(Vector2[] ring)
        {
            if (ring == null)
            {
                return Array.Empty<VenueSurveyPoint>();
            }

            var points = new VenueSurveyPoint[ring.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                points[i] = ToPoint(ring[i]);
            }

            return points;
        }
    }
}
