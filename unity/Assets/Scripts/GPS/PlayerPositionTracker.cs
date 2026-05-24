using System;
using UnityEngine;

namespace DT.GPS
{
    public class PlayerPositionTracker : MonoBehaviour
    {
        [SerializeField] GPSProvider gpsProvider;
        [SerializeField] MockGPSProvider mockGPSProvider;
        [SerializeField] GPSCalibrator calibrator;
        [SerializeField] Transform playerMarker;

        Action<GPSData> handler;

        void OnEnable()
        {
            handler = OnGPSUpdated;

            if (mockGPSProvider != null)
                mockGPSProvider.OnGPSUpdated += handler;
            else if (gpsProvider != null)
                gpsProvider.OnGPSUpdated += handler;
        }

        void OnDisable()
        {
            if (mockGPSProvider != null)
                mockGPSProvider.OnGPSUpdated -= handler;
            else if (gpsProvider != null)
                gpsProvider.OnGPSUpdated -= handler;
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
