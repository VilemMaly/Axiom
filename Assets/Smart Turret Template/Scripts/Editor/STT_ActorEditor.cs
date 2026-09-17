using System.Reflection;
using UnityEditor;
using UnityEngine;
using STT;

namespace STT.Editor {

[CustomEditor(typeof(STT_Actor))]
public class STT_ActorEditor : UnityEditor.Editor {

    private bool showParameters = true;
    private bool showEffects    = true;
    private bool showEffectsVFX = true;
    private bool showEffectsSFX = true;

    private static readonly Color headerColor      = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color accentParameters = new Color(0.85f, 0.3f,  0.3f,  1f);
    private static readonly Color accentEffects    = new Color(0.3f,  0.8f,  0.45f, 1f);
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

        DrawParameters();
        EditorGUILayout.Space(2);
        DrawEffects();

        if (Application.isPlaying) {
            EditorGUILayout.Space(2);
            DrawRuntime();
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ─── Sections ───

    private void DrawParameters() {
        var p = serializedObject.FindProperty("parameters");
        showParameters = DrawHeader("PARAMETERS", showParameters, accentParameters);
        if (!showParameters) return;
        BeginPanel();

        EditorGUILayout.PropertyField(p.FindPropertyRelative("toughness"),
            new GUIContent("Toughness", "Actor's health. Damage is subtracted from this. Destroyed when it reaches zero"));
        EditorGUILayout.PropertyField(p.FindPropertyRelative("armor"),
            new GUIContent("Armor", "Minimum collision velocity required to apply impact damage. Acts as a collision damage threshold"));
        EditorGUILayout.PropertyField(p.FindPropertyRelative("damageFactor"),
            new GUIContent("Damage Factor", "Multiplier applied to explosion force on each hit. Higher values produce more knockback"));

        EndPanel();
    }

    private void DrawEffects() {
        showEffects = DrawHeader("EFFECTS", showEffects, accentEffects);
        if (!showEffects) return;
        BeginPanel();

        var vfx = serializedObject.FindProperty("VFX");
        showEffectsVFX = DrawToggleSection("VFX", showEffectsVFX);
        if (showEffectsVFX) {
            EditorGUILayout.PropertyField(vfx.FindPropertyRelative("damageFX"),
                new GUIContent("Damage FX", "Particle prefab instantiated at the hit position on each damage event. Destroyed after 3 seconds"));
            EditorGUILayout.PropertyField(vfx.FindPropertyRelative("deactivateFX"),
                new GUIContent("Destroy FX", "Particle prefab instantiated at the actor position on death. Destroyed after 3 seconds"));
            EndSubPanel();
        }

        var sfx = serializedObject.FindProperty("SFX");
        showEffectsSFX = DrawToggleSection("SFX", showEffectsSFX);
        if (showEffectsSFX) {
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("destroyClip"),
                new GUIContent("Destroy Clip", "Audio clip played when the actor is destroyed. Plays in full before the GameObject is removed"));
            EndSubPanel();
        }

        EndPanel();
    }

    private void DrawRuntime() {
        DrawHeader("RUNTIME", true, accentRuntime);
        BeginPanel();

        STT_Actor actor = (STT_Actor)target;
        EditorGUI.BeginDisabledGroup(true);

        EditorGUILayout.FloatField(new GUIContent("Toughness", "Current remaining health"), actor.parameters.toughness);

        var flags  = BindingFlags.NonPublic | BindingFlags.Instance;
        bool isDead = (bool)typeof(STT_Actor).GetField("isDead", flags).GetValue(actor);
        EditorGUILayout.Toggle(new GUIContent("Dead", "Whether Die() has been called and the actor is pending destruction"), isDead);

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

    private void BeginPanel()    { EditorGUILayout.BeginVertical(PanelStyle);    }
    private void EndPanel()      { EditorGUILayout.EndVertical();                }
    private void BeginSubPanel() { EditorGUILayout.BeginVertical(SubPanelStyle); }
    private void EndSubPanel()   { EditorGUILayout.EndVertical();                }
}

}
