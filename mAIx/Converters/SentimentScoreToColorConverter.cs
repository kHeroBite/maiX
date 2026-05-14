// 감성 점수(0~100) → 그라데이션 색상 변환 (빨강 → 회색 → 녹색)
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using mAIx.Models;

namespace mAIx.Converters;

/// <summary>
/// <see cref="SentimentResult.Score"/>(0~100) 또는 정수 점수를 그라데이션 SolidColorBrush로 변환.
/// 0(매우 부정) → #FFEBEE(연빨강), 50(중립) → #F5F5F5(회색), 100(매우 긍정) → #E8F5E9(연녹색).
/// null/잘못된 입력 → 회색 폴백.
/// </summary>
public class SentimentScoreToColorConverter : IValueConverter
{
    // 부정(0)~중립(50)~긍정(100) 그라데이션 기준 색상 (HSL 보간 대신 단순 선형 RGB 보간 — 시각적 충분)
    // #FFEBEE = (255, 235, 238)
    // #F5F5F5 = (245, 245, 245)
    // #E8F5E9 = (232, 245, 233)
    private static readonly Color NegativeColor = Color.FromRgb(0xFF, 0xEB, 0xEE);
    private static readonly Color NeutralColor = Color.FromRgb(0xF5, 0xF5, 0xF5);
    private static readonly Color PositiveColor = Color.FromRgb(0xE8, 0xF5, 0xE9);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // null / Sentiment 미분석 → 회색 폴백
        if (value is null)
            return new SolidColorBrush(NeutralColor);

        int score;
        switch (value)
        {
            case int i:
                score = i;
                break;
            case SentimentResult sr:
                score = sr.Score;
                break;
            default:
                if (!int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out score))
                    return new SolidColorBrush(NeutralColor);
                break;
        }

        // 0~100 범위 클램프
        if (score < 0) score = 0;
        if (score > 100) score = 100;

        Color color;
        if (score <= 50)
        {
            // 0~50: NegativeColor → NeutralColor 보간 (t = score/50)
            var t = score / 50.0;
            color = Lerp(NegativeColor, NeutralColor, t);
        }
        else
        {
            // 50~100: NeutralColor → PositiveColor 보간 (t = (score-50)/50)
            var t = (score - 50) / 50.0;
            color = Lerp(NeutralColor, PositiveColor, t);
        }

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("SentimentScoreToColorConverter는 단방향 변환만 지원합니다.");

    private static Color Lerp(Color a, Color b, double t)
    {
        byte R = (byte)(a.R + (b.R - a.R) * t);
        byte G = (byte)(a.G + (b.G - a.G) * t);
        byte B = (byte)(a.B + (b.B - a.B) * t);
        return Color.FromRgb(R, G, B);
    }
}
