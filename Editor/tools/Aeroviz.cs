using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AeroTools.Editor
{
    internal enum AeroMetric { Mass, Lift, Drag, Fuel }

    public sealed class AerovizWindow : EditorWindow
    {
        private const float BarThickness = 5f;
        private const float TipSizeFactor = 0.10f;
        private const float DragTipMinScale = 0.6f;
        private const float DragTipMaxScale = 2f;
        private const float MaxCombinedShare = 0.8f;

        private static readonly Color MassColor = new Color(0.75f, 0.62f, 0.10f, 1f);
        private static readonly Color LiftColor = Color.cyan;
        private static readonly Color DragColor = Color.red;
        private static readonly Color FuelColor = Color.green;

        [SerializeField] private bool _enabled;
        [SerializeField] private bool _showMassBars = true;
        [SerializeField] private bool _showFuelBars = true;
        [SerializeField] private bool _showLiftBars = true;
        [SerializeField] private bool _showDragBars = true;
        [SerializeField] private float _symmetryTolerance = 0.2f;

        private AeroScan _scan;
        private int _scanRootId;
        private float _scanTolerance;
        private bool _undoRecorded;

        [MenuItem("Blueprinter/Tools/Aeroviz")]
        public static void ShowWindow()
        {
            GetWindow<AerovizWindow>("Aeroviz").Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Aeroviz");
            minSize = new Vector2(420, 320);
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            Undo.postprocessModifications += OnPostprocessModifications;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.postprocessModifications -= OnPostprocessModifications;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _enabled = EditorGUILayout.ToggleLeft("Enable Aeroviz", _enabled);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            if (!_enabled) return;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
            {
                EditorGUILayout.HelpBox("Open a prefab in Prefab Mode to use Aeroviz.", MessageType.Info);
                return;
            }

            if (AeroAccess.AeroPartType == null)
            {
                EditorGUILayout.HelpBox("Type \"AeroPart\" not found.", MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            _symmetryTolerance = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Mirror tolerance", _symmetryTolerance));
            if (EditorGUI.EndChangeCheck())
            {
                _scan = null;
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(6);

            var scan = GetScan(stage);
            if (scan == null || scan.Parts.Count == 0)
            {
                EditorGUILayout.HelpBox("No AeroPart components found (or required fields missing).", MessageType.Info);
                return;
            }

            bool changed = false;
            using (new EditorGUILayout.VerticalScope("box"))
            {
                changed |= MetricBlock(stage, scan, ref _showMassBars, AeroMetric.Mass, "Mass (dry)", scan.TotalMass, scan.CenterOfMass);
                changed |= MetricBlock(stage, scan, ref _showFuelBars, AeroMetric.Fuel, "Fuel", scan.TotalFuel, scan.CenterOfWetMass,
                    $"Wet mass: {scan.TotalWetMass:0.###}", $"Wet center: {FormatVector(scan.CenterOfWetMass)}");
                changed |= MetricBlock(stage, scan, ref _showLiftBars, AeroMetric.Lift, "Lift", scan.TotalLift, scan.CenterOfLift);
                changed |= MetricBlock(stage, scan, ref _showDragBars, AeroMetric.Drag, "Drag", scan.TotalDrag, scan.CenterOfDrag);

                if (changed)
                {
                    _scan = null;
                    scan = GetScan(stage);
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Derived", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Mass / Lift: {FormatRatio(scan.TotalMass, scan.TotalLift)}");
                EditorGUILayout.LabelField($"Lift / Drag: {FormatRatio(scan.TotalLift, scan.TotalDrag)}");
                EditorGUILayout.LabelField($"Wet / Dry: {FormatRatio(scan.TotalWetMass, scan.TotalMass)}");
                Transform root = stage.prefabContentsRoot.transform;
                EditorGUILayout.LabelField($"Lift - Mass: {FormatOffset(root, scan.CenterOfMass, scan.CenterOfLift)}");
                EditorGUILayout.LabelField($"Drag - Mass: {FormatOffset(root, scan.CenterOfMass, scan.CenterOfDrag)}");
                EditorGUILayout.LabelField($"Wet - Dry: {FormatOffset(root, scan.CenterOfMass, scan.CenterOfWetMass)}");
            }

            if (changed || GUI.changed) SceneView.RepaintAll();
        }

        private void OnSceneGUI(SceneView view)
        {
            if (!_enabled || view == null || !view.drawGizmos) return;
            if (Event.current.type == EventType.MouseMove)
            {
                view.Repaint();
                return;
            }
            if (GUIUtility.hotControl == 0) _undoRecorded = false;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null) return;

            var scan = GetScan(stage);
            if (scan == null || scan.Parts.Count == 0) return;

            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            bool changed = false;
            try
            {
                int partCount = scan.Parts.Count;
                float massScale = scan.TotalMass > 0f ? partCount / scan.TotalMass : 0f;
                float liftScale = scan.TotalLift > 0f ? partCount / scan.TotalLift : 0f;
                float dragScale = scan.TotalDrag > 0f ? partCount / scan.TotalDrag : 0f;
                float fuelScale = scan.TotalFuel > 0f && scan.FuelTankCount > 0 ? scan.FuelTankCount / scan.TotalFuel : 0f;

                for (int i = 0; i < scan.Parts.Count; i++)
                {
                    var part = scan.Parts[i];
                    if (part.Part == null) continue;

                    if (_showMassBars)
                        changed |= DrawBar(stage, view.camera, scan, part, AeroMetric.Mass, part.MassPosition, scan.Up,
                            MassColor, part.Mass, scan.TotalMass, partCount, massScale);

                    Vector3 liftDirection = part.LiftNormal != null ? part.LiftNormal.up : scan.Up;
                    if (_showLiftBars)
                        changed |= DrawBar(stage, view.camera, scan, part, AeroMetric.Lift, part.LiftPosition, liftDirection,
                            LiftColor, part.Lift, scan.TotalLift, partCount, liftScale);

                    if (_showDragBars)
                        changed |= DrawBar(stage, view.camera, scan, part, AeroMetric.Drag, part.LiftPosition, liftDirection,
                            DragColor, part.Drag, scan.TotalDrag, partCount, dragScale);

                    if (_showFuelBars && part.Tank != null && part.Fuel > 0f)
                        changed |= DrawBar(stage, view.camera, scan, part, AeroMetric.Fuel, part.MassPosition, scan.Up,
                            FuelColor, part.Fuel, scan.TotalFuel, scan.FuelTankCount, fuelScale);
                }

                if (_showMassBars && scan.TotalMass > 0f) DrawCentroid(scan.CenterOfMass, scan.Up, MassColor);
                if (_showLiftBars && scan.LiftWeight > 0f) DrawCentroid(scan.CenterOfLift, scan.Up, LiftColor);
                if (_showDragBars && scan.TotalDrag > 0f) DrawCentroid(scan.CenterOfDrag, scan.Up, DragColor);
                if (_showFuelBars && scan.TotalWetMass > 0f) DrawCentroid(scan.CenterOfWetMass, scan.Up, FuelColor);
            }
            finally
            {
                Handles.zTest = previousZTest;
            }

            if (changed)
            {
                _scan = null;
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private AeroScan GetScan(PrefabStage stage)
        {
            if (stage == null || stage.prefabContentsRoot == null) return null;

            int rootId = stage.prefabContentsRoot.GetInstanceID();
            float tolerance = Mathf.Max(0.0001f, _symmetryTolerance);
            if (_scan == null || _scanRootId != rootId || !Mathf.Approximately(_scanTolerance, tolerance))
            {
                _scan = AeroScan.Build(stage.prefabContentsRoot, tolerance);
                _scanRootId = rootId;
                _scanTolerance = tolerance;
            }
            return _scan;
        }

        private void OnUndoRedo()
        {
            _scan = null;
            _undoRecorded = false;
            Repaint();
            SceneView.RepaintAll();
        }

        private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            _scan = null;
            return modifications;
        }

        private void OnHierarchyChanged()
        {
            _scan = null;
            if (!_enabled) return;
            Repaint();
            SceneView.RepaintAll();
        }

        private bool MetricBlock(PrefabStage stage, AeroScan scan, ref bool show, AeroMetric metric, string title,
            float total, Vector3 center, string extraLine1 = null, string extraLine2 = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                show = EditorGUILayout.Toggle(show, GUILayout.Width(16));
                GUILayout.Label(title, EditorStyles.boldLabel);
            }

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.DelayedFloatField("Total", total);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed) ScaleTotal(stage, scan, metric, Mathf.Max(0f, value));

            if (extraLine1 != null) EditorGUILayout.LabelField(extraLine1);
            if (extraLine2 != null) EditorGUILayout.LabelField(extraLine2);
            if (extraLine1 == null && extraLine2 == null) EditorGUILayout.LabelField($"Center: {FormatVector(center)}");

            EditorGUILayout.Space(2);
            return changed;
        }

        private static void ScaleTotal(PrefabStage stage, AeroScan scan, AeroMetric metric, float targetTotal)
        {
            var targets = new List<Component>(scan.Parts.Count);
            var values = new List<float>(scan.Parts.Count);
            float currentTotal = 0f;

            for (int i = 0; i < scan.Parts.Count; i++)
            {
                var part = scan.Parts[i];
                var target = TargetComponent(part, metric);
                if (target == null) continue;

                float value = MetricValue(part, metric);
                targets.Add(target);
                values.Add(value);
                currentTotal += value;
            }

            if (targets.Count == 0) return;

            Undo.RecordObjects(targets.ToArray(), "Set Total " + MetricLabel(metric));
            float equalValue = targetTotal / targets.Count;
            float factor = currentTotal > 0f ? targetTotal / currentTotal : 0f;

            for (int i = 0; i < targets.Count; i++)
            {
                float value = currentTotal > 0f ? values[i] * factor : equalValue;
                AeroAccess.WriteMetric(targets[i], metric, value);
                EditorUtility.SetDirty(targets[i]);
            }

            EditorSceneManager.MarkSceneDirty(stage.scene);
        }

        private bool DrawBar(PrefabStage stage, Camera camera, AeroScan scan, AeroPartData part, AeroMetric metric,
            Vector3 basePosition, Vector3 direction, Color color, float oldValue, float total, int count, float scale)
        {
            if (count <= 0 || scale <= 0f) return false;

            oldValue = Mathf.Max(0f, oldValue);
            total = Mathf.Max(0f, total);
            float barHeight = count;
            Vector3 tip = basePosition + direction * (oldValue * scale);

            var previousColor = Handles.color;
            Handles.color = new Color(color.r, color.g, color.b, 0.85f);
            Handles.DrawAAPolyLine(BarThickness, basePosition, tip);

            float size = HandleUtility.GetHandleSize(tip) * TipSizeFactor;
            if (metric == AeroMetric.Drag || metric == AeroMetric.Fuel) size *= DragTipScale(oldValue, total, count);

            int controlId = GUIUtility.GetControlID(part.Part.GetInstanceID() ^ (((int)metric + 1) * 0x1f123bb5), FocusType.Passive);
            EditorGUI.BeginChangeCheck();
            Vector3 newTip = Handles.Slider(controlId, tip, direction, size, Handles.SphereHandleCap, 0f);
            bool changed = EditorGUI.EndChangeCheck();
            bool hot = GUIUtility.hotControl == controlId;
            bool hover = HandleUtility.nearestControl == controlId;

            Component target = TargetComponent(part, metric);
            AeroPartData mirror = part.MirrorIndex >= 0 ? scan.Parts[part.MirrorIndex] : default;
            Component mirrorTarget = part.MirrorIndex >= 0 ? TargetComponent(mirror, metric) : null;
            bool mirrored = mirrorTarget != null && mirrorTarget != target;
            float mirrorValue = mirrored ? MetricValue(mirror, metric) : 0f;

            if (hot && !_undoRecorded && target != null)
            {
                if (mirrored) Undo.RecordObjects(new[] { target, mirrorTarget }, "Drag " + MetricLabel(metric));
                else Undo.RecordObject(target, "Drag " + MetricLabel(metric));
                _undoRecorded = true;
            }

            float height = Mathf.Max(0f, Vector3.Dot(newTip - basePosition, direction));
            float newValue = SolveFromHeight(height, total, oldValue, mirrorValue, barHeight, mirrored);

            if (hover || hot)
            {
                float shownValue = hot ? newValue : oldValue;
                float shownTotal = hot ? UpdatedTotal(total, oldValue, mirrorValue, newValue, mirrored) : total;
                float percentage = shownTotal > 0f ? shownValue / shownTotal * 100f : 0f;
                Vector3 labelOffset = camera != null
                    ? camera.transform.right * (HandleUtility.GetHandleSize(newTip) * 0.06f)
                    : Vector3.right * 0.05f;
                Handles.Label(newTip + labelOffset, $"{MetricLabel(metric)} {shownValue:0.###} ({percentage:0.##}%)");
            }

            if (changed) ApplyValue(stage, target, mirrorTarget, metric, newValue);

            Handles.color = previousColor;
            return changed;
        }

        private static void ApplyValue(PrefabStage stage, Component target, Component mirrorTarget, AeroMetric metric, float value)
        {
            if (target != null)
            {
                AeroAccess.WriteMetric(target, metric, value);
                EditorUtility.SetDirty(target);
            }

            if (mirrorTarget != null && mirrorTarget != target)
            {
                AeroAccess.WriteMetric(mirrorTarget, metric, value);
                EditorUtility.SetDirty(mirrorTarget);
            }

            EditorSceneManager.MarkSceneDirty(stage.scene);
        }

        private static float SolveFromHeight(float height, float total, float oldValue, float mirrorValue, float barHeight, bool mirrored)
        {
            int changedCount = mirrored ? 2 : 1;
            float fixedTotal = Mathf.Max(0f, total - oldValue - (mirrored ? mirrorValue : 0f));
            if (fixedTotal <= 1e-6f) return oldValue;

            float maxHeight = barHeight * (MaxCombinedShare / changedCount);
            height = Mathf.Clamp(height, 0f, maxHeight);
            float denominator = barHeight - changedCount * height;
            if (denominator <= 1e-6f) return oldValue;

            float value = height * fixedTotal / denominator;
            return float.IsNaN(value) || float.IsInfinity(value) ? oldValue : value;
        }

        private static float UpdatedTotal(float total, float oldValue, float mirrorValue, float newValue, bool mirrored)
        {
            return Mathf.Max(0f, total - oldValue - (mirrored ? mirrorValue : 0f) + newValue * (mirrored ? 2f : 1f));
        }

        private static Component TargetComponent(AeroPartData part, AeroMetric metric)
        {
            return metric == AeroMetric.Fuel ? part.Tank : part.Part;
        }

        private static float MetricValue(AeroPartData part, AeroMetric metric)
        {
            switch (metric)
            {
                case AeroMetric.Mass: return Mathf.Max(0f, part.Mass);
                case AeroMetric.Lift: return Mathf.Max(0f, part.Lift);
                case AeroMetric.Drag: return Mathf.Max(0f, part.Drag);
                default: return Mathf.Max(0f, part.Fuel);
            }
        }

        private static string MetricLabel(AeroMetric metric)
        {
            switch (metric)
            {
                case AeroMetric.Mass: return "Mass";
                case AeroMetric.Lift: return "Lift";
                case AeroMetric.Drag: return "Drag";
                default: return "Fuel";
            }
        }

        private static float DragTipScale(float value, float total, int count)
        {
            if (total <= 0f || count <= 0) return 1f;
            float normalized = Mathf.Sqrt(Mathf.Clamp01(value / total * count));
            return Mathf.Lerp(DragTipMinScale, DragTipMaxScale, normalized);
        }

        private static void DrawCentroid(Vector3 position, Vector3 up, Color color)
        {
            var previousColor = Handles.color;
            Handles.color = new Color(color.r, color.g, color.b, 0.95f);
            float size = HandleUtility.GetHandleSize(position) * 0.10f;
            Handles.DrawWireDisc(position, up, size);
            Handles.SphereHandleCap(0, position, Quaternion.identity, size * 0.35f, EventType.Repaint);
            Handles.color = previousColor;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
        }

        private static string FormatRatio(float numerator, float denominator)
        {
            return denominator <= 0f ? "n/a" : (numerator / denominator).ToString("0.###");
        }

        private static string FormatOffset(Transform root, Vector3 from, Vector3 to)
        {
            return FormatVector(root.InverseTransformVector(to - from));
        }
    }

    internal sealed class AeroScan
    {
        public readonly List<AeroPartData> Parts = new List<AeroPartData>(128);
        public Vector3 Up;
        public int FuelTankCount;
        public float TotalMass, TotalLift, TotalDrag, TotalFuel, TotalWetMass;
        public float LiftWeight;
        public Vector3 CenterOfMass, CenterOfLift, CenterOfDrag, CenterOfWetMass;

        public static AeroScan Build(GameObject rootObject, float tolerance)
        {
            if (rootObject == null || AeroAccess.AeroPartType == null) return null;

            var components = rootObject.GetComponentsInChildren(AeroAccess.AeroPartType, true);
            if (components == null || components.Length == 0) return null;

            var root = rootObject.transform;
            var scan = new AeroScan { Up = root.up };
            var localPositions = new List<Vector3>(components.Length);
            var depths = new List<int>(components.Length);
            Vector3 massMoment = Vector3.zero;
            Vector3 liftMoment = Vector3.zero;
            Vector3 dragMoment = Vector3.zero;
            Vector3 wetMoment = Vector3.zero;

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i] as Component;
                if (!AeroAccess.TryReadPart(component, out var part)) continue;

                part.MirrorIndex = -1;
                scan.Parts.Add(part);
                localPositions.Add(root.InverseTransformPoint(component.transform.position));
                depths.Add(DepthFromRoot(root, component.transform));

                scan.TotalMass += part.Mass;
                massMoment += part.MassPosition * part.Mass;

                float horizontalWeight = HorizontalWeight(part.LiftNormal, component.transform, scan.Up);
                float liftWeight = part.Lift * horizontalWeight;
                scan.TotalLift += part.Lift;
                scan.LiftWeight += liftWeight;
                liftMoment += part.LiftPosition * liftWeight;
                scan.TotalDrag += part.Drag;
                dragMoment += part.LiftPosition * part.Drag;

                scan.TotalFuel += part.Fuel;
                if (part.Tank != null && part.Fuel > 0f) scan.FuelTankCount++;

                float wetMass = part.Mass + part.Fuel;
                scan.TotalWetMass += wetMass;
                wetMoment += part.MassPosition * wetMass;
            }

            float toleranceSq = tolerance * tolerance;
            for (int i = 0; i < scan.Parts.Count; i++)
            {
                Vector3 position = localPositions[i];
                if (Mathf.Abs(position.x) < tolerance) continue;

                Vector3 mirroredPosition = new Vector3(-position.x, position.y, position.z);
                int mirrorIndex = -1;
                float bestDistance = toleranceSq;
                for (int j = 0; j < scan.Parts.Count; j++)
                {
                    if (i == j || depths[i] != depths[j]) continue;
                    float distance = (localPositions[j] - mirroredPosition).sqrMagnitude;
                    if (distance > bestDistance) continue;
                    bestDistance = distance;
                    mirrorIndex = j;
                }

                if (mirrorIndex < 0) continue;
                var part = scan.Parts[i];
                part.MirrorIndex = mirrorIndex;
                scan.Parts[i] = part;
            }

            scan.CenterOfMass = scan.TotalMass > 0f ? massMoment / scan.TotalMass : Vector3.zero;
            scan.CenterOfLift = scan.LiftWeight > 0f ? liftMoment / scan.LiftWeight : Vector3.zero;
            scan.CenterOfDrag = scan.TotalDrag > 0f ? dragMoment / scan.TotalDrag : Vector3.zero;
            scan.CenterOfWetMass = scan.TotalWetMass > 0f ? wetMoment / scan.TotalWetMass : Vector3.zero;
            return scan;
        }

        private static int DepthFromRoot(Transform root, Transform transform)
        {
            int depth = 0;
            while (transform != null && transform != root)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        private static float HorizontalWeight(Transform liftNormal, Transform part, Vector3 up)
        {
            Vector3 normal = liftNormal != null ? liftNormal.up : part.up;
            float alignment = Mathf.Abs(Vector3.Dot(normal.normalized, up.normalized));
            return alignment * alignment;
        }
    }

    internal struct AeroPartData
    {
        public Component Part;
        public Component Tank;
        public float Mass, Lift, Drag, Fuel;
        public Vector3 MassPosition, LiftPosition;
        public Transform LiftNormal;
        public int MirrorIndex;
    }

    internal static class AeroAccess
    {
        public const float MinAeroValue = 1e-5f;

        private const string MassField = "mass";
        private const string DragField = "dragArea";
        private const string LiftField = "wingArea";
        private const string CenterOfMassField = "centerOfMass";
        private const string LiftNormalField = "liftNormal";
        private const string CenterOfLiftField = "centerOfLift";
        private const string FuelField = "fuelCapacity";

        public static readonly Type AeroPartType;
        public static readonly Type FuelTankType;

        static AeroAccess()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (type == null) continue;
                if (type.Name == "AeroPart") AeroPartType = type;
                else if (type.Name == "FuelTank") FuelTankType = type;
                if (AeroPartType != null && FuelTankType != null) break;
            }
        }

        public static bool TryReadPart(Component component, out AeroPartData part)
        {
            part = default;
            if (component == null) return false;

            var serialized = new SerializedObject(component);
            serialized.Update();
            if (!TryReadFloat(serialized, MassField, out float mass) ||
                !TryReadFloat(serialized, LiftField, out float lift) ||
                !TryReadFloat(serialized, DragField, out float drag))
                return false;

            Vector3 objectPosition = component.transform.position;
            Transform centerOfMass = ReadObject<Transform>(serialized, CenterOfMassField);
            Transform liftNormal = ReadObject<Transform>(serialized, LiftNormalField);
            bool hasCenterOfLift = TryReadVector3(serialized, CenterOfLiftField, out var centerOfLift) && centerOfLift != Vector3.zero;

            part.Part = component;
            part.Mass = Mathf.Max(MinAeroValue, mass);
            part.Lift = Mathf.Max(MinAeroValue, lift);
            part.Drag = Mathf.Max(MinAeroValue, drag);
            part.MassPosition = centerOfMass != null ? centerOfMass.position : objectPosition;
            part.LiftNormal = liftNormal;
            part.LiftPosition = liftNormal != null
                ? (hasCenterOfLift ? liftNormal.TransformPoint(centerOfLift) : liftNormal.position)
                : (hasCenterOfLift ? component.transform.TransformPoint(centerOfLift) : objectPosition);

            if (FuelTankType != null)
            {
                part.Tank = component.GetComponent(FuelTankType);
                if (part.Tank != null)
                {
                    var fuelObject = new SerializedObject(part.Tank);
                    fuelObject.Update();
                    if (TryReadFloat(fuelObject, FuelField, out float fuel)) part.Fuel = Mathf.Max(0f, fuel);
                    else part.Tank = null;
                }
            }

            return true;
        }

        public static void WriteMetric(Component component, AeroMetric metric, float value)
        {
            if (component == null) return;

            string field;
            if (metric == AeroMetric.Mass) field = MassField;
            else if (metric == AeroMetric.Lift) field = LiftField;
            else if (metric == AeroMetric.Drag) field = DragField;
            else field = FuelField;

            var serialized = new SerializedObject(component);
            serialized.Update();
            var property = serialized.FindProperty(field);
            if (property == null || property.propertyType != SerializedPropertyType.Float) return;

            property.floatValue = metric == AeroMetric.Fuel ? Mathf.Max(0f, value) : Mathf.Max(MinAeroValue, value);
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        private static bool TryReadFloat(SerializedObject serialized, string field, out float value)
        {
            var property = serialized.FindProperty(field);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
            {
                value = 0f;
                return false;
            }

            value = property.floatValue;
            return true;
        }

        private static T ReadObject<T>(SerializedObject serialized, string field) where T : UnityEngine.Object
        {
            var property = serialized.FindProperty(field);
            return property != null && property.propertyType == SerializedPropertyType.ObjectReference
                ? property.objectReferenceValue as T
                : null;
        }

        private static bool TryReadVector3(SerializedObject serialized, string field, out Vector3 value)
        {
            var property = serialized.FindProperty(field);
            if (property == null || property.propertyType != SerializedPropertyType.Vector3)
            {
                value = Vector3.zero;
                return false;
            }

            value = property.vector3Value;
            return true;
        }
    }
}
