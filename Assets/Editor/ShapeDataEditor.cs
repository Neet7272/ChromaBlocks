using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ShapeData))]
public sealed class ShapeDataEditor : Editor
{
    static readonly float s_CellSize = 32f;
    static readonly float s_CellPad = 4f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var data = (ShapeData)target;
        GUILayout.Space(8);
        EditorGUILayout.LabelField("5x5 Rol Izgarası", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Hücreye tıklayın: BlockRole değerleri arasında döner (None → Primary → Secondary → Tertiary).", MessageType.Info);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            for (int y = ShapeData.BoardSize - 1; y >= 0; y--) // üst satır yukarıda görünsün
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int x = 0; x < ShapeData.BoardSize; x++)
                    {
                        var current = data.GetCell(x, y);
                        var rect = GUILayoutUtility.GetRect(s_CellSize, s_CellSize, GUILayout.ExpandWidth(false));
                        rect.width = s_CellSize;
                        rect.height = s_CellSize;
                        rect.x += s_CellPad * 0.5f;
                        rect.y += s_CellPad * 0.5f;

                        var prev = GUI.color;
                        GUI.color = current switch
                        {
                            BlockRole.None => new Color(0.2f, 0.2f, 0.2f, 0.35f),
                            BlockRole.Primary => new Color(0.9f, 0.35f, 0.35f, 1f),
                            BlockRole.Secondary => new Color(0.35f, 0.55f, 0.95f, 1f),
                            BlockRole.Tertiary => new Color(0.55f, 0.9f, 0.45f, 1f),
                            _ => Color.white
                        };

                        if (GUI.Button(rect, GUIContent.none))
                        {
                            Undo.RecordObject(data, "Change Shape Cell Role");
                            data.SetCell(x, y, NextRole(current));
                            EditorUtility.SetDirty(data);
                        }

                        GUI.color = prev;
                    }
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Hepsini None Yap"))
            {
                Undo.RecordObject(data, "Clear Shape Board");
                for (int y = 0; y < ShapeData.BoardSize; y++)
                    for (int x = 0; x < ShapeData.BoardSize; x++)
                        data.SetCell(x, y, BlockRole.None);
                EditorUtility.SetDirty(data);
            }
        }
    }

    static BlockRole NextRole(BlockRole c)
    {
        return c switch
        {
            BlockRole.None => BlockRole.Primary,
            BlockRole.Primary => BlockRole.Secondary,
            BlockRole.Secondary => BlockRole.Tertiary,
            _ => BlockRole.None
        };
    }
}

