using UnityEngine;

namespace DT.UI
{
    /// <summary>
    /// CJK対応フォントの共有ローダー。
    /// IMGUI標準フォント(Arial)はCJKグリフを持たず日本語が□表示になるため、
    /// Resources/NotoSansJP.ttf (SIL OFL) を動的フォントとして全IMGUIで共有する。
    /// WebGLでもTTFアセット同梱でグリフが実行時ラスタライズされる。
    /// </summary>
    public static class DTFonts
    {
        static Font _japanese;
        static bool _attempted;

        public static Font Japanese
        {
            get
            {
                if (!_attempted)
                {
                    _attempted = true;
                    _japanese = Resources.Load<Font>("NotoSansJP");
                    if (_japanese == null)
                        Debug.LogWarning("[DTFonts] NotoSansJP not found in Resources");
                }
                return _japanese;
            }
        }

        /// <summary>GUIStyleにCJKフォントを適用する (nullなら無変更)</summary>
        public static void Apply(GUIStyle style)
        {
            var f = Japanese;
            if (f != null && style != null) style.font = f;
        }
    }
}
