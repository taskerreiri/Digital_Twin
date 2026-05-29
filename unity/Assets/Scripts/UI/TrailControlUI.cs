using UnityEngine;
using DT.Entities;

namespace DT.UI
{
    /// <summary>軌跡(trail)の種別ON/OFFトグルパネル。EntityManager を参照して反映する。</summary>
    public class TrailControlUI : MonoBehaviour
    {
        [SerializeField] EntityManager entityManager;
        GUIStyle headerStyle, labelStyle;
        bool stylesReady;

        void Start()
        {
            if (entityManager == null)
                entityManager = FindFirstObjectByType<EntityManager>();
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            labelStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            DTFonts.Apply(headerStyle);
            DTFonts.Apply(labelStyle);
            stylesReady = true;
        }

        void OnGUI()
        {
            if (entityManager == null) return;
            EnsureStyles();

            // 左下にパネル
            const float w = 180f, h = 96f;
            var rect = new Rect(10f, Screen.height - h - 10f, w, h);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, w - 16f, h - 12f));
            try
            {
                GUILayout.Label("軌跡表示", headerStyle);

                bool worker = GUILayout.Toggle(entityManager.ShowWorkerTrails, " 作業員", labelStyle);
                bool equip = GUILayout.Toggle(entityManager.ShowEquipmentTrails, " 重機", labelStyle);

                if (worker != entityManager.ShowWorkerTrails || equip != entityManager.ShowEquipmentTrails)
                {
                    entityManager.ShowWorkerTrails = worker;
                    entityManager.ShowEquipmentTrails = equip;
                    entityManager.ApplyTrailVisibility();
                }
            }
            finally
            {
                GUILayout.EndArea();
            }
        }
    }
}
