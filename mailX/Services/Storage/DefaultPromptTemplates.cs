using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using mailX.Data;
using mailX.Models;

namespace mailX.Services.Storage;

/// <summary>
/// 기본 프롬프트 템플릿 정의 - 15개 기본 프롬프트
/// </summary>
public static class DefaultPromptTemplates
{
    private static readonly ILogger _logger = Log.ForContext(typeof(DefaultPromptTemplates));

    /// <summary>
    /// 1. 이메일 비서 페르소나 (global)
    /// </summary>
    public static readonly Prompt Persona = new()
    {
        PromptKey = "persona",
        Category = "global",
        Name = "이메일 비서 페르소나",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"user_name\", \"company_name\"]",
        Template = @"당신은 {{user_name}}님의 전문 이메일 비서입니다.
{{company_name}}에서 근무하는 {{user_name}}님의 업무 이메일을 분석하고 관리하는 역할을 수행합니다.

핵심 역할:
1. 이메일 내용을 정확하게 분석하고 요약
2. 중요도와 긴급도를 객관적으로 평가
3. 마감일, 계약 정보, TODO 항목을 빠짐없이 추출
4. 비즈니스 맥락을 이해하고 적절한 조언 제공

응답 원칙:
- 간결하고 명확하게 핵심만 전달
- 불확실한 정보는 추측하지 않음
- 한국어로 응답 (기술 용어는 영어 병기 가능)
- JSON 형식 요청 시 유효한 JSON만 반환"
    };

    /// <summary>
    /// 2. 한줄 요약 (analysis)
    /// </summary>
    public static readonly Prompt SummaryOneline = new()
    {
        PromptKey = "summary_oneline",
        Category = "analysis",
        Name = "한줄 요약 (30-50자)",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"from\"]",
        Template = @"다음 이메일을 30-50자 이내의 한 문장으로 요약하세요.

발신자: {{from}}
제목: {{subject}}
본문:
{{body}}

요약 규칙:
- 핵심 내용만 포함
- 완전한 문장으로 작성
- 이모지 사용 금지
- 30-50자 엄수

한줄 요약:"
    };

    /// <summary>
    /// 3. 상세 요약 (analysis)
    /// </summary>
    public static readonly Prompt SummaryDetail = new()
    {
        PromptKey = "summary_detail",
        Category = "analysis",
        Name = "상세 요약",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"from\", \"to\", \"cc\", \"attachments\"]",
        Template = @"다음 이메일을 상세하게 분석하고 요약하세요.

발신자: {{from}}
수신자: {{to}}
참조: {{cc}}
제목: {{subject}}
첨부파일: {{attachments}}

본문:
{{body}}

다음 형식으로 응답하세요:

## 요약
(3-5문장으로 핵심 내용 요약)

## 주요 포인트
- (핵심 포인트 1)
- (핵심 포인트 2)
- (핵심 포인트 3)

## 요청 사항
(발신자가 요청한 사항이 있다면 나열, 없으면 ""없음"")

## 후속 조치 필요 여부
(예/아니오 + 간단한 설명)"
    };

    /// <summary>
    /// 4. 스레드 전체 요약 (analysis)
    /// </summary>
    public static readonly Prompt SummaryGroup = new()
    {
        PromptKey = "summary_group",
        Category = "analysis",
        Name = "스레드 전체 요약",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"thread_emails\", \"participant_count\", \"email_count\"]",
        Template = @"다음 이메일 스레드 전체를 분석하고 요약하세요.

참여자 수: {{participant_count}}명
이메일 수: {{email_count}}건

스레드 내용:
{{thread_emails}}

다음 형식으로 응답하세요:

## 스레드 개요
(전체 대화의 주제와 맥락 설명, 2-3문장)

## 대화 흐름
1. (첫 번째 주요 논의점)
2. (두 번째 주요 논의점)
3. (세 번째 주요 논의점)

## 현재 상태
(대화가 어떤 상태인지: 결론 도출됨/논의 중/응답 대기 중 등)

## 미해결 사항
(아직 해결되지 않은 사항 나열, 없으면 ""없음"")"
    };

    /// <summary>
    /// 5. 마감일 추출 (extraction)
    /// </summary>
    public static readonly Prompt Deadline = new()
    {
        PromptKey = "deadline",
        Category = "extraction",
        Name = "마감일 추출",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"received_date\"]",
        Template = @"다음 이메일에서 마감일/기한 정보를 추출하세요.

수신일: {{received_date}}
제목: {{subject}}
본문:
{{body}}

JSON 형식으로 응답하세요:
{
  ""has_deadline"": true/false,
  ""deadlines"": [
    {
      ""date"": ""YYYY-MM-DD"",
      ""time"": ""HH:MM"" 또는 null,
      ""description"": ""마감 내용"",
      ""confidence"": ""high""/""medium""/""low""
    }
  ],
  ""relative_expressions"": [""이번 주 금요일까지"", ""내일 오전 중"" 등]
}

참고:
- 명확한 날짜만 ""high"" confidence
- ""곧"", ""빠른 시일 내"" 등 모호한 표현은 ""low"" confidence
- 마감일이 없으면 has_deadline: false, deadlines: []"
    };

    /// <summary>
    /// 6. 중요도 분석 (analysis)
    /// </summary>
    public static readonly Prompt Importance = new()
    {
        PromptKey = "importance",
        Category = "analysis",
        Name = "중요도 분석 (100점 만점)",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"from\", \"to\", \"cc\", \"my_email\"]",
        Template = @"다음 이메일의 중요도를 분석하세요.

내 이메일: {{my_email}}
발신자: {{from}}
수신자: {{to}}
참조: {{cc}}
제목: {{subject}}

본문:
{{body}}

5가지 항목 각 20점씩, 총 100점 만점으로 평가하세요:

1. 발신자 중요도 (20점)
   - 상사/임원: 20점, 동료: 10-15점, 외부: 상황에 따라

2. 직접 수신 여부 (20점)
   - To에 포함: 20점, CC에만 포함: 10점, 없음: 0점

3. 업무 영향도 (20점)
   - 프로젝트/계약/마감 관련: 높음

4. 긴급성 표현 (20점)
   - ""긴급"", ""ASAP"", ""오늘 중"" 등 표현 여부

5. 응답 필요성 (20점)
   - 질문, 요청, 승인 필요 등

JSON 형식으로 응답:
{
  ""sender_score"": 0-20,
  ""recipient_score"": 0-20,
  ""impact_score"": 0-20,
  ""urgency_score"": 0-20,
  ""response_score"": 0-20,
  ""total_score"": 0-100,
  ""level"": ""critical""/""high""/""medium""/""low"",
  ""reason"": ""평가 근거 요약""
}"
    };

    /// <summary>
    /// 7. 긴급도 분석 (analysis)
    /// </summary>
    public static readonly Prompt Urgency = new()
    {
        PromptKey = "urgency",
        Category = "analysis",
        Name = "긴급도 분석 (D-day 5단계)",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"received_date\", \"current_date\"]",
        Template = @"다음 이메일의 긴급도를 D-day 기반으로 분석하세요.

수신일: {{received_date}}
현재일: {{current_date}}
제목: {{subject}}

본문:
{{body}}

5단계 긴급도 기준:
- Level 5 (Critical): 오늘 중, 지금 당장, 즉시
- Level 4 (High): D-1 ~ D-3, 이번 주 내
- Level 3 (Medium): D-4 ~ D-7, 다음 주
- Level 2 (Low): D-8 ~ D-14, 이번 달 내
- Level 1 (Minimal): D-15+, 기한 없음

JSON 형식으로 응답:
{
  ""urgency_level"": 1-5,
  ""urgency_label"": ""critical""/""high""/""medium""/""low""/""minimal"",
  ""deadline_detected"": ""YYYY-MM-DD"" 또는 null,
  ""days_remaining"": 숫자 또는 null,
  ""urgency_keywords"": [""긴급"", ""ASAP"" 등 발견된 키워드],
  ""reason"": ""판단 근거""
}"
    };

    /// <summary>
    /// 8. 계약정보 추출 (extraction)
    /// </summary>
    public static readonly Prompt ContractInfo = new()
    {
        PromptKey = "contract_info",
        Category = "extraction",
        Name = "계약정보 추출 (7가지)",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"attachments\"]",
        Template = @"다음 이메일에서 계약 관련 정보 7가지를 추출하세요.

제목: {{subject}}
첨부파일: {{attachments}}

본문:
{{body}}

추출할 7가지 정보:
1. 계약명/프로젝트명
2. 계약 금액
3. 계약 기간 (시작일 ~ 종료일)
4. 계약 상대방 (회사/담당자)
5. 계약 유형 (신규/갱신/변경/해지)
6. 주요 조건/특이사항
7. 다음 단계/필요 조치

JSON 형식으로 응답:
{
  ""has_contract_info"": true/false,
  ""contract_name"": ""계약명"" 또는 null,
  ""amount"": ""금액 (통화 포함)"" 또는 null,
  ""period"": {
    ""start_date"": ""YYYY-MM-DD"" 또는 null,
    ""end_date"": ""YYYY-MM-DD"" 또는 null,
    ""duration"": ""기간 설명"" 또는 null
  },
  ""counterparty"": {
    ""company"": ""회사명"" 또는 null,
    ""contact"": ""담당자"" 또는 null
  },
  ""contract_type"": ""신규""/""갱신""/""변경""/""해지"" 또는 null,
  ""conditions"": [""조건1"", ""조건2""],
  ""next_steps"": [""다음 단계1"", ""다음 단계2""]
}"
    };

    /// <summary>
    /// 9. TODO 추출 (extraction)
    /// </summary>
    public static readonly Prompt Todo = new()
    {
        PromptKey = "todo",
        Category = "extraction",
        Name = "TODO 항목 추출",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"from\", \"my_email\"]",
        Template = @"다음 이메일에서 나({{my_email}})가 해야 할 TODO 항목을 추출하세요.

발신자: {{from}}
제목: {{subject}}

본문:
{{body}}

추출 기준:
- 나에게 직접 요청된 작업
- 나의 응답/확인이 필요한 항목
- 나의 참석/참여가 필요한 일정
- 나의 승인/결재가 필요한 건

JSON 형식으로 응답:
{
  ""has_todos"": true/false,
  ""todos"": [
    {
      ""title"": ""할 일 제목 (20자 이내)"",
      ""description"": ""상세 설명"",
      ""deadline"": ""YYYY-MM-DD"" 또는 null,
      ""priority"": ""high""/""medium""/""low"",
      ""type"": ""reply""/""action""/""meeting""/""approval""/""review""
    }
  ],
  ""total_count"": 숫자
}"
    };

    /// <summary>
    /// 10. 키워드 추출 (extraction)
    /// </summary>
    public static readonly Prompt Keywords = new()
    {
        PromptKey = "keywords",
        Category = "extraction",
        Name = "키워드 추출 (5개 카테고리)",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\"]",
        Template = @"다음 이메일에서 5개 카테고리별 키워드를 추출하세요.

제목: {{subject}}

본문:
{{body}}

5개 카테고리:
1. 주제 키워드 (이메일의 주요 주제)
2. 인물/조직 (언급된 사람, 회사, 부서)
3. 프로젝트/업무 (관련 프로젝트, 업무명)
4. 날짜/일정 (언급된 날짜, 기한)
5. 액션 아이템 (요청된 행동)

JSON 형식으로 응답:
{
  ""topic"": [""키워드1"", ""키워드2""],
  ""people_org"": [""이름/조직""],
  ""project_work"": [""프로젝트명""],
  ""dates"": [""날짜/일정""],
  ""actions"": [""액션""],
  ""all_keywords"": [""전체 키워드 합집합""]
}"
    };

    /// <summary>
    /// 11. 비업무 메일 필터 (analysis)
    /// </summary>
    public static readonly Prompt IsNonBusiness = new()
    {
        PromptKey = "is_non_business",
        Category = "analysis",
        Name = "광고/스팸 필터",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"subject\", \"body\", \"from\"]",
        Template = @"다음 이메일이 업무와 관련 없는 메일인지 판단하세요.

발신자: {{from}}
제목: {{subject}}

본문:
{{body}}

비업무 메일 유형:
1. 광고/마케팅 (프로모션, 할인, 신제품 안내)
2. 뉴스레터 (정기 발송 뉴스, 매거진)
3. 스팸 (피싱, 사기, 불필요한 메일)
4. 알림 (자동 발송 시스템 알림)
5. 개인 (업무와 무관한 개인 메일)

JSON 형식으로 응답:
{
  ""is_non_business"": true/false,
  ""category"": ""advertisement""/""newsletter""/""spam""/""notification""/""personal""/""business"",
  ""confidence"": ""high""/""medium""/""low"",
  ""reason"": ""판단 근거"",
  ""safe_to_ignore"": true/false
}"
    };

    /// <summary>
    /// 12. 수신 위치 분석 (analysis)
    /// </summary>
    public static readonly Prompt MyPosition = new()
    {
        PromptKey = "my_position",
        Category = "analysis",
        Name = "수신 위치 분석 (To/CC/BCC)",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"from\", \"to\", \"cc\", \"bcc\", \"my_email\"]",
        Template = @"다음 이메일에서 나({{my_email}})의 수신 위치를 분석하세요.

발신자: {{from}}
수신자 (To): {{to}}
참조 (CC): {{cc}}
숨은참조 (BCC): {{bcc}}

판단 기준:
- sender: 내가 보낸 메일
- to_direct: To에 나만 포함
- to_group: To에 나 포함 (여러 명)
- cc: CC에만 포함
- bcc: BCC에만 포함
- mentioned: 본문에서 언급됨
- none: 수신자 목록에 없음

JSON 형식으로 응답:
{
  ""position"": ""sender""/""to_direct""/""to_group""/""cc""/""bcc""/""mentioned""/""none"",
  ""is_primary_recipient"": true/false,
  ""requires_action"": true/false,
  ""response_expected"": true/false,
  ""reason"": ""판단 근거""
}"
    };

    /// <summary>
    /// 13. 첨부파일 분류 (analysis)
    /// </summary>
    public static readonly Prompt HasAttachments = new()
    {
        PromptKey = "has_attachments",
        Category = "analysis",
        Name = "첨부파일 분류",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"attachments\", \"subject\", \"body\"]",
        Template = @"다음 이메일의 첨부파일을 분류하세요.

제목: {{subject}}
첨부파일 목록:
{{attachments}}

본문:
{{body}}

파일 유형별 분류:
- document: 문서 (docx, pdf, hwp, txt)
- spreadsheet: 스프레드시트 (xlsx, csv)
- presentation: 프레젠테이션 (pptx)
- image: 이미지 (jpg, png, gif)
- archive: 압축 (zip, rar)
- code: 코드 (js, py, cs)
- other: 기타

JSON 형식으로 응답:
{
  ""has_attachments"": true/false,
  ""total_count"": 숫자,
  ""files"": [
    {
      ""name"": ""파일명.확장자"",
      ""type"": ""document""/""spreadsheet""/...,
      ""size"": ""크기"" 또는 null,
      ""purpose"": ""본문에서 언급된 용도"" 또는 null
    }
  ],
  ""needs_review"": true/false,
  ""summary"": ""첨부파일 요약 설명""
}"
    };

    /// <summary>
    /// 14. 그룹 분석 (analysis)
    /// </summary>
    public static readonly Prompt GroupAnalysis = new()
    {
        PromptKey = "group_analysis",
        Category = "analysis",
        Name = "그룹 상태/키워드 분석",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"thread_emails\", \"participants\", \"date_range\"]",
        Template = @"다음 이메일 그룹(스레드)을 분석하세요.

참여자 목록:
{{participants}}

기간: {{date_range}}

이메일 내용:
{{thread_emails}}

분석 항목:
1. 대화 상태 (진행중/완료/대기중/보류)
2. 주요 키워드
3. 참여자별 역할
4. 감정/톤 분석

JSON 형식으로 응답:
{
  ""status"": ""in_progress""/""completed""/""waiting""/""on_hold"",
  ""status_reason"": ""상태 판단 근거"",
  ""keywords"": {
    ""primary"": [""주요 키워드""],
    ""secondary"": [""부가 키워드""]
  },
  ""participants"": [
    {
      ""email"": ""이메일 주소"",
      ""role"": ""initiator""/""responder""/""approver""/""observer"",
      ""message_count"": 숫자
    }
  ],
  ""tone"": ""formal""/""informal""/""urgent""/""neutral"",
  ""sentiment"": ""positive""/""negative""/""neutral"",
  ""next_action"": ""예상되는 다음 단계""
}"
    };

    /// <summary>
    /// 15. 첨부 문서 분석 (extraction)
    /// </summary>
    public static readonly Prompt AttachmentAnalysis = new()
    {
        PromptKey = "attachment_analysis",
        Category = "extraction",
        Name = "첨부 문서 분석",
        IsSystem = true,
        IsEnabled = true,
        Variables = "[\"attachment_name\", \"attachment_content\", \"email_subject\", \"email_body\"]",
        Template = @"다음 첨부 문서의 내용을 분석하세요.

이메일 제목: {{email_subject}}
이메일 본문 요약:
{{email_body}}

첨부파일명: {{attachment_name}}
첨부파일 내용:
{{attachment_content}}

분석 항목:
1. 문서 유형 및 목적
2. 핵심 내용 요약
3. 중요 수치/데이터
4. 주의 사항/리스크
5. 필요 조치 사항

JSON 형식으로 응답:
{
  ""document_type"": ""계약서""/""보고서""/""제안서""/""견적서""/""기타"",
  ""purpose"": ""문서의 목적"",
  ""summary"": ""핵심 내용 3-5문장 요약"",
  ""key_data"": [
    {
      ""label"": ""항목명"",
      ""value"": ""값"",
      ""importance"": ""high""/""medium""/""low""
    }
  ],
  ""risks"": [""주의사항1"", ""주의사항2""],
  ""action_items"": [""필요조치1"", ""필요조치2""],
  ""related_to_email"": true/false,
  ""requires_signature"": true/false
}"
    };

    /// <summary>
    /// 모든 기본 프롬프트 목록 반환
    /// </summary>
    /// <returns>15개 기본 프롬프트 목록</returns>
    public static List<Prompt> GetAllDefaults()
    {
        return new List<Prompt>
        {
            Persona,           // 1. global
            SummaryOneline,    // 2. analysis
            SummaryDetail,     // 3. analysis
            SummaryGroup,      // 4. analysis
            Deadline,          // 5. extraction
            Importance,        // 6. analysis
            Urgency,           // 7. analysis
            ContractInfo,      // 8. extraction
            Todo,              // 9. extraction
            Keywords,          // 10. extraction
            IsNonBusiness,     // 11. analysis
            MyPosition,        // 12. analysis
            HasAttachments,    // 13. analysis
            GroupAnalysis,     // 14. analysis
            AttachmentAnalysis // 15. extraction
        };
    }

    /// <summary>
    /// DB에 기본 프롬프트 시드 (없는 것만 삽입)
    /// </summary>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <returns>삽입된 프롬프트 수</returns>
    public static async Task<int> SeedDatabaseAsync(MailXDbContext dbContext)
    {
        if (dbContext == null)
            throw new ArgumentNullException(nameof(dbContext));

        var defaults = GetAllDefaults();
        var insertedCount = 0;

        foreach (var prompt in defaults)
        {
            // 이미 존재하는지 확인
            var exists = await dbContext.Prompts
                .AnyAsync(p => p.PromptKey == prompt.PromptKey);

            if (!exists)
            {
                // ID 초기화 (새로 삽입되도록)
                prompt.Id = 0;
                await dbContext.Prompts.AddAsync(prompt);
                insertedCount++;
                _logger.Information("기본 프롬프트 추가: {Key}", prompt.PromptKey);
            }
            else
            {
                _logger.Debug("기본 프롬프트 이미 존재: {Key}", prompt.PromptKey);
            }
        }

        if (insertedCount > 0)
        {
            await dbContext.SaveChangesAsync();
            _logger.Information("기본 프롬프트 시드 완료: {Count}개 추가", insertedCount);
        }
        else
        {
            _logger.Information("기본 프롬프트 시드: 추가할 항목 없음 (모두 존재)");
        }

        return insertedCount;
    }

    /// <summary>
    /// 카테고리별 기본 프롬프트 목록 반환
    /// </summary>
    /// <param name="category">카테고리</param>
    /// <returns>해당 카테고리의 프롬프트 목록</returns>
    public static List<Prompt> GetDefaultsByCategory(string category)
    {
        return GetAllDefaults()
            .Where(p => p.Category == category)
            .ToList();
    }

    /// <summary>
    /// 프롬프트 키로 기본 프롬프트 조회
    /// </summary>
    /// <param name="promptKey">프롬프트 키</param>
    /// <returns>프롬프트 또는 null</returns>
    public static Prompt? GetDefaultByKey(string promptKey)
    {
        return GetAllDefaults()
            .FirstOrDefault(p => p.PromptKey == promptKey);
    }
}
