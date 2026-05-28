using System.Collections.Generic;
using UnityEngine;

namespace DT.Facility
{
    [System.Serializable]
    public struct AreaDef
    {
        public string id;
        public string label;
        public float x, y, w, h;
        public float height;
        public Color color;
    }

    public class FacilityBuilder : MonoBehaviour
    {
        // Site dimensions in meters (approximate from yard layout)
        const float SiteWidth = 500f;
        const float SiteDepth = 350f;

        static readonly Color BuildingColor = new Color(0.6f, 0.6f, 0.65f);
        static readonly Color YardColor = new Color(0.75f, 0.7f, 0.5f, 0.6f);
        static readonly Color EquipColor = new Color(0.5f, 0.65f, 0.5f);
        static readonly Color SpecialColor = new Color(0.7f, 0.55f, 0.55f);

        public void Build()
        {
            var parent = new GameObject("Facility");

            // Ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "SiteGround";
            ground.transform.SetParent(parent.transform);
            ground.transform.localPosition = new Vector3(SiteWidth / 2, 0, -SiteDepth / 2);
            ground.transform.localScale = new Vector3(SiteWidth / 10, 1, SiteDepth / 10);
            var gMat = new Material(Shader.Find("Standard"));
            gMat.color = new Color(0.85f, 0.85f, 0.8f);
            ground.GetComponent<Renderer>().material = gMat;

            foreach (var area in GetOutdoorAreas())
                CreateBuilding(parent.transform, area);

            Debug.Log($"Facility built: {GetOutdoorAreas().Count} areas on {SiteWidth}x{SiteDepth}m site");
        }

        void CreateBuilding(Transform parent, AreaDef area)
        {
            float posX = (area.x + area.w / 2f) / 100f * SiteWidth;
            float posZ = -(area.y + area.h / 2f) / 100f * SiteDepth;
            float sizeX = area.w / 100f * SiteWidth;
            float sizeZ = area.h / 100f * SiteDepth;
            float sizeY = area.height;

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = area.label;
            obj.transform.SetParent(parent);
            obj.transform.localPosition = new Vector3(posX, sizeY / 2f, posZ);
            obj.transform.localScale = new Vector3(sizeX, sizeY, sizeZ);

            var mat = new Material(Shader.Find("Standard"));
            mat.color = area.color;
            obj.GetComponent<Renderer>().material = mat;
        }

        public static List<AreaDef> GetOutdoorAreas()
        {
            return new List<AreaDef>
            {
                // ヤード（屋外作業場）- 低い箱
                new AreaDef { id = "8Y", label = "8ヤード", x = 15.5f, y = 12, w = 4, h = 2.5f, height = 0.3f, color = YardColor },
                new AreaDef { id = "新8Y", label = "新8ヤード", x = 3, y = 16, w = 5, h = 3, height = 0.3f, color = YardColor },
                new AreaDef { id = "新7Y", label = "新7ヤード", x = 12, y = 16.5f, w = 7, h = 3, height = 0.3f, color = YardColor },
                new AreaDef { id = "7Y", label = "7ヤード", x = 28, y = 15, w = 10, h = 5, height = 0.3f, color = YardColor },
                new AreaDef { id = "6Y", label = "6ヤード", x = 43, y = 15, w = 9, h = 5, height = 0.3f, color = YardColor },
                new AreaDef { id = "5Y", label = "5ヤード", x = 57, y = 15, w = 9, h = 5, height = 0.3f, color = YardColor },
                new AreaDef { id = "4Y", label = "4ヤード", x = 78, y = 14, w = 8, h = 6, height = 0.3f, color = YardColor },
                new AreaDef { id = "新7Y南", label = "新7ヤード南", x = 10, y = 22, w = 8, h = 3, height = 0.3f, color = YardColor },
                new AreaDef { id = "7Y南", label = "7ヤード南", x = 24, y = 22, w = 9, h = 4, height = 0.3f, color = YardColor },
                new AreaDef { id = "6Y南", label = "6ヤード南", x = 40, y = 22, w = 9, h = 4, height = 0.3f, color = YardColor },
                new AreaDef { id = "4Y南", label = "4ヤード南", x = 75, y = 22, w = 8, h = 4, height = 0.3f, color = YardColor },
                new AreaDef { id = "仮組Y", label = "仮組ヤード", x = 78, y = 28, w = 9, h = 5, height = 0.3f, color = YardColor },
                new AreaDef { id = "塗装Y", label = "塗装ヤード", x = 15, y = 32, w = 15, h = 8, height = 0.3f, color = YardColor },
                new AreaDef { id = "5-6棟西Y", label = "5-6棟間西ヤード", x = 20, y = 46, w = 12, h = 5, height = 0.3f, color = YardColor },
                new AreaDef { id = "材料Y西", label = "材料ヤード西", x = 30, y = 55, w = 10, h = 5, height = 0.3f, color = YardColor },
                new AreaDef { id = "材料Y東", label = "材料ヤード東", x = 42, y = 60, w = 10, h = 5, height = 0.3f, color = YardColor },

                // 棟（建屋）- 高い箱
                new AreaDef { id = "6棟", label = "第6棟", x = 56, y = 28, w = 6, h = 15, height = 15, color = BuildingColor },
                new AreaDef { id = "5棟", label = "第5棟", x = 62, y = 28, w = 5, h = 15, height = 15, color = BuildingColor },
                new AreaDef { id = "4棟", label = "第4棟", x = 54, y = 50, w = 5, h = 12, height = 12, color = BuildingColor },
                new AreaDef { id = "3棟", label = "第3棟", x = 59, y = 50, w = 5, h = 12, height = 12, color = BuildingColor },
                new AreaDef { id = "2棟", label = "第2棟", x = 64, y = 50, w = 5, h = 12, height = 12, color = BuildingColor },
                new AreaDef { id = "1棟", label = "第1棟", x = 69, y = 50, w = 5, h = 12, height = 12, color = BuildingColor },

                // 5-6棟間エリア
                new AreaDef { id = "5-6棟間", label = "5-6棟間", x = 48, y = 42, w = 7, h = 6, height = 3, color = EquipColor },

                // 設備エリア
                new AreaDef { id = "設備1", label = "設備エリア", x = 35, y = 48, w = 6, h = 4, height = 4, color = EquipColor },
                new AreaDef { id = "設備2", label = "設備エリア2", x = 18, y = 55, w = 8, h = 8, height = 4, color = EquipColor },
                new AreaDef { id = "設備倉庫", label = "設備倉庫", x = 85, y = 20, w = 6, h = 5, height = 6, color = EquipColor },

                // その他
                new AreaDef { id = "原寸棟", label = "原寸棟", x = 75, y = 40, w = 6, h = 4, height = 8, color = SpecialColor },
                new AreaDef { id = "正門", label = "正門", x = 88, y = 68, w = 5, h = 3, height = 3, color = SpecialColor },
                new AreaDef { id = "管理棟", label = "管理棟", x = 80, y = 50, w = 6, h = 5, height = 10, color = SpecialColor },
            };
        }
    }
}
