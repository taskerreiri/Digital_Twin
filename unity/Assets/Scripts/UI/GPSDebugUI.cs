using UnityEngine;
using DT.GPS;

namespace DT.UI
{
    public class GPSDebugUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] GPSProvider gpsProvider;
        [SerializeField] MockGPSProvider mockGPSProvider;
        [SerializeField] WebGLGPSProvider webGLGPSProvider;
        [SerializeField] GPSCalibrator calibrator;
        [SerializeField] Transform playerMarker;

        int calibrationCount;
        GPSData lastGPSData;
        string pointName = "";
        string statusMsg = "Waiting for GPS...";
        string coordMsg = "Lat: --\nLon: --\nAlt: --";
        string unityPosMsg = "Unity Pos: --";
        string calibMsg = "Not calibrated";

        GUIStyle labelStyle;
        GUIStyle headerStyle;
        GUIStyle statusStyle;
        bool stylesInitialized;

        void Start()
        {
            if (webGLGPSProvider != null)
                webGLGPSProvider.OnGPSUpdated += OnGPSUpdated;
            else if (mockGPSProvider != null)
                mockGPSProvider.OnGPSUpdated += OnGPSUpdated;
            else if (gpsProvider != null)
                gpsProvider.OnGPSUpdated += OnGPSUpdated;
        }

        void InitStyles()
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = Color.white },
                wordWrap = true
            };
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.29f, 0.62f, 1f) }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = Color.green }
            };
            DTFonts.Apply(labelStyle);
            DTFonts.Apply(headerStyle);
            DTFonts.Apply(statusStyle);
            stylesInitialized = true;
        }

        void OnGUI()
        {
            if (!stylesInitialized) InitStyles();

            float panelW = 340;
            float panelH = 360;

            GUI.Box(new Rect(10, 10, panelW, panelH), "");

            float x = 20;
            float y = 20;

            GUI.Label(new Rect(x, y, panelW - 20, 30), "Digital Twin GPS", headerStyle);
            y += 35;

            GUI.Label(new Rect(x, y, panelW - 20, 25), statusMsg, statusStyle);
            y += 30;

            GUI.Label(new Rect(x, y, panelW - 20, 60), coordMsg, labelStyle);
            y += 65;

            GUI.Label(new Rect(x, y, panelW - 20, 25), unityPosMsg,
                new GUIStyle(labelStyle) { normal = { textColor = Color.cyan } });
            y += 30;

            GUI.Label(new Rect(x, y, panelW - 20, 25), calibMsg,
                new GUIStyle(labelStyle) { normal = { textColor = Color.yellow } });
            y += 35;

            GUI.Label(new Rect(x, y, 100, 25), "Point Name:", labelStyle);
            pointName = GUI.TextField(new Rect(x + 105, y, panelW - 130, 25), pointName);
            y += 35;

            if (GUI.Button(new Rect(x, y, panelW - 20, 35), "Add Calibration Point"))
                OnCalibratePressed();
        }

        void OnGPSUpdated(GPSData data)
        {
            lastGPSData = data;
            statusMsg = $"GPS Active (Accuracy: {data.HorizontalAccuracy:F1}m)";
            coordMsg = $"Lat: {data.Latitude:F6}\nLon: {data.Longitude:F6}\nAlt: {data.Altitude:F1}m";

            if (calibrator != null && calibrator.IsCalibrated)
            {
                Vector3 pos = calibrator.GPSToUnity(data.Latitude, data.Longitude);
                unityPosMsg = $"Unity Pos: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})";
            }
        }

        void OnCalibratePressed()
        {
            if (lastGPSData == null)
            {
                calibMsg = "No GPS data yet.";
                return;
            }

            calibrationCount++;
            string name = string.IsNullOrEmpty(pointName) ? $"Point_{calibrationCount}" : pointName;
            Vector3 markerPos = playerMarker != null ? playerMarker.position : Vector3.zero;

            calibrator.AddCalibrationPoint(name, lastGPSData.Latitude, lastGPSData.Longitude, markerPos);

            calibMsg = calibrator.IsCalibrated
                ? $"Calibrated! ({calibrationCount} points)"
                : $"Point {calibrationCount} saved. Need {2 - calibrationCount} more.";

            pointName = "";
        }

        void OnDestroy()
        {
            if (webGLGPSProvider != null)
                webGLGPSProvider.OnGPSUpdated -= OnGPSUpdated;
            else if (mockGPSProvider != null)
                mockGPSProvider.OnGPSUpdated -= OnGPSUpdated;
            else if (gpsProvider != null)
                gpsProvider.OnGPSUpdated -= OnGPSUpdated;
        }
    }
}
