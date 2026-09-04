using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// Drives one on-site survey session inside the survey scene: three capture modes (calibration
    /// marks → wall corners → columns), the guide geometry and the upload.
    /// <para>
    /// ⚠️ Created from code by <see cref="VenueSurveyGesture"/> and owned by the scene: leaving the
    /// scene THROWS THE MEASUREMENT AWAY. That is deliberate — a half-finished survey surviving into
    /// the next scene would silently mix two rooms' points.
    /// </para>
    /// </summary>
    public class VenueSurveyController : MonoBehaviour
    {
        /// <summary>Capture stages. ⚠️ Never serialized — it is a session cursor, not saved data.</summary>
        private enum SurveyMode
        {
            Calibration,
            Corners,
            Columns
        }

        private static readonly Color CornerColor = new Color(0.3f, 0.9f, 0.4f);
        private static readonly Color ColumnPointColor = new Color(0.95f, 0.6f, 0.2f);

        private const float ReturnDelaySeconds = 2f;

        private Transform pointer;
        private Transform head;

        private VenueSurveyInput input;
        private VenueSurveyGuide guide;
        private VenueSurveyLabel label;

        private SurveyMode mode = SurveyMode.Calibration;
        private bool finished;

        private readonly List<Vector3> calibration = new List<Vector3>(2);
        private readonly List<GameObject> calibrationMarks = new List<GameObject>(2);

        private readonly List<Vector3> corners = new List<Vector3>();
        private readonly List<GameObject> cornerMarks = new List<GameObject>();

        private readonly List<List<Vector3>> columns = new List<List<Vector3>>();
        private readonly List<List<GameObject>> columnMarks = new List<List<GameObject>>();
        private readonly List<GameObject> columnBodies = new List<GameObject>();

        private readonly List<Vector3> currentColumn = new List<Vector3>();
        private readonly List<GameObject> currentColumnMarks = new List<GameObject>();

        private GameObject shell;
        private string lastEvent = "";

        private void Awake()
        {
            // ⚠️ The rig is found by type, never through Camera.main (Yapma-Listesi).
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null)
            {
                Debug.LogError("[VenueSurvey] Sahnede OVRCameraRig yok; ölçüm alınamaz.");
            }
            else
            {
                pointer = rig.rightControllerAnchor;
                head = rig.centerEyeAnchor;
            }

            input = new VenueSurveyInput(VenueSurveyHaptics.Hand);
            // The A+B hold that opened this scene may still be down: it must not count as "finish".
            input.RequireRelease();
            guide = new VenueSurveyGuide();

            if (head != null)
            {
                label = new VenueSurveyLabel(head);
            }

            UpdateLabel();
        }

        private void OnDestroy()
        {
            if (finished)
            {
                return;
            }

            Debug.LogWarning("[VenueSurvey] Ölçüm sahnesi kapandı — alınan noktalar atıldı.");
            VenueSurveyContext.Reset();
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            input.Tick();

            if (input.CaptureFired)
            {
                HandleCapture();
            }
            else if (input.ModeSwitchFired)
            {
                HandleModeSwitch();
            }
            else if (input.UndoFired)
            {
                HandleUndo();
            }
            else if (input.EnterExitFired)
            {
                HandleFinish();
            }

            UpdateLabel();
        }

        // ------------------------------------------------------------------ capture

        private void HandleCapture()
        {
            if (pointer == null)
            {
                Reject("kumanda yok — nokta alınamadı");
                return;
            }

            Vector3 point = pointer.position;

            switch (mode)
            {
                case SurveyMode.Calibration:
                    CaptureCalibration(point);
                    break;

                case SurveyMode.Corners:
                    CaptureCorner(point);
                    break;

                default:
                    CaptureColumnPoint(point);
                    break;
            }
        }

        private void CaptureCalibration(Vector3 point)
        {
            if (calibration.Count >= 2)
            {
                Reject("iki nokta alındı — mod değiştir ya da geri al");
                return;
            }

            bool isA = calibration.Count == 0;

            if (!isA)
            {
                float span = Flat(point - calibration[0]).magnitude;
                if (span < ArenaProtocol.SURVEY_MIN_CALIBRATION_SPAN)
                {
                    Reject($"A-B mesafesi çok kısa ({span:0.00} m); en az " +
                           $"{ArenaProtocol.SURVEY_MIN_CALIBRATION_SPAN:0.00} m olmalı");
                    return;
                }
            }

            calibration.Add(point);
            calibrationMarks.Add(guide.PlaceCalibrationCube(point, isA));

            Accept($"{calibration.Count}/2 {(isA ? "A" : "B")} alındı ({point.x:0.00}, {point.z:0.00})");
        }

        private void CaptureCorner(Vector3 point)
        {
            corners.Add(point);
            cornerMarks.Add(guide.PlaceMarker(point, CornerColor));
            Accept($"Köşe {corners.Count} alındı ({point.x:0.00}, {point.z:0.00})");
        }

        private void CaptureColumnPoint(Vector3 point)
        {
            currentColumn.Add(point);
            currentColumnMarks.Add(guide.PlaceMarker(point, ColumnPointColor));

            if (currentColumn.Count < ArenaProtocol.SURVEY_COLUMN_POINTS)
            {
                Accept($"Kolon {columns.Count + 1}: {currentColumn.Count}/" +
                       $"{ArenaProtocol.SURVEY_COLUMN_POINTS} nokta " +
                       $"({point.x:0.00}, {point.z:0.00})");
                return;
            }

            Vector2[] ring = ToRing(currentColumn);
            if (!Polygon2D.IsValid(ring))
            {
                DestroyAll(currentColumnMarks);
                currentColumn.Clear();
                Reject("kolon geçersiz — dört nokta yeniden alınmalı");
                return;
            }

            if (Polygon2D.IsSelfIntersecting(ring))
            {
                Debug.LogWarning(
                    $"[VenueSurvey] Kolon {columns.Count + 1} kendini kesiyor — köşe sırasını " +
                    "kontrol edin (kabul edildi).");
            }

            columns.Add(new List<Vector3>(currentColumn));
            columnMarks.Add(new List<GameObject>(currentColumnMarks));
            columnBodies.Add(guide.BuildColumn(ring, VenueSurveyContext.DefaultColumnHeight, columns.Count));

            currentColumn.Clear();
            currentColumnMarks.Clear();

            Accept($"Kolon {columns.Count} tamamlandı");
        }

        // ------------------------------------------------------------------ mode switch

        private void HandleModeSwitch()
        {
            switch (mode)
            {
                case SurveyMode.Calibration:
                    if (calibration.Count < 2)
                    {
                        Reject("önce iki kalibrasyon noktası alınmalı");
                        return;
                    }

                    mode = SurveyMode.Corners;
                    break;

                case SurveyMode.Corners:
                {
                    Vector2[] ring = ToRing(corners);
                    if (corners.Count < ArenaProtocol.SURVEY_MIN_PLANE_POINTS || !Polygon2D.IsValid(ring))
                    {
                        Reject($"en az {ArenaProtocol.SURVEY_MIN_PLANE_POINTS} duvar köşesi gerekli " +
                               $"(alınan: {corners.Count})");
                        return;
                    }

                    if (Polygon2D.IsSelfIntersecting(ring))
                    {
                        Debug.LogWarning("[VenueSurvey] Zemin halkası kendini kesiyor — köşe sırasını " +
                                         "kontrol edin (kabul edildi).");
                    }

                    shell = guide.BuildFloorAndWalls(ring, VenueSurveyContext.DefaultColumnHeight);
                    mode = SurveyMode.Columns;
                    break;
                }

                default:
                    Reject("son mod — bitirmek için A+B 3 sn");
                    return;
            }

            // No input.Reset() here: the switch already disarmed until release, and re-arming while
            // A is still held would start the undo hold.
            VenueSurveyHaptics.Pulse(this, 2);
            lastEvent = ModeTitle(mode);
            Debug.Log($"[VenueSurvey] {ModeTitle(mode)}");
        }

        // ------------------------------------------------------------------ undo

        private void HandleUndo()
        {
            switch (mode)
            {
                case SurveyMode.Calibration:
                    if (!DropLast(calibration, calibrationMarks))
                    {
                        Reject("geri alınacak nokta yok");
                        return;
                    }

                    Undone($"Kalibrasyon noktası geri alındı ({calibration.Count}/2)");
                    return;

                case SurveyMode.Corners:
                    if (!DropLast(corners, cornerMarks))
                    {
                        Reject("geri alınacak köşe yok");
                        return;
                    }

                    Undone($"Köşe geri alındı (kalan: {corners.Count})");
                    return;

                default:
                    UndoColumnPoint();
                    return;
            }
        }

        /// <summary>Undo inside the column mode. With no half-built column the LAST FINISHED column
        /// is reopened, so a wrong fourth corner is still reachable.</summary>
        private void UndoColumnPoint()
        {
            if (currentColumn.Count == 0)
            {
                if (columns.Count == 0)
                {
                    Reject("geri alınacak kolon noktası yok");
                    return;
                }

                int last = columns.Count - 1;

                if (columnBodies[last] != null)
                {
                    Destroy(columnBodies[last]);
                }

                currentColumn.AddRange(columns[last]);
                currentColumnMarks.AddRange(columnMarks[last]);

                columns.RemoveAt(last);
                columnMarks.RemoveAt(last);
                columnBodies.RemoveAt(last);
            }

            if (!DropLast(currentColumn, currentColumnMarks))
            {
                Reject("geri alınacak kolon noktası yok");
                return;
            }

            Undone($"Kolon noktası geri alındı ({currentColumn.Count}/" +
                   $"{ArenaProtocol.SURVEY_COLUMN_POINTS})");
        }

        // ------------------------------------------------------------------ finish

        private void HandleFinish()
        {
            bool empty = calibration.Count == 0 && corners.Count == 0 &&
                         columns.Count == 0 && currentColumn.Count == 0;
            if (empty)
            {
                Debug.Log("[VenueSurvey] Hiç nokta alınmadı — ölçüm iptal edildi.");
                Finish(false);
                return;
            }

            if (calibration.Count < 2)
            {
                Reject("kalibrasyon eksik — A ve B noktaları alınmalı");
                return;
            }

            if (corners.Count < ArenaProtocol.SURVEY_MIN_PLANE_POINTS)
            {
                Reject($"en az {ArenaProtocol.SURVEY_MIN_PLANE_POINTS} duvar köşesi gerekli " +
                       $"(alınan: {corners.Count})");
                return;
            }

            if (currentColumn.Count > 0)
            {
                Debug.LogWarning($"[VenueSurvey] Yarım kolon atıldı ({currentColumn.Count} nokta).");
                currentColumn.Clear();
            }

            Finish(true);
        }

        private void Finish(bool send)
        {
            finished = true;

            if (send)
            {
                ArenaDimensions plan = VenueSurveyExport.Build(calibration, corners, AsRings(), out string warning);
                if (warning.Length > 0)
                {
                    Debug.LogWarning($"[VenueSurvey] {warning}");
                }

                string localPath = VenueSurveyExport.SaveLocalCopy(plan);

                ArenaClient client = ArenaClient.Instance;
                if (client != null && client.IsConnected)
                {
                    client.Send(VenueSurveyExport.ToMessage(plan));
                    Debug.Log($"[VenueSurvey] Ölçüm sunucuya gönderildi " +
                              $"(köşe {corners.Count}, kolon {columns.Count}).");
                }
                else
                {
                    Debug.LogWarning("[VenueSurvey] Sunucu bağlı değil — yalnız yerel kopya " +
                                     $"yazıldı: {localPath}");
                }

                VenueSurveyHaptics.Pulse(this, 1, VenueSurveyHaptics.Long);
                label?.SetText("MEKAN ÖLÇÜMÜ\nGönderildi — lobiye dönülüyor…");
            }
            else
            {
                label?.SetText("MEKAN ÖLÇÜMÜ\nİptal edildi — lobiye dönülüyor…");
            }

            StartCoroutine(ReturnRoutine());
        }

        private IEnumerator ReturnRoutine()
        {
            yield return new WaitForSecondsRealtime(ReturnDelaySeconds);

            VenueSurveyContext.Reset();

            SceneRouter router = SceneRouter.Instance;
            if (router != null)
            {
                router.ReturnToOpenScene();
            }
            else
            {
                Debug.LogError("[VenueSurvey] SceneRouter yok; ölçüm sahnesinden dönülemedi.");
            }
        }

        // ------------------------------------------------------------------ label

        private void UpdateLabel()
        {
            if (label == null)
            {
                return;
            }

            string counts = mode == SurveyMode.Columns
                ? $"Alınan: {calibration.Count}/2  ·  Köşe: {corners.Count}  ·  Kolon: {columns.Count} " +
                  $"(+{currentColumn.Count}/{ArenaProtocol.SURVEY_COLUMN_POINTS})"
                : $"Alınan: {calibration.Count}/2  ·  Köşe: {corners.Count}  ·  Kolon: {columns.Count}";

            string text =
                $"MEKAN ÖLÇÜMÜ — {ModeTitle(mode)}\n" +
                $"{counts}\n" +
                "B 3 sn: nokta  ·  A basılı + B×3: sonraki mod  ·  A 3 sn: geri al  ·  A+B 3 sn: bitir";

            string progress = ProgressLine();
            if (progress.Length > 0)
            {
                text += "\n" + progress;
            }

            if (lastEvent.Length > 0)
            {
                text += "\n" + lastEvent;
            }

            label.SetText(text);
        }

        private string ProgressLine()
        {
            if (input.EnterExitProgress > 0f)
            {
                return Held("A+B", input.EnterExitProgress);
            }

            if (input.CaptureProgress > 0f)
            {
                return Held("B", input.CaptureProgress);
            }

            if (input.UndoProgress > 0f)
            {
                return Held("A", input.UndoProgress);
            }

            if (input.ModeTapCount > 0)
            {
                return $"B tık {input.ModeTapCount}/{VenueSurveyInput.ModeTaps}";
            }

            return "";
        }

        private static string Held(string button, float progress)
        {
            float seconds = progress * VenueSurveyInput.HoldSeconds;
            return $"{button} basılı {seconds:0.0} / {VenueSurveyInput.HoldSeconds:0.0} sn";
        }

        private static string ModeTitle(SurveyMode value)
        {
            switch (value)
            {
                case SurveyMode.Calibration:
                    return "Mod 1/3: Kalibrasyon noktaları (A → B)";
                case SurveyMode.Corners:
                    return "Mod 2/3: Duvar köşeleri";
                default:
                    return "Mod 3/3: Kolonlar (her kolonda 4 nokta)";
            }
        }

        // ------------------------------------------------------------------ helpers

        private void Accept(string message)
        {
            lastEvent = message;
            VenueSurveyHaptics.Pulse(this, 1);
            Debug.Log($"[VenueSurvey] {message}");
        }

        private void Reject(string reason)
        {
            lastEvent = "Reddedildi: " + reason;
            VenueSurveyHaptics.Pulse(this, 3);
            Debug.LogWarning($"[VenueSurvey] {reason}.");
        }

        private void Undone(string message)
        {
            lastEvent = message;
            VenueSurveyHaptics.Pulse(this, 1, VenueSurveyHaptics.Medium);
            Debug.Log($"[VenueSurvey] {message}");
        }

        private bool DropLast(List<Vector3> points, List<GameObject> marks)
        {
            if (points.Count == 0)
            {
                return false;
            }

            points.RemoveAt(points.Count - 1);

            int last = marks.Count - 1;
            if (last >= 0)
            {
                if (marks[last] != null)
                {
                    Destroy(marks[last]);
                }

                marks.RemoveAt(last);
            }

            return true;
        }

        private void DestroyAll(List<GameObject> marks)
        {
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i] != null)
                {
                    Destroy(marks[i]);
                }
            }

            marks.Clear();
        }

        private IReadOnlyList<IReadOnlyList<Vector3>> AsRings()
        {
            var rings = new List<IReadOnlyList<Vector3>>(columns.Count);
            for (int i = 0; i < columns.Count; i++)
            {
                rings.Add(columns[i]);
            }

            return rings;
        }

        private static Vector2[] ToRing(List<Vector3> points)
        {
            var ring = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                ring[i] = new Vector2(points[i].x, points[i].z);
            }

            return ring;
        }

        private static Vector3 Flat(Vector3 delta)
        {
            return new Vector3(delta.x, 0f, delta.z);
        }
    }
}
