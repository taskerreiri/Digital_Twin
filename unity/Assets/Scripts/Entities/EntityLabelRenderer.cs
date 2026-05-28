using UnityEngine;
using DT.UI;

namespace DT.Entities
{
    /// <summary>
    /// 全 EntityLabel をワールド空間からスクリーン座標に変換して名前を描画する (IMGUI)。
    /// FacilityLabels と同じ方式。距離カリング付き。
    /// </summary>
    public class EntityLabelRenderer : MonoBehaviour
    {
        [SerializeField] float maxDistance = 250f;
        Camera cam;
        GUIStyle style;

        void Start()
        {
            cam = Camera.main;
        }

        void OnGUI()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                };
                DTFonts.Apply(style);
            }

            var labels = FindObjectsByType<EntityLabel>(FindObjectsSortMode.None);
            foreach (var label in labels)
            {
                Vector3 worldPos = label.transform.position + Vector3.up * 2f;
                Vector3 screen = cam.WorldToScreenPoint(worldPos);
                if (screen.z < 0) continue;

                float dist = Vector3.Distance(cam.transform.position, label.transform.position);
                if (dist > maxDistance) continue;

                float scale = Mathf.Clamp(1f - dist / maxDistance, 0.4f, 1f);
                style.fontSize = Mathf.RoundToInt(14 * scale) + 6;
                style.normal.textColor = Color.white;

                float y = Screen.height - screen.y;
                var rect = new Rect(screen.x - 60, y - 12, 120, 24);

                // 背景
                var bg = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.5f * scale);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = bg;

                GUI.Label(rect, label.displayName, style);
            }
        }
    }
}
