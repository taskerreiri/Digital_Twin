using System;
using System.Collections.Generic;
using UnityEngine;

namespace DT.Entities
{
    /// <summary>
    /// 1エンティティの移動軌跡を LineRenderer で描く。
    /// raw (lat,lon,timestamp) を保持し、world変換は外部 resolver に委ねる(再キャリブレーション対応)。
    /// 直近 WindowMs かつ 最大 MaxPoints でトリム。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class EntityTrail : MonoBehaviour
    {
        public const long WindowMs = 5 * 60 * 1000; // 5分
        public const int MaxPoints = 200;

        struct P { public double lat; public double lon; public long t; }
        readonly List<P> points = new();

        LineRenderer lr;
        Color baseColor = Color.gray;
        bool visible = true;
        float intensity = 1f; // 1=通常, <1=減光(他を強調時)

        Func<double, double, Vector3> resolver;

        void Awake()
        {
            lr = GetComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.4f;
            lr.numCornerVertices = 2;
            lr.positionCount = 0;
            // Built-in RP: URP不在時の null Material を回避(EntityManager の方式に一致)
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            lr.material = new Material(shader);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        /// <summary>初期化。色とworld変換resolverを与える。</summary>
        public void Init(Color color, Func<double, double, Vector3> worldResolver)
        {
            baseColor = color;
            resolver = worldResolver;
        }

        /// <summary>API初期ロード等で点列を一括設定(timestamp昇順前提)。</summary>
        public void SetPoints(IEnumerable<(double lat, double lon, long t)> pts)
        {
            points.Clear();
            foreach (var p in pts) points.Add(new P { lat = p.lat, lon = p.lon, t = p.t });
            Trim();
            Rebuild();
        }

        /// <summary>ライブ追従: 1点追加。</summary>
        public void Append(double lat, double lon, long timestamp)
        {
            points.Add(new P { lat = lat, lon = lon, t = timestamp });
            Trim();
            Rebuild();
        }

        /// <summary>再キャリブレーション時など、world座標を再計算。</summary>
        public void Rebuild()
        {
            // 時間窓トリム(古い側 = 末尾基準)も最新点基準で評価
            Trim();
            if (resolver == null || !visible || points.Count < 2)
            {
                lr.positionCount = 0;
                return;
            }
            lr.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 w = resolver(points[i].lat, points[i].lon);
                w.y = 0.3f; // 地表近くに描く
                lr.SetPosition(i, w);
            }
            ApplyGradient();
        }

        void Trim()
        {
            if (points.Count == 0) return;
            long newest = points[points.Count - 1].t;
            long cutoff = newest - WindowMs;
            // 時間窓外(古い点)を先頭から除去
            int drop = 0;
            while (drop < points.Count && points[drop].t < cutoff) drop++;
            if (drop > 0) points.RemoveRange(0, drop);
            // 点数上限: 古い側を切る
            if (points.Count > MaxPoints)
                points.RemoveRange(0, points.Count - MaxPoints);
        }

        void ApplyGradient()
        {
            Color c = baseColor * intensity;
            // 先頭(古)=透明寄り → 末尾(新)=濃いフェード
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.05f, 0f), new GradientAlphaKey(0.9f, 1f) });
            lr.colorGradient = grad;
        }

        /// <summary>種別トグル等での表示ON/OFF。</summary>
        public void SetVisible(bool v)
        {
            visible = v;
            Rebuild();
        }

        /// <summary>強調/減光。highlighted=null は通常(全体表示)、true=強調、false=減光。</summary>
        public void SetIntensity(float value)
        {
            intensity = value;
            if (visible) ApplyGradient();
        }
    }
}
