using System;
using System.Collections;
using UnityEngine;

namespace DT.GPS
{
    public class MockGPSProvider : MonoBehaviour
    {
        public event Action<GPSData> OnGPSUpdated;

        [Header("Simulated Position")]
        [SerializeField] double latitude = 35.6812;
        [SerializeField] double longitude = 139.7671;
        [SerializeField] float altitude = 40f;

        [Header("Movement Simulation")]
        [SerializeField] bool simulateMovement = true;
        [SerializeField] float moveSpeed = 0.00001f;
        [SerializeField] float updateInterval = 1f;

        void Start()
        {
            StartCoroutine(SimulateGPS());
        }

        IEnumerator SimulateGPS()
        {
            while (true)
            {
                if (simulateMovement)
                {
                    float h = Input.GetAxis("Horizontal");
                    float v = Input.GetAxis("Vertical");
                    longitude += h * moveSpeed;
                    latitude += v * moveSpeed;
                }

                var data = new GPSData
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Altitude = altitude,
                    HorizontalAccuracy = 5f,
                    Timestamp = Time.timeAsDouble
                };

                OnGPSUpdated?.Invoke(data);
                yield return new WaitForSeconds(updateInterval);
            }
        }

        public void SetPosition(double lat, double lon)
        {
            latitude = lat;
            longitude = lon;
        }
    }
}
