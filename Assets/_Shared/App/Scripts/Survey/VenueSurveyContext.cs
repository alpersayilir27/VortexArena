using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// What the survey scene needs to know about the scene it was launched FROM: the venue's old
    /// A/B frame, its name and the scene to return to.
    /// <para>
    /// ⚠️ It is captured at ENTRY, not read later: the survey scene has no <c>ArenaBoundary</c>, so
    /// once the arena is unloaded the template is gone. Static because the carrier (the scene load)
    /// cannot carry fields.
    /// </para>
    /// </summary>
    internal static class VenueSurveyContext
    {
        /// <summary>Does the source scene have a plan WITH calibration marks — i.e. may the export
        /// keep the file in the existing frame instead of inventing a new one.</summary>
        internal static bool HasTemplate;

        /// <summary>Venue name from the old plan; informational, may be empty.</summary>
        internal static string VenueName = "";

        /// <summary>The old plan's A/B marks in plan space (only meaningful with <see cref="HasTemplate"/>).</summary>
        internal static Vector2 TemplateA;

        /// <inheritdoc cref="TemplateA"/>
        internal static Vector2 TemplateB;

        /// <summary>Carried over so a re-survey does not silently reset the venue's column height.</summary>
        internal static float DefaultColumnHeight = 3f;

        /// <inheritdoc cref="DefaultColumnHeight"/>
        internal static float TopViewHeight;

        /// <summary>Scene the survey was started from; only a fallback — the return normally goes to
        /// the server's open scene.</summary>
        internal static string ReturnScene = "";

        /// <summary>Snapshots the source scene. A <c>null</c> boundary or plan is normal (a venue
        /// being measured for the FIRST time has no file yet).</summary>
        internal static void CaptureFrom(ArenaBoundary boundary, string returnScene)
        {
            Reset();
            ReturnScene = returnScene ?? "";

            ArenaDimensions plan = boundary != null ? boundary.Plan : null;
            if (plan == null)
            {
                return;
            }

            VenueName = plan.name ?? "";
            DefaultColumnHeight = plan.defaultColumnHeight > 0f ? plan.defaultColumnHeight : 3f;
            TopViewHeight = plan.topViewHeight;

            if (plan.HasCalibration)
            {
                HasTemplate = true;
                TemplateA = plan.calibration.a;
                TemplateB = plan.calibration.b;
            }
        }

        internal static void Reset()
        {
            HasTemplate = false;
            VenueName = "";
            TemplateA = Vector2.zero;
            TemplateB = Vector2.zero;
            DefaultColumnHeight = 3f;
            TopViewHeight = 0f;
            ReturnScene = "";
        }
    }
}
