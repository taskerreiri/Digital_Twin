using UnityEngine;

namespace DT.GPS
{
    public class PlayerPositionTracker : MonoBehaviour
    {
        [SerializeField] GPSProvider gpsProvider;
        [SerializeField] GPSCalibrator calibrator;
        [SerializeField] Transform playerMarker;

        void OnEnable()
        {
            if (gpsProvider != null)
                gpsProvider.OnGPSUpdated += OnGPSUpdated;
        }

        void OnDisable()
        {
            if (gpsProvider != null)
                gpsProvider.OnGPSUpdated -= OnGPSUpdated;
        }

        void OnGPSUpdated(GPSData data)
        {
            if (!calibrator.IsCalibrated)
                return;

            Vector3 pos = calibrator.GPSToUnity(data.Latitude, data.Longitude);
            if (playerMarker != null)
                playerMarker.position = pos;
        }
    }
}
