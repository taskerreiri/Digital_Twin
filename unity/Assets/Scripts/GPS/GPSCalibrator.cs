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

    /// <summary>
    /// GPS(WGS84) ↔ Unity座標 の変換。
    /// 2点以上のキャリブレーションペアから2D相似変換(回転+等方スケール+並進)を
    /// 最小二乗(Procrustes)で推定する。スマホGPSのノイズに対し3点以上で精度が安定。
    /// </summary>
    public class GPSCalibrator : MonoBehaviour
    {
        [SerializeField] List<CalibrationPoint> calibrationPoints = new();

        // 変換パラメータ
        double refLat, refLon;       // メートル換算の基準 (GPS重心)
        double metersPerDegLon;      // refLatでの経度1度あたりメートル
        double rotation;             // ラジアン
        double scale = 1.0;
        Vector3 translation;         // Unity並進 (Unity重心)
        bool isCalibrated;

        const double MetersPerDegreeLat = 111320.0;

        public bool IsCalibrated => isCalibrated;
        public int PointCount => calibrationPoints.Count;

        public void AddCalibrationPoint(string name, double lat, double lon, Vector3 unityPos)
        {
            calibrationPoints.Add(new CalibrationPoint
            {
                Name = name, Latitude = lat, Longitude = lon, UnityPosition = unityPos
            });
            if (calibrationPoints.Count >= 2) Calibrate();
        }

        /// <summary>全ペアを一括設定して再計算する (ランドマーク方式用)</summary>
        public void SetCalibrationPairs(IEnumerable<CalibrationPoint> pairs)
        {
            calibrationPoints = new List<CalibrationPoint>(pairs);
            if (calibrationPoints.Count >= 2) Calibrate();
            else isCalibrated = false;
        }

        public void ClearCalibration()
        {
            calibrationPoints.Clear();
            isCalibrated = false;
        }

        void Calibrate()
        {
            int n = calibrationPoints.Count;

            // GPS重心を基準点に (メートル換算の数値安定性確保)
            double sumLat = 0, sumLon = 0;
            foreach (var p in calibrationPoints) { sumLat += p.Latitude; sumLon += p.Longitude; }
            refLat = sumLat / n;
            refLon = sumLon / n;
            metersPerDegLon = MetersPerDegreeLat * Math.Cos(refLat * Math.PI / 180.0);

            // ソース(GPSメートル, 重心中心) と ターゲット(Unity XZ) を構築
            var src = new (double e, double nth)[n];   // east, north
            var tgt = new (double x, double z)[n];
            Vector3 cq = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var p = calibrationPoints[i];
                src[i] = ((p.Longitude - refLon) * metersPerDegLon,
                          (p.Latitude - refLat) * MetersPerDegreeLat);
                tgt[i] = (p.UnityPosition.x, p.UnityPosition.z);
                cq += p.UnityPosition;
            }
            cq /= n;

            // Procrustes 2D相似: a=Σ(s·t_centered), b=Σ(s×t_centered)
            double a = 0, b = 0, denom = 0;
            for (int i = 0; i < n; i++)
            {
                double tx = tgt[i].x - cq.x;
                double tz = tgt[i].z - cq.z;
                a += src[i].e * tx + src[i].nth * tz;
                b += src[i].e * tz - src[i].nth * tx;
                denom += src[i].e * src[i].e + src[i].nth * src[i].nth;
            }

            if (denom < 1e-9)
            {
                Debug.LogWarning("[GPSCalibrator] degenerate points (too close)");
                isCalibrated = false;
                return;
            }

            rotation = Math.Atan2(b, a);
            scale = Math.Sqrt(a * a + b * b) / denom;
            translation = cq;
            isCalibrated = true;

            // 残差(RMS)を診断出力
            double sqErr = 0;
            for (int i = 0; i < n; i++)
            {
                var u = GPSToUnity(calibrationPoints[i].Latitude, calibrationPoints[i].Longitude);
                double dx = u.x - calibrationPoints[i].UnityPosition.x;
                double dz = u.z - calibrationPoints[i].UnityPosition.z;
                sqErr += dx * dx + dz * dz;
            }
            double rms = Math.Sqrt(sqErr / n);
            Debug.Log($"[GPSCalibrator] {n} pts: scale={scale:F4}, rot={rotation * 180 / Math.PI:F2}deg, RMS={rms:F2}m");
        }

        public Vector3 GPSToUnity(double lat, double lon)
        {
            if (!isCalibrated) return Vector3.zero;

            double east = (lon - refLon) * metersPerDegLon;
            double north = (lat - refLat) * MetersPerDegreeLat;

            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);
            double rx = scale * (cos * east - sin * north);
            double rz = scale * (sin * east + cos * north);

            return new Vector3(
                translation.x + (float)rx,
                translation.y,
                translation.z + (float)rz
            );
        }

        public CalibrationPoint[] GetCalibrationPoints() => calibrationPoints.ToArray();
    }
}
