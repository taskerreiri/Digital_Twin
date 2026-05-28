using UnityEngine;
using DT.UI;

namespace DT.Entities
{
    /// <summary>
    /// Phase 2.2/2.3: カメラ別のシーン解析結果(状態判定/混雑度/OCR)を
    /// 画面右上のパネルにIMGUIで表示する。EntityManager.SceneAnalyses を参照。
    /// </summary>
    public class SceneAnalysisRenderer : MonoBehaviour
    {
        GUIStyle headerStyle;
        GUIStyle labelStyle;
        bool init;

        static Color StateColor(string state)
        {
            switch (state)
            {
                case "operating": return new Color(0.4f, 0.9f, 0.5f);
                case "idle": return new Color(0.7f, 0.7f, 0.7f);
                case "abnormal": return new Color(1f, 0.4f, 0.4f);
                default: return new Color(0.9f, 0.85f, 0.4f); // unknown
            }
        }

        void OnGUI()
        {
            if (!init)
            {
                headerStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 15, fontStyle = FontStyle.Bold,
                  normal = { textColor = new Color(0.4f, 0.8f, 1f) } };
                labelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 13, normal = { textColor = Color.white } };
                DTFonts.Apply(headerStyle);
                DTFonts.Apply(labelStyle);
                init = true;
            }

            var analyses = EntityManager.SceneAnalyses;
            if (analyses.Count == 0) return;

            float panelW = 300;
            float x = Screen.width - panelW - 10;
            float y = 10;
            float h = 30 + analyses.Count * 70;

            GUI.Box(new Rect(x, y, panelW, h), "");
            GUI.Label(new Rect(x + 10, y + 6, panelW - 20, 22),
                "カメラ解析 (Phase 2)", headerStyle);

            float ly = y + 32;
            foreach (var kv in analyses)
            {
                var a = kv.Value;
                var st = labelStyle;
                st.normal.textColor = StateColor(a.state);
                GUI.Label(new Rect(x + 10, ly, panelW - 20, 20),
                    $"{a.cameraId}: {a.state} / {a.congestion} [{a.sourceAi}]", st);
                ly += 20;

                st.normal.textColor = Color.white;
                int nTexts = a.texts != null ? a.texts.Length : 0;
                if (nTexts > 0)
                {
                    string ocr = "OCR: ";
                    for (int i = 0; i < nTexts && i < 3; i++)
                        ocr += a.texts[i].text + " ";
                    GUI.Label(new Rect(x + 20, ly, panelW - 30, 20), ocr, st);
                }
                else
                {
                    GUI.Label(new Rect(x + 20, ly, panelW - 30, 20), "OCR: -", st);
                }
                ly += 24;
            }
        }
    }
}
