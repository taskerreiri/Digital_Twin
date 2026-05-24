using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DT.GPS
{
    public class WebGLGPSProvider : MonoBehaviour
    {
        public event Action<GPSData> OnGPSUpdated;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void StartGeolocation(string callbackObj, string callbackMethod);

        [DllImport("__Internal")]
        static extern void StopGeolocation();
#endif

        void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            StartGeolocation(gameObject.name, "OnGeolocationUpdate");
#else
            Debug.Log("WebGLGPSProvider: only works in WebGL builds.");
#endif
        }

        // Called from JavaScript via SendMessage
        public void OnGeolocationUpdate(string payload)
        {
            if (payload.StartsWith("ERROR:"))
            {
                Debug.LogWarning($"GPS Error: {payload.Substring(6)}");
                return;
            }

            var parts = payload.Split(',');
            if (parts.Length < 5) return;

            var data = new GPSData
            {
                Latitude = double.Parse(parts[0]),
                Longitude = double.Parse(parts[1]),
                Altitude = float.Parse(parts[2]),
                HorizontalAccuracy = float.Parse(parts[3]),
                Timestamp = double.Parse(parts[4])
            };

            OnGPSUpdated?.Invoke(data);
        }

        void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            StopGeolocation();
#endif
        }
    }
}
