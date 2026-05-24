using UnityEngine;
using UnityEngine.UI;
using DT.GPS;

namespace DT.UI
{
    public class GPSDebugUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] GPSProvider gpsProvider;
        [SerializeField] MockGPSProvider mockGPSProvider;
        [SerializeField] GPSCalibrator calibrator;
        [SerializeField] Transform playerMarker;

        [Header("UI Elements")]
        [SerializeField] Text gpsStatusText;
        [SerializeField] Text coordinateText;
        [SerializeField] Text calibrationText;
        [SerializeField] Text unityPositionText;
        [SerializeField] Button calibrateButton;
        [SerializeField] InputField pointNameInput;

        int calibrationCount;
        GPSData lastGPSData;

        void Start()
        {
            if (calibrateButton != null)
                calibrateButton.onClick.AddListener(OnCalibratePressed);

            if (mockGPSProvider != null)
                mockGPSProvider.OnGPSUpdated += OnGPSUpdated;
            else if (gpsProvider != null)
                gpsProvider.OnGPSUpdated += OnGPSUpdated;

            UpdateStatusText("Waiting for GPS...");
        }

        void OnGPSUpdated(GPSData data)
        {
            lastGPSData = data;

            UpdateStatusText($"GPS Active (Accuracy: {data.HorizontalAccuracy:F1}m)");
            UpdateCoordinateText(
                $"Lat: {data.Latitude:F6}\n" +
                $"Lon: {data.Longitude:F6}\n" +
                $"Alt: {data.Altitude:F1}m"
            );

            if (calibrator != null && calibrator.IsCalibrated)
            {
                Vector3 pos = calibrator.GPSToUnity(data.Latitude, data.Longitude);
                UpdateUnityPositionText($"Unity Pos: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
            }
        }

        void OnCalibratePressed()
        {
            if (lastGPSData == null)
            {
                UpdateCalibrationText("No GPS data yet.");
                return;
            }

            calibrationCount++;
            string pointName = (pointNameInput != null && !string.IsNullOrEmpty(pointNameInput.text))
                ? pointNameInput.text
                : $"Point_{calibrationCount}";

            Vector3 markerPos = playerMarker != null ? playerMarker.position : Vector3.zero;

            calibrator.AddCalibrationPoint(pointName, lastGPSData.Latitude, lastGPSData.Longitude, markerPos);

            string status = calibrator.IsCalibrated
                ? $"Calibrated! ({calibrationCount} points)"
                : $"Point {calibrationCount} saved. Need {2 - calibrationCount} more.";

            UpdateCalibrationText(status);

            if (pointNameInput != null)
                pointNameInput.text = "";
        }

        void UpdateStatusText(string text)
        {
            if (gpsStatusText != null) gpsStatusText.text = text;
        }

        void UpdateCoordinateText(string text)
        {
            if (coordinateText != null) coordinateText.text = text;
        }

        void UpdateCalibrationText(string text)
        {
            if (calibrationText != null) calibrationText.text = text;
        }

        void UpdateUnityPositionText(string text)
        {
            if (unityPositionText != null) unityPositionText.text = text;
        }

        void OnDestroy()
        {
            if (mockGPSProvider != null)
                mockGPSProvider.OnGPSUpdated -= OnGPSUpdated;
            else if (gpsProvider != null)
                gpsProvider.OnGPSUpdated -= OnGPSUpdated;
        }
    }
}
