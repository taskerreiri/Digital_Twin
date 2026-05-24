using System;
using System.Collections;
using UnityEngine;

namespace DT.GPS
{
    public class GPSData
    {
        public double Latitude;
        public double Longitude;
        public float Altitude;
        public float HorizontalAccuracy;
        public double Timestamp;
    }

    public class GPSProvider : MonoBehaviour
    {
        public event Action<GPSData> OnGPSUpdated;

        [SerializeField] float updateInterval = 1f;
        [SerializeField] float desiredAccuracyInMeters = 5f;
        [SerializeField] float updateDistanceInMeters = 1f;

        bool isRunning;

        IEnumerator Start()
        {
            if (!Input.location.isEnabledByUser)
            {
                Debug.LogWarning("GPS is disabled by the user.");
                yield break;
            }

            Input.location.Start(desiredAccuracyInMeters, updateDistanceInMeters);

            int timeout = 20;
            while (Input.location.status == LocationServiceStatus.Initializing && timeout > 0)
            {
                yield return new WaitForSeconds(1);
                timeout--;
            }

            if (Input.location.status == LocationServiceStatus.Failed)
            {
                Debug.LogError("GPS initialization failed.");
                yield break;
            }

            isRunning = true;
            StartCoroutine(UpdateLoop());
        }

        IEnumerator UpdateLoop()
        {
            while (isRunning)
            {
                var loc = Input.location.lastData;
                var data = new GPSData
                {
                    Latitude = loc.latitude,
                    Longitude = loc.longitude,
                    Altitude = loc.altitude,
                    HorizontalAccuracy = loc.horizontalAccuracy,
                    Timestamp = loc.timestamp
                };
                OnGPSUpdated?.Invoke(data);
                yield return new WaitForSeconds(updateInterval);
            }
        }

        void OnDestroy()
        {
            isRunning = false;
            if (Input.location.status == LocationServiceStatus.Running)
                Input.location.Stop();
        }
    }
}
