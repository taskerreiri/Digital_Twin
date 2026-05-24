using System;
using System.Collections.Generic;
using UnityEngine;

namespace DT.GPS
{
    [Serializable]
    public class CalibrationPoint
    {
        public string Name;
        public double Latitude;
        public double Longitude;
        public Vector3 UnityPosition;
    }

    public class GPSCalibrator : MonoBehaviour
    {
        [SerializeField] List<CalibrationPoint> calibrationPoints = new();

        Vector2d originGPS;
        Vector3 originUnity;
        double scaleX = 1.0;
        double scaleZ = 1.0;
        double rotation = 0.0;
        bool isCalibrated;

        const double MetersPerDegreeLat = 111320.0;

        public bool IsCalibrated => isCalibrated;

        public void AddCalibrationPoint(string name, double lat, double lon, Vector3 unityPos)
        {
            calibrationPoints.Add(new CalibrationPoint
            {
                Name = name,
                Latitude = lat,
                Longitude = lon,
                UnityPosition = unityPos
            });

            if (calibrationPoints.Count >= 2)
                Calibrate();
        }

        public void ClearCalibration()
        {
            calibrationPoints.Clear();
            isCalibrated = false;
        }

        void Calibrate()
        {
            var p0 = calibrationPoints[0];
            var p1 = calibrationPoints[1];

            originGPS = new Vector2d(p0.Latitude, p0.Longitude);
            originUnity = p0.UnityPosition;

            double dLat = p1.Latitude - p0.Latitude;
            double dLon = p1.Longitude - p0.Longitude;

            double metersPerDegreeLon = MetersPerDegreeLat * Math.Cos(p0.Latitude * Math.PI / 180.0);

            double dMetersX = dLon * metersPerDegreeLon;
            double dMetersZ = dLat * MetersPerDegreeLat;

            double dUnityX = p1.UnityPosition.x - p0.UnityPosition.x;
            double dUnityZ = p1.UnityPosition.z - p0.UnityPosition.z;

            double gpsAngle = Math.Atan2(dMetersX, dMetersZ);
            double unityAngle = Math.Atan2(dUnityX, dUnityZ);
            rotation = unityAngle - gpsAngle;

            double gpsDist = Math.Sqrt(dMetersX * dMetersX + dMetersZ * dMetersZ);
            double unityDist = Math.Sqrt(dUnityX * dUnityX + dUnityZ * dUnityZ);

            if (gpsDist > 0.001)
            {
                double scale = unityDist / gpsDist;
                scaleX = scale;
                scaleZ = scale;
            }

            isCalibrated = true;
            Debug.Log($"Calibrated with {calibrationPoints.Count} points. Scale={scaleX:F4}, Rotation={rotation * 180 / Math.PI:F2} deg");
        }

        public Vector3 GPSToUnity(double lat, double lon)
        {
            if (!isCalibrated)
            {
                Debug.LogWarning("Not calibrated yet.");
                return Vector3.zero;
            }

            double metersPerDegreeLon = MetersPerDegreeLat * Math.Cos(originGPS.x * Math.PI / 180.0);

            double dMetersX = (lon - originGPS.y) * metersPerDegreeLon;
            double dMetersZ = (lat - originGPS.x) * MetersPerDegreeLat;

            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);
            double rotatedX = dMetersX * cos - dMetersZ * sin;
            double rotatedZ = dMetersX * sin + dMetersZ * cos;

            return new Vector3(
                originUnity.x + (float)(rotatedX * scaleX),
                originUnity.y,
                originUnity.z + (float)(rotatedZ * scaleZ)
            );
        }

        public CalibrationPoint[] GetCalibrationPoints() => calibrationPoints.ToArray();
    }

    public struct Vector2d
    {
        public double x;
        public double y;

        public Vector2d(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
