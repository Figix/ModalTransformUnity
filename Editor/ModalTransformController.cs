// Copyright (c) 2026 Figix. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Figix.ModalTransformUnity
{
    /// <summary>
    /// Keyboard-first modal transforms for the Unity Scene view.
    /// The implementation intentionally uses Unity 6000.0 editor APIs only.
    /// </summary>
    [InitializeOnLoad]
    internal static class ModalTransformController
    {
        private enum TransformMode
        {
            Translate,
            Rotate,
            Scale
        }

        private enum Constraint
        {
            None,
            X,
            Y,
            Z,
            PlaneX,
            PlaneY,
            PlaneZ
        }

        private sealed class TransformSnapshot
        {
            internal readonly Transform Target;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal readonly Vector3 LocalScale;

            internal TransformSnapshot(Transform target)
            {
                Target = target;
                Position = target.position;
                Rotation = target.rotation;
                LocalScale = target.localScale;
            }

            internal void Restore()
            {
                if (Target == null)
                {
                    return;
                }

                Target.position = Position;
                Target.rotation = Rotation;
                Target.localScale = LocalScale;
            }
        }

        private static readonly List<TransformSnapshot> Snapshots = new List<TransformSnapshot>();
        private static readonly HashSet<KeyCode> HeldModalKeys = new HashSet<KeyCode>();

        private static bool s_IsActive;
        private static bool s_RightMouseHeld;
        private static bool s_PreviousToolsHidden;
        private static TransformMode s_Mode;
        private static Constraint s_Constraint;
        private static Vector2 s_StartMouse;
        private static Vector2 s_CurrentMouse;
        private static Vector3 s_Pivot;
        private static bool s_ConstraintLocal;
        private static bool s_UseNativeCursor;
        private static int s_AnchorX;
        private static int s_AnchorY;
        private static int s_HiddenCalls;
        private static Vector3 s_ViewRight;
        private static Vector3 s_ViewUp;
        private static Vector3 s_ViewForward;
        private static Camera s_Camera;
        private static int s_UndoGroup = -1;
        private static string s_NumericInput = string.Empty;

        static ModalTransformController()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CancelForShutdown;
            EditorApplication.quitting += CancelForShutdown;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            TrackRightMouse(current);

            // Consume the matching KeyUp for modal keys. Keeping the key in this
            // set until release prevents OS key-repeat from restarting a mode.
            if (current.type == EventType.KeyUp && HeldModalKeys.Remove(current.keyCode))
            {
                current.Use();
                if (s_IsActive)
                {
                    DrawHud(sceneView);
                }
                return;
            }

            if (!s_IsActive)
            {
                TryStart(sceneView, current);
                return;
            }

            if (!HasValidTargets())
            {
                Cancel();
                return;
            }

            HandleActiveInput(sceneView, current);
            if (s_IsActive && current.type == EventType.Repaint)
            {
                DrawGizmo();
            }
            DrawHud(sceneView);
        }

        private static void TrackRightMouse(Event current)
        {
            if (current.button != 1)
            {
                return;
            }

            if (current.rawType == EventType.MouseDown)
            {
                s_RightMouseHeld = true;
            }
            else if (current.rawType == EventType.MouseUp)
            {
                s_RightMouseHeld = false;
            }
        }

        private static void TryStart(SceneView sceneView, Event current)
        {
            if (current.type != EventType.KeyDown || current.alt || current.control || current.command)
            {
                return;
            }

            // Unity fly-camera navigation owns W/A/S/D/Q/E whenever RMB was held first.
            if (s_RightMouseHeld)
            {
                return;
            }

            TransformMode mode;
            KeyCode key = current.keyCode;
            if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.Translate))
            {
                mode = TransformMode.Translate;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.Rotate))
            {
                mode = TransformMode.Rotate;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.Scale))
            {
                mode = TransformMode.Scale;
            }
            else
            {
                return;
            }

            Transform[] targets = Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable);
            if (targets == null || targets.Length == 0 || sceneView.camera == null)
            {
                return;
            }

            HeldModalKeys.Add(current.keyCode);
            Begin(mode, targets, sceneView, current.mousePosition);
            current.Use();
        }

        private static void Begin(TransformMode mode, Transform[] targets, SceneView sceneView, Vector2 mousePosition)
        {
            Snapshots.Clear();
            foreach (Transform target in targets)
            {
                if (target != null)
                {
                    Snapshots.Add(new TransformSnapshot(target));
                }
            }

            if (Snapshots.Count == 0)
            {
                return;
            }

            s_Mode = mode;
            s_Constraint = Constraint.None;
            s_StartMouse = mousePosition;
            s_CurrentMouse = mousePosition;
            s_NumericInput = string.Empty;
            s_Camera = sceneView.camera;
            s_ViewRight = s_Camera.transform.right;
            s_ViewUp = s_Camera.transform.up;
            s_ViewForward = s_Camera.transform.forward;
            s_Pivot = CalculatePivot();
            s_ConstraintLocal = false;
            BeginCursorCapture();

            Undo.IncrementCurrentGroup();
            s_UndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Modal " + mode);
            UnityEngine.Object[] undoTargets = new UnityEngine.Object[Snapshots.Count];
            for (int index = 0; index < Snapshots.Count; index++)
            {
                undoTargets[index] = Snapshots[index].Target;
            }
            Undo.RecordObjects(undoTargets, "Modal " + mode);

            s_PreviousToolsHidden = Tools.hidden;
            Tools.hidden = true;
            s_IsActive = true;
            SceneView.RepaintAll();
        }

        private static void HandleActiveInput(SceneView sceneView, Event current)
        {
            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
            {
                if (!s_UseNativeCursor)
                {
                    // Mouse jumping wraps the cursor at screen edges; delta stays continuous.
                    s_CurrentMouse += current.delta;
                }
                ApplyCurrentTransform();
                current.Use();
                sceneView.Repaint();
                return;
            }

            if (current.type == EventType.MouseDown)
            {
                if (current.button == 0)
                {
                    Confirm();
                    current.Use();
                }
                else if (current.button == 1)
                {
                    Cancel();
                    current.Use();
                }
                return;
            }

            if (current.type != EventType.KeyDown)
            {
                return;
            }

            switch (current.keyCode)
            {
                case KeyCode.Escape:
                    Cancel();
                    current.Use();
                    return;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Confirm();
                    current.Use();
                    return;
                case KeyCode.Backspace:
                    if (s_NumericInput.Length > 0)
                    {
                        s_NumericInput = s_NumericInput.Substring(0, s_NumericInput.Length - 1);
                        ApplyCurrentTransform();
                        current.Use();
                    }
                    return;
            }

            if (TryHandleBinding(current))
            {
                return;
            }

            if (TryAppendNumeric(current))
            {
                ApplyCurrentTransform();
                current.Use();
            }
        }

        private static bool TryHandleBinding(Event current)
        {
            KeyCode key = current.keyCode;
            TransformMode? mode = null;
            Constraint axis = Constraint.None;

            if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.Translate))
            {
                mode = TransformMode.Translate;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.Rotate))
            {
                mode = TransformMode.Rotate;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.Scale))
            {
                mode = TransformMode.Scale;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.AxisX))
            {
                axis = current.shift ? Constraint.PlaneX : Constraint.X;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.AxisY))
            {
                axis = current.shift ? Constraint.PlaneY : Constraint.Y;
            }
            else if (key == ModalTransformSettings.Get(ModalTransformSettings.Action.AxisZ))
            {
                axis = current.shift ? Constraint.PlaneZ : Constraint.Z;
            }
            else
            {
                return false;
            }

            current.Use();
            if (!TryAcceptModalKey(current))
            {
                return true;
            }

            if (mode.HasValue)
            {
                SwitchMode(mode.Value);
            }
            else
            {
                SetConstraint(axis);
            }
            return true;
        }

        private static bool TryAcceptModalKey(Event current)
        {
            return HeldModalKeys.Add(current.keyCode);
        }

        private static void SwitchMode(TransformMode mode)
        {
            RestoreSnapshots();
            s_Mode = mode;
            s_Constraint = Constraint.None;
            s_ConstraintLocal = false;
            s_StartMouse = s_CurrentMouse;
            s_NumericInput = string.Empty;
            ApplyCurrentTransform();
        }

        private static void SetConstraint(Constraint constraint)
        {
            // Press 1: world, 2: local, 3: world, ... Switching W/E/R returns to the free state.
            if (s_Constraint != constraint)
            {
                s_Constraint = constraint;
                s_ConstraintLocal = false;
            }
            else
            {
                s_ConstraintLocal = !s_ConstraintLocal;
            }
            ApplyCurrentTransform();
        }

        private static void ApplyCurrentTransform()
        {
            if (!s_IsActive)
            {
                return;
            }

            float numericValue;
            bool hasNumericValue = TryGetNumericValue(out numericValue);
            switch (s_Mode)
            {
                case TransformMode.Translate:
                    ApplyTranslation(hasNumericValue, numericValue);
                    break;
                case TransformMode.Rotate:
                    ApplyRotation(hasNumericValue, numericValue);
                    break;
                case TransformMode.Scale:
                    ApplyScale(hasNumericValue, numericValue);
                    break;
            }
        }

        private static void ApplyTranslation(bool hasNumericValue, float numericValue)
        {
            Vector2 mouseDelta = s_CurrentMouse - s_StartMouse;
            Vector3 delta;

            if (IsAxisConstraint(s_Constraint))
            {
                Vector3 axis = GetConstraintAxis(s_Constraint);
                float amount = hasNumericValue ? numericValue : MouseDeltaAlongAxis(mouseDelta, axis);
                delta = axis * amount;
            }
            else
            {
                delta = ScreenPlaneDelta(mouseDelta);
                if (IsPlaneConstraint(s_Constraint))
                {
                    Vector3 excludedAxis = GetExcludedAxis(s_Constraint);
                    delta -= Vector3.Project(delta, excludedAxis);
                }

                if (hasNumericValue)
                {
                    Vector3 direction = delta.sqrMagnitude > 0.000001f ? delta.normalized : s_ViewRight;
                    delta = direction * numericValue;
                }
            }

            foreach (TransformSnapshot snapshot in Snapshots)
            {
                if (snapshot.Target != null)
                {
                    snapshot.Target.position = snapshot.Position + delta;
                }
            }
        }

        private static void ApplyRotation(bool hasNumericValue, float numericValue)
        {
            Vector2 mouseDelta = s_CurrentMouse - s_StartMouse;
            float angle = hasNumericValue ? numericValue : (mouseDelta.x - mouseDelta.y) * 0.25f;
            Vector3 axis = IsAxisConstraint(s_Constraint)
                ? GetConstraintAxis(s_Constraint)
                : s_ViewForward;
            Quaternion deltaRotation = Quaternion.AngleAxis(angle, axis.normalized);

            foreach (TransformSnapshot snapshot in Snapshots)
            {
                if (snapshot.Target == null)
                {
                    continue;
                }

                snapshot.Target.position = s_Pivot + deltaRotation * (snapshot.Position - s_Pivot);
                snapshot.Target.rotation = deltaRotation * snapshot.Rotation;
            }
        }

        private static void ApplyScale(bool hasNumericValue, float numericValue)
        {
            Vector2 mouseDelta = s_CurrentMouse - s_StartMouse;
            float factor = hasNumericValue
                ? numericValue
                : Mathf.Max(0.001f, 1f + (mouseDelta.x - mouseDelta.y) * 0.01f);

            Vector3 componentFactor = GetScaleComponentFactor(factor);
            foreach (TransformSnapshot snapshot in Snapshots)
            {
                if (snapshot.Target == null)
                {
                    continue;
                }

                snapshot.Target.localScale = Vector3.Scale(snapshot.LocalScale, componentFactor);
                Vector3 pivotOffset = snapshot.Position - s_Pivot;
                if (s_Constraint == Constraint.None)
                {
                    snapshot.Target.position = s_Pivot + pivotOffset * factor;
                }
                else
                {
                    Vector3 localOffset = Quaternion.Inverse(CurrentBasis()) * pivotOffset;
                    localOffset = Vector3.Scale(localOffset, componentFactor);
                    snapshot.Target.position = s_Pivot + CurrentBasis() * localOffset;
                }
            }
        }

        private static Vector3 GetScaleComponentFactor(float factor)
        {
            switch (s_Constraint)
            {
                case Constraint.X:
                    return new Vector3(factor, 1f, 1f);
                case Constraint.Y:
                    return new Vector3(1f, factor, 1f);
                case Constraint.Z:
                    return new Vector3(1f, 1f, factor);
                case Constraint.PlaneX:
                    return new Vector3(1f, factor, factor);
                case Constraint.PlaneY:
                    return new Vector3(factor, 1f, factor);
                case Constraint.PlaneZ:
                    return new Vector3(factor, factor, 1f);
                default:
                    return Vector3.one * factor;
            }
        }

        private static float MouseDeltaAlongAxis(Vector2 mouseDelta, Vector3 axis)
        {
            if (s_Camera == null)
            {
                return 0f;
            }

            Vector3 startScreen = s_Camera.WorldToScreenPoint(s_Pivot);
            Vector3 endScreen = s_Camera.WorldToScreenPoint(s_Pivot + axis.normalized);
            Vector2 guiStart = new Vector2(startScreen.x, s_Camera.pixelHeight - startScreen.y);
            Vector2 guiEnd = new Vector2(endScreen.x, s_Camera.pixelHeight - endScreen.y);
            Vector2 axisOnScreen = guiEnd - guiStart;
            float pixelsPerUnit = axisOnScreen.magnitude;
            if (pixelsPerUnit < 0.01f)
            {
                return 0f;
            }

            return Vector2.Dot(mouseDelta, axisOnScreen / pixelsPerUnit) / pixelsPerUnit;
        }

        private static Vector3 ScreenPlaneDelta(Vector2 mouseDelta)
        {
            if (s_Camera == null)
            {
                return Vector3.zero;
            }

            float unitsPerPixel;
            if (s_Camera.orthographic)
            {
                unitsPerPixel = (s_Camera.orthographicSize * 2f) / Mathf.Max(1f, s_Camera.pixelHeight);
            }
            else
            {
                float distance = Mathf.Max(0.01f, Vector3.Dot(s_Pivot - s_Camera.transform.position, s_ViewForward));
                float worldHeight = 2f * distance * Mathf.Tan(s_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                unitsPerPixel = worldHeight / Mathf.Max(1f, s_Camera.pixelHeight);
            }

            return (s_ViewRight * mouseDelta.x - s_ViewUp * mouseDelta.y) * unitsPerPixel;
        }

        private static Quaternion CurrentBasis()
        {
            return s_ConstraintLocal && Snapshots.Count > 0
                ? Snapshots[0].Rotation
                : Quaternion.identity;
        }

        private static Vector3 GetConstraintAxis(Constraint constraint)
        {
            Vector3 localAxis;
            switch (constraint)
            {
                case Constraint.Y:
                    localAxis = Vector3.up;
                    break;
                case Constraint.Z:
                    localAxis = Vector3.forward;
                    break;
                default:
                    localAxis = Vector3.right;
                    break;
            }
            return CurrentBasis() * localAxis;
        }

        private static Vector3 GetExcludedAxis(Constraint constraint)
        {
            switch (constraint)
            {
                case Constraint.PlaneY:
                    return CurrentBasis() * Vector3.up;
                case Constraint.PlaneZ:
                    return CurrentBasis() * Vector3.forward;
                default:
                    return CurrentBasis() * Vector3.right;
            }
        }

        private static bool IsAxisConstraint(Constraint constraint)
        {
            return constraint == Constraint.X || constraint == Constraint.Y || constraint == Constraint.Z;
        }

        private static bool IsPlaneConstraint(Constraint constraint)
        {
            return constraint == Constraint.PlaneX || constraint == Constraint.PlaneY || constraint == Constraint.PlaneZ;
        }

        private static bool TryAppendNumeric(Event current)
        {
            char character = current.character;
            if (character >= '0' && character <= '9')
            {
                s_NumericInput += character;
                return true;
            }

            if ((character == '.' || character == ',') && !s_NumericInput.Contains("."))
            {
                s_NumericInput += ".";
                return true;
            }

            if (character == '-' && s_NumericInput.Length == 0)
            {
                s_NumericInput = "-";
                return true;
            }

            return false;
        }

        private static bool TryGetNumericValue(out float value)
        {
            return float.TryParse(
                s_NumericInput,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static Vector3 CalculatePivot()
        {
            Vector3 total = Vector3.zero;
            int count = 0;
            foreach (TransformSnapshot snapshot in Snapshots)
            {
                if (snapshot.Target != null)
                {
                    total += snapshot.Position;
                    count++;
                }
            }
            return count > 0 ? total / count : Vector3.zero;
        }

        private static bool HasValidTargets()
        {
            if (Snapshots.Count == 0)
            {
                return false;
            }

            foreach (TransformSnapshot snapshot in Snapshots)
            {
                if (snapshot.Target == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static void Confirm()
        {
            if (!s_IsActive)
            {
                return;
            }

            Undo.FlushUndoRecordObjects();
            if (s_UndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(s_UndoGroup);
            }
            EndSession();
        }

        private static void Cancel()
        {
            if (!s_IsActive)
            {
                return;
            }

            if (s_UndoGroup >= 0)
            {
                Undo.RevertAllDownToGroup(s_UndoGroup);
            }
            RestoreSnapshots();
            EndSession();
        }

        private static void RestoreSnapshots()
        {
            foreach (TransformSnapshot snapshot in Snapshots)
            {
                snapshot.Restore();
            }
        }

        private static void EndSession()
        {
            EndCursorCapture();
            Tools.hidden = s_PreviousToolsHidden;
            s_IsActive = false;
            s_UndoGroup = -1;
            s_NumericInput = string.Empty;
            s_Camera = null;
            Snapshots.Clear();
            SceneView.RepaintAll();
        }

        private static void OnSelectionChanged()
        {
            if (s_IsActive)
            {
                Cancel();
            }
        }

        private static void CancelForShutdown()
        {
            if (s_IsActive)
            {
                Cancel();
            }
        }

        // Infinite mouse movement. On Windows the cursor is re-centred on its start point every
        // tick and the travelled distance is accumulated, so it works outside the Scene view and
        // across monitors. Other platforms fall back to Unity's mouse jumping.
        private static void BeginCursorCapture()
        {
            s_UseNativeCursor = false;
#if UNITY_EDITOR_WIN
            POINT point;
            if (GetCursorPos(out point))
            {
                s_AnchorX = point.X;
                s_AnchorY = point.Y;
                s_UseNativeCursor = true;
                s_HiddenCalls = 0;
                while (ShowCursor(false) >= 0 && s_HiddenCalls < 16)
                {
                    s_HiddenCalls++;
                }
                s_HiddenCalls++;
                return;
            }
#endif
            EditorGUIUtility.SetWantsMouseJumping(1);
        }

        private static void EndCursorCapture()
        {
            if (!s_UseNativeCursor)
            {
                EditorGUIUtility.SetWantsMouseJumping(0);
            }
#if UNITY_EDITOR_WIN
            for (; s_HiddenCalls > 0; s_HiddenCalls--)
            {
                ShowCursor(true);
            }
#endif
            s_UseNativeCursor = false;
        }

        private static void OnEditorUpdate()
        {
#if UNITY_EDITOR_WIN
            if (!s_IsActive || !s_UseNativeCursor || !InternalEditorUtility.isApplicationActive)
            {
                return;
            }

            POINT point;
            if (!GetCursorPos(out point))
            {
                return;
            }

            int dx = point.X - s_AnchorX;
            int dy = point.Y - s_AnchorY;
            if (dx == 0 && dy == 0)
            {
                return;
            }

            SetCursorPos(s_AnchorX, s_AnchorY);
            s_CurrentMouse += new Vector2(dx, dy) / EditorGUIUtility.pixelsPerPoint;
            ApplyCurrentTransform();
            SceneView.RepaintAll();
#endif
        }

#if UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool show);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
#endif

        private static void DrawGizmo()
        {
            float size = HandleUtility.GetHandleSize(s_Pivot);
            Quaternion basis = CurrentBasis();
            bool axisMode = IsAxisConstraint(s_Constraint);
            bool planeMode = IsPlaneConstraint(s_Constraint);

            for (int axis = 0; axis < 3; axis++)
            {
                bool visible = s_Constraint == Constraint.None
                    || (axisMode && (int)s_Constraint - (int)Constraint.X == axis)
                    || (planeMode && (int)s_Constraint - (int)Constraint.PlaneX != axis);
                if (!visible)
                {
                    continue;
                }

                Vector3 direction = basis * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                Color color = axis == 0 ? Handles.xAxisColor : axis == 1 ? Handles.yAxisColor : Handles.zAxisColor;
                using (new Handles.DrawingScope(color))
                {
                    switch (s_Mode)
                    {
                        case TransformMode.Translate:
                            Handles.ArrowHandleCap(0, s_Pivot, Quaternion.LookRotation(direction), size, EventType.Repaint);
                            break;
                        case TransformMode.Rotate:
                            Handles.DrawWireDisc(s_Pivot, direction, size * 0.9f);
                            break;
                        default:
                            Handles.DrawLine(s_Pivot, s_Pivot + direction * size);
                            Handles.CubeHandleCap(0, s_Pivot + direction * size, Quaternion.identity, size * 0.12f, EventType.Repaint);
                            break;
                    }
                }
            }

            if (s_Mode == TransformMode.Rotate && s_Constraint == Constraint.None)
            {
                using (new Handles.DrawingScope(Color.white))
                {
                    Handles.DrawWireDisc(s_Pivot, s_ViewForward, size);
                }
            }
        }

        private static Color s_HudColor = Color.white;

        // Complement of the pixels behind the HUD, falling back to black/white when the
        // complement would be too close in brightness to read.
        private static Color SampleHudColor(Rect rect)
        {
            try
            {
                Vector2 screen = GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.y - 6f));
                Color[] pixels = InternalEditorUtility.ReadScreenPixel(
                    screen * EditorGUIUtility.pixelsPerPoint, 32, 2);
                if (pixels == null || pixels.Length == 0)
                {
                    return s_HudColor;
                }

                Color average = Color.black;
                foreach (Color pixel in pixels)
                {
                    average += pixel;
                }
                average /= pixels.Length;
                average.a = 1f;

                Color inverse = new Color(1f - average.r, 1f - average.g, 1f - average.b, 1f);
                float background = average.grayscale;
                if (Mathf.Abs(inverse.grayscale - background) < 0.4f)
                {
                    inverse = background > 0.5f ? Color.black : Color.white;
                }
                return inverse;
            }
            catch (Exception)
            {
                return s_HudColor;
            }
        }

        private static void DrawHud(SceneView sceneView)
        {
            if (!s_IsActive || Event.current.type != EventType.Repaint)
            {
                return;
            }

            string constraint = s_Constraint == Constraint.None
                ? "Free"
                : s_Constraint + (s_ConstraintLocal ? " (Local)" : " (World)");
            string numeric = string.IsNullOrEmpty(s_NumericInput) ? "Mouse" : s_NumericInput;
            string text = string.Format("{0} | {1} | {2}\nLMB/Enter confirm · RMB/Esc cancel", s_Mode, constraint, numeric);
            Rect rect = new Rect(12f, sceneView.position.height - 58f, 320f, 38f);

            s_HudColor = Color.Lerp(s_HudColor, SampleHudColor(rect), 0.5f);
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            Color shadow = s_HudColor.grayscale > 0.5f ? Color.black : Color.white;

            Handles.BeginGUI();
            style.normal.textColor = new Color(shadow.r, shadow.g, shadow.b, 0.6f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            style.normal.textColor = s_HudColor;
            GUI.Label(rect, text, style);
            Handles.EndGUI();
        }
    }
}
