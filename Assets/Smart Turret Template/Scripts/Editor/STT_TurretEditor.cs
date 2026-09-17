using System.Reflection;
using UnityEditor;
using UnityEngine;
using STT;

namespace STT.Editor {

[CustomEditor(typeof(STT_Turret))]
public class STT_TurretEditor : UnityEditor.Editor {

    private bool showReferences = true;
    private bool showParameters = true;
    private bool showTargeting = true;
    private bool showEffects = true;
    private bool showPhysics;

    private bool showParametersStatus = true;
    private bool showParametersShooting = true;

    private bool showTargetingAiming = true;
    private bool showTargetingTags = true;

    private bool showEffectsVFX = true;
    private bool showEffectsSFX = true;

    private bool showPhysicsCollider;

    private static readonly Color headerColor     = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color accentGray       = Color.gray;
    private static readonly Color accentParameters = new Color(0.85f, 0.3f,  0.3f,  1f);
    private static readonly Color accentTargeting  = new Color(0.3f,  0.65f, 0.85f, 1f);
    private static readonly Color accentEffects    = new Color(0.3f,  0.8f,  0.45f, 1f);
    private static readonly Color accentPhysics    = new Color(0.85f, 0.6f,  0.2f,  1f);
    private static readonly Color accentRuntime    = new Color(0.9f,  0.75f, 0.2f,  1f);

    private static readonly Color toggleOffColor = new Color(0.22f, 0.22f, 0.22f, 1f);

    private static GUIStyle _panelStyle;
    private static GUIStyle PanelStyle {
        get {
            if (_panelStyle == null) {
                _panelStyle = new GUIStyle(EditorStyles.helpBox);
                _panelStyle.padding = new RectOffset(8, 4, 4, 4);
                _panelStyle.margin  = new RectOffset(0, 0, 0, 0);
            }
            return _panelStyle;
        }
    }

    private static GUIStyle _subPanelStyle;
    private static GUIStyle SubPanelStyle {
        get {
            if (_subPanelStyle == null) {
                _subPanelStyle = new GUIStyle();
                _subPanelStyle.padding = new RectOffset(8, 4, 4, 4);
                _subPanelStyle.margin  = new RectOffset(0, 0, 0, 0);
                Texture2D bg = new Texture2D(1, 1);
                bg.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f, 1f));
                bg.Apply();
                _subPanelStyle.normal.background = bg;
            }
            return _subPanelStyle;
        }
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        DrawReferences();
        EditorGUILayout.Space(2);
        DrawParameters();
        EditorGUILayout.Space(2);
        DrawTargeting();
        EditorGUILayout.Space(2);
        DrawEffects();
        EditorGUILayout.Space(2);
        DrawPhysics();

        if (Application.isPlaying) {
            EditorGUILayout.Space(2);
            DrawRuntime();
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ─── Sections ───

    private void DrawReferences() {
        showReferences = DrawHeader("REFERENCES", showReferences, accentGray);
        if (!showReferences) return;
        BeginPanel();
        var vfx = serializedObject.FindProperty("VFX");
        EditorGUILayout.PropertyField(vfx.FindPropertyRelative("muzzle"),
            new GUIContent("Muzzle", "Transform at the barrel tip — raycast origin and shot FX spawn point"));
        EndPanel();
    }

    private void DrawParameters() {
        var p = serializedObject.FindProperty("parameters");
        showParameters = DrawHeader("PARAMETERS", showParameters, accentParameters);
        if (!showParameters) return;
        BeginPanel();

        showParametersStatus = DrawToggleSectionWithProp(p, "Status", "active", showParametersStatus);
        if (showParametersStatus) {
            EditorGUILayout.PropertyField(p.FindPropertyRelative("active"),
                new GUIContent("Active", "Enable or disable the turret entirely"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("canFire"),
                new GUIContent("Can Fire", "Allow the turret to shoot. Turret still aims when false"));
            EndSubPanel();
        }

        showParametersShooting = DrawToggleSection("Shooting", showParametersShooting);
        if (showParametersShooting) {
            EditorGUILayout.PropertyField(p.FindPropertyRelative("power"),
                new GUIContent("Power", "Damage dealt per shot and explosion force applied to the target"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("ShootingDelay"),
                new GUIContent("Delay (s)", "Pause between shots in seconds"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("radius"),
                new GUIContent("Radius", "Detection and maximum shoot range of the turret"));
            EndSubPanel();
        }

        EndPanel();
    }

    private void DrawTargeting() {
        var t = serializedObject.FindProperty("targeting");
        showTargeting = DrawHeader("TARGETING", showTargeting, accentTargeting);
        if (!showTargeting) return;
        BeginPanel();

        showTargetingAiming = DrawToggleSection("Aiming", showTargetingAiming);
        if (showTargetingAiming) {
            EditorGUILayout.PropertyField(t.FindPropertyRelative("aimingSpeed"),
                new GUIContent("Speed", "How fast the turret rotates toward its target via torque"));
            EditorGUILayout.PropertyField(t.FindPropertyRelative("aimingDelay"),
                new GUIContent("Delay (s)", "Pause before the turret starts rotating after acquiring a target. Set to 0 for instant"));
            EndSubPanel();
        }

        showTargetingTags = DrawToggleSection("Tags", showTargetingTags);
        if (showTargetingTags) {
            EditorGUILayout.PropertyField(t.FindPropertyRelative("tagsToFire"),
                new GUIContent("Tags to Fire", "GameObjects with these tags will be identified as enemies and targeted"));
            EndSubPanel();
        }

        EndPanel();
    }

    private void DrawEffects() {
        showEffects = DrawHeader("EFFECTS", showEffects, accentEffects);
        if (!showEffects) return;
        BeginPanel();

        var vfx = serializedObject.FindProperty("VFX");
        showEffectsVFX = DrawToggleSection("VFX", showEffectsVFX);
        if (showEffectsVFX) {
            EditorGUILayout.PropertyField(vfx.FindPropertyRelative("shotFX"),
                new GUIContent("Shot FX", "Particle prefab instantiated at the muzzle on each shot. Destroyed after 2 seconds"));
            EndSubPanel();
        }

        var sfx = serializedObject.FindProperty("SFX");
        showEffectsSFX = DrawToggleSection("SFX", showEffectsSFX);
        if (showEffectsSFX) {
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("shotClip"),
                new GUIContent("Shot Clip", "Audio clip played at a random volume (0.75–1.0) on each shot"));
            EndSubPanel();
        }

        EndPanel();
    }

    private void DrawPhysics() {
        var p = serializedObject.FindProperty("parameters");
        showPhysics = DrawHeader("PHYSICS", showPhysics, accentPhysics);
        if (!showPhysics) return;
        BeginPanel();

        EditorGUILayout.PropertyField(p.FindPropertyRelative("rotationDamping"),
            new GUIContent("Rotation Damping", "Angular damping applied to the rigidbody. Prevents the turret from spinning past its target"));

        showPhysicsCollider = DrawToggleSection("Body Collider", showPhysicsCollider);
        if (showPhysicsCollider) {
            EditorGUILayout.PropertyField(p.FindPropertyRelative("bodyColliderSize"),
                new GUIContent("Size", "Size of the BoxCollider representing the turret body"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("bodyColliderCenter"),
                new GUIContent("Center", "Center offset of the BoxCollider relative to the turret pivot"));
            EndSubPanel();
        }

        EndPanel();
    }

    private void DrawRuntime() {
        DrawHeader("RUNTIME", true, accentRuntime);
        BeginPanel();

        STT_Turret turret = (STT_Turret)target;
        EditorGUI.BeginDisabledGroup(true);

        string targetName = turret.targeting.target != null ? turret.targeting.target.name : "None";
        EditorGUILayout.TextField(new GUIContent("Target", "Currently selected target"), targetName);
        EditorGUILayout.IntField(new GUIContent("Target Count", "Number of enemies in detection range"), turret.targeting.targets.Count);

        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type  = typeof(STT_Turret);
        bool isAiming   = (bool)type.GetField("isAiming",   flags).GetValue(turret);
        bool isShooting = (bool)type.GetField("isShooting", flags).GetValue(turret);
        EditorGUILayout.Toggle(new GUIContent("Aiming",   "Whether the turret is currently allowed to rotate toward its target"), isAiming);
        EditorGUILayout.Toggle(new GUIContent("Shooting", "Whether a shot is currently queued via Invoke"), isShooting);

        EditorGUI.EndDisabledGroup();
        EndPanel();
        Repaint();
    }

    // ─── Helpers ───

    private bool DrawHeader(string title, bool foldout, Color accent) {
        Rect rect = EditorGUILayout.GetControlRect(false, 32);
        EditorGUI.DrawRect(rect, headerColor);

        Rect accentRect = new Rect(rect.x, rect.y, 4, rect.height);
        EditorGUI.DrawRect(accentRect, accent);

        Rect arrowRect = new Rect(rect.x + 8, rect.y + 8, 16, 16);
        EditorGUI.LabelField(arrowRect, foldout ? "\u25BC" : "\u25B6", EditorStyles.miniLabel);

        Rect labelRect = new Rect(rect.x + 32, rect.y, rect.width - 32, rect.height);
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
        headerStyle.fontSize  = 12;
        headerStyle.alignment = TextAnchor.MiddleLeft;
        headerStyle.normal.textColor = accent;
        EditorGUI.LabelField(labelRect, title, headerStyle);

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
            foldout = !foldout;
            Event.current.Use();
        }

        return foldout;
    }

    private bool DrawToggleSection(string title, bool expanded) {
        EditorGUILayout.Space(2);
        Rect rect = EditorGUILayout.GetControlRect(false, 24);
        EditorGUI.DrawRect(rect, expanded ? new Color(0.12f, 0.12f, 0.12f, 1f) : toggleOffColor);

        Rect labelRect = new Rect(rect.x + 8, rect.y, rect.width - 8, rect.height);
        EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
            expanded = !expanded;
            Event.current.Use();
        }

        if (expanded) {
            EditorGUILayout.Space(-2);
            BeginSubPanel();
        }
        return expanded;
    }

    private bool DrawToggleSectionWithProp(SerializedProperty parent, string title, string toggleProp, bool expanded) {
        EditorGUILayout.Space(2);
        Rect rect = EditorGUILayout.GetControlRect(false, 24);
        EditorGUI.DrawRect(rect, expanded ? new Color(0.12f, 0.12f, 0.12f, 1f) : toggleOffColor);

        var  prop       = parent.FindPropertyRelative(toggleProp);
        Rect toggleRect = new Rect(rect.x + 4, rect.y + 4, 16, 16);
        prop.boolValue  = EditorGUI.Toggle(toggleRect, prop.boolValue);

        Rect labelRect = new Rect(rect.x + 24, rect.y, rect.width - 24, rect.height);
        EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
            expanded = !expanded;
            Event.current.Use();
        }

        if (expanded) {
            EditorGUILayout.Space(-2);
            BeginSubPanel();
        }
        return expanded;
    }

    private void BeginPanel()    { EditorGUILayout.BeginVertical(PanelStyle);    }
    private void EndPanel()      { EditorGUILayout.EndVertical();                }
    private void BeginSubPanel() { EditorGUILayout.BeginVertical(SubPanelStyle); }
    private void EndSubPanel()   { EditorGUILayout.EndVertical();                }
}

}
