using UnityEngine;

namespace DT.Facility
{
    public class FacilityLabels : MonoBehaviour
    {
        GUIStyle labelStyle;
        Camera mainCam;
        Transform facilityRoot;
        bool initialized;

        void Start()
        {
            mainCam = Camera.main;
        }

        void OnGUI()
        {
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam == null) return;

            if (!initialized)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                initialized = true;
            }

            if (facilityRoot == null)
                facilityRoot = GameObject.Find("Facility")?.transform;
            if (facilityRoot == null) return;

            foreach (Transform child in facilityRoot)
            {
                Vector3 worldPos = child.position + Vector3.up * (child.localScale.y / 2 + 1);
                Vector3 screen = mainCam.WorldToScreenPoint(worldPos);

                if (screen.z < 0) continue;

                float screenY = Screen.height - screen.y;
                float dist = Vector3.Distance(mainCam.transform.position, worldPos);

                if (dist > 300) continue;

                float scale = Mathf.Clamp(80f / dist, 0.5f, 2f);
                var style = new GUIStyle(labelStyle) { fontSize = (int)(12 * scale) };

                Vector2 size = style.CalcSize(new GUIContent(child.name));
                Rect rect = new Rect(screen.x - size.x / 2, screenY - size.y / 2, size.x, size.y);

                GUI.Label(rect, child.name, style);
            }
        }
    }
}
