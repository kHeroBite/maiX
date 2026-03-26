using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;
using mAIx.Models;

namespace mAIx.Services.Analysis;

/// <summary>
/// 계약 정보 추출기 - AI 응답에서 7가지 계약 정보 파싱
/// 1. 계약명
/// 2. 계약금액
/// 3. 계약기간
/// 4. 계약상대방
/// 5. 담당자
/// 6. 마감일
/// 7. 특이사항
/// </summary>
public class ContractExtractor
{
    private readonly ILogger _logger;

    // 금액 파싱용 정규식
    private static readonly Regex AmountPattern = new(
        @"(\d{1,3}(?:,\d{3})*(?:\.\d+)?)\s*(원|만원|억|백만원|천만원)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // MM 파싱용 정규식
    private static readonly Regex ManMonthPattern = new(
        @"(\d+(?:\.\d+)?)\s*(MM|M\/M|명|개월|man-month)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ContractExtractor()
    {
        _logger = Log.ForContext<ContractExtractor>();
    }

    /// <summary>
    /// AI 응답에서 계약 정보 파싱
    /// </summary>
    /// <param name="response">AI 응답 (JSON 형식 기대)</param>
    /// <returns>계약 정보 또는 null (정보 없음)</returns>
    public ContractInfoResult? Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            // JSON 시작/끝 찾기 (AI가 추가 텍스트를 붙일 경우 대비)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                _logger.Debug("계약 정보 JSON 없음");
                return null;
            }

            var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var json = JsonDocument.Parse(jsonString);
            var root = json.RootElement;

            // 계약 정보 존재 여부 확인
            if (root.TryGetProperty("has_contract", out var hasContract))
            {
                if (!hasContract.GetBoolean())
                {
                    _logger.Debug("이메일에 계약 정보 없음");
                    return null;
                }
            }

            var result = new ContractInfoResult();

            // 1. 계약명
            if (root.TryGetProperty("contract_name", out var contractName))
            {
                result.ContractName = contractName.GetString();
            }

            // 2. 계약금액
            if (root.TryGetProperty("amount", out var amount))
            {
                var amountStr = amount.ValueKind == JsonValueKind.String
                    ? amount.GetString()
                    : amount.ToString();

                result.AmountText = amountStr;
                result.Amount = ParseAmount(amountStr);
            }

            // 3. 계약기간
            if (root.TryGetProperty("period", out var period))
            {
                result.Period = period.GetString();
            }

            // 4. 계약상대방
            if (root.TryGetProperty("counter_party", out var counterParty))
            {
                result.CounterParty = counterParty.GetString();
            }

            // 5. 담당자
            if (root.TryGetProperty("contact_person", out var contactPerson))
            {
                result.ContactPerson = contactPerson.GetString();
            }

            // 6. 마감일
            if (root.TryGetProperty("deadline", out var deadline))
            {
                var deadlineStr = deadline.GetString();
                if (!string.IsNullOrEmpty(deadlineStr) &&
                    DateTime.TryParse(deadlineStr, out var deadlineDate))
                {
                    result.Deadline = deadlineDate;
                }
            }

            // 7. 특이사항
            if (root.TryGetProperty("notes", out var notes))
            {
                result.Notes = notes.GetString();
            }

            // 추가 필드: 투입 공수
            if (root.TryGetProperty("man_month", out var manMonth))
            {
                var mmStr = manMonth.ValueKind == JsonValueKind.String
                    ? manMonth.GetString()
                    : manMonth.ToString();

                result.ManMonth = ParseManMonth(mmStr);
            }

            // 추가 필드: 근무 위치
            if (root.TryGetProperty("location", out var location))
            {
                result.Location = location.GetString();
            }

            // 추가 필드: 원격근무 가능 여부
            if (root.TryGetProperty("is_remote", out var isRemote))
            {
                result.IsRemote = isRemote.ValueKind == JsonValueKind.True ||
                    (isRemote.ValueKind == JsonValueKind.String &&
                     isRemote.GetString()?.Contains("가능", StringComparison.OrdinalIgnoreCase) == true);
            }

            // 추가 필드: 업무 범위
            if (root.TryGetProperty("scope", out var scope))
            {
                result.Scope = scope.GetString();
            }

            // 추가 필드: 사업 유형
            if (root.TryGetProperty("business_type", out var businessType))
            {
                result.BusinessType = businessType.GetString();
            }

            // 최소 하나의 필수 정보가 있어야 유효한 계약 정보
            if (string.IsNullOrEmpty(result.ContractName) &&
                result.Amount == null &&
                string.IsNullOrEmpty(result.Period) &&
                string.IsNullOrEmpty(result.CounterParty))
            {
                _logger.Debug("계약 정보가 불충분함");
                return null;
            }

            _logger.Information("계약 정보 추출 완료: {Name}, 금액: {Amount}",
                result.ContractName ?? "미상", result.AmountText ?? "미상");

            return result;
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "계약 정보 JSON 파싱 실패: {Response}", response);
            return null;
        }
    }

    /// <summary>
    /// 금액 문자열 파싱 (원 단위로 변환)
    /// </summary>
    private decimal? ParseAmount(string? amountText)
    {
        if (string.IsNullOrWhiteSpace(amountText))
            return null;

        // 숫자와 단위 추출
        var match = AmountPattern.Match(amountText);
        if (!match.Success)
            return null;

        var numberStr = match.Groups[1].Value.Replace(",", "");
        var unit = match.Groups[2].Value.ToLower();

        if (!decimal.TryParse(numberStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return null;

        // 단위 변환
        return unit switch
        {
            "억" => number * 100_000_000m,
            "천만원" => number * 10_000_000m,
            "백만원" => number * 1_000_000m,
            "만원" => number * 10_000m,
            "원" => number,
            "" => number >= 10000 ? number : number * 10000, // 단위 없으면 만원으로 추정
            _ => number
        };
    }

    /// <summary>
    /// MM(Man-Month) 파싱
    /// </summary>
    private decimal? ParseManMonth(string? mmText)
    {
        if (string.IsNullOrWhiteSpace(mmText))
            return null;

        var match = ManMonthPattern.Match(mmText);
        if (match.Success)
        {
            if (decimal.TryParse(match.Groups[1].Value, out var mm))
            {
                return mm;
            }
        }

        // 직접 숫자 파싱 시도
        if (decimal.TryParse(mmText, out var directMm))
        {
            return directMm;
        }

        return null;
    }

    /// <summary>
    /// ContractInfoResult를 ContractInfo 엔티티로 변환
    /// </summary>
    public ContractInfo ToEntity(ContractInfoResult result, int emailId)
    {
        return new ContractInfo
        {
            EmailId = emailId,
            Amount = result.Amount,
            Period = result.Period,
            ManMonth = result.ManMonth,
            Location = result.Location,
            IsRemote = result.IsRemote,
            Scope = result.Scope ?? result.Notes,
            BusinessType = result.BusinessType
        };
    }
}
