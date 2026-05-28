using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace DT.GPS
{
    /// <summary>
    /// サーバーからランドマーク(Unity座標既知)とキャリブレーションサンプル(スマホGPS)を
    /// 取得し、landmarkIdで紐付けて GPSCalibrator に投入する。
    /// ランドマーク方式: 作業員が各ランドマーク地点でPWAの基準点記録を押すと
    /// スマホGPSがサーバーに保存され、ここで既知Unity座標とペアになる。
    /// </summary>
    public class CalibrationSync : MonoBehaviour
    {
        [SerializeField] string serverHttpUrl = "http://localhost:9300";
        [SerializeField] float pollInterval = 15f;
        [SerializeField] GPSCalibrator calibrator;

        [Serializable] class Landmark { public string landmarkId; public string label; public float unityX; public float unityZ; }
        [Serializable] class LandmarkList { public Landmark[] landmarks; }
        [Serializable] class CalSample { public string landmarkId; public double lat; public double lon; public int samples; }
        [Serializable] class CalList { public CalSample[] samples; }

        readonly Dictionary<string, Landmark> landmarkMap = new();
        int lastAppliedCount = -1;

        public event Action OnCalibrationApplied;

        void Start()
        {
            if (calibrator == null) calibrator = FindFirstObjectByType<GPSCalibrator>();
            StartCoroutine(Loop());
        }

        IEnumerator Loop()
        {
            yield return FetchLandmarks();
            while (true)
            {
                yield return FetchAndApplyCalibration();
                yield return new WaitForSeconds(pollInterval);
            }
        }

        IEnumerator FetchLandmarks()
        {
            using var req = UnityWebRequest.Get($"{serverHttpUrl}/api/landmarks");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[CalibrationSync] landmarks fetch failed: {req.error}");
                yield break;
            }
            var list = JsonUtility.FromJson<LandmarkList>(req.downloadHandler.text);
            landmarkMap.Clear();
            if (list?.landmarks != null)
                foreach (var l in list.landmarks) landmarkMap[l.landmarkId] = l;
            Debug.Log($"[CalibrationSync] {landmarkMap.Count} landmarks loaded");
        }

        IEnumerator FetchAndApplyCalibration()
        {
            using var req = UnityWebRequest.Get($"{serverHttpUrl}/api/calibration");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var list = JsonUtility.FromJson<CalList>(req.downloadHandler.text);
            if (list?.samples == null) yield break;

            var pairs = new List<CalibrationPoint>();
            foreach (var s in list.samples)
            {
                if (!landmarkMap.TryGetValue(s.landmarkId, out var lm)) continue;
                pairs.Add(new CalibrationPoint
                {
                    Name = lm.label,
                    Latitude = s.lat,
                    Longitude = s.lon,
                    UnityPosition = new Vector3(lm.unityX, 0f, lm.unityZ),
                });
            }

            // 新たなサンプルが揃ったときのみ再計算
            if (pairs.Count >= 2 && pairs.Count != lastAppliedCount)
            {
                calibrator.SetCalibrationPairs(pairs);
                lastAppliedCount = pairs.Count;
                Debug.Log($"[CalibrationSync] applied {pairs.Count} calibration pairs");
                OnCalibrationApplied?.Invoke();
            }
        }
    }
}
