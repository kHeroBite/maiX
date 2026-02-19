---
name: kRules_maix
description: "MaiX 프로젝트 코딩 규칙. kO에서 참조 안내."
---
# kRules_maix — MaiX 프로젝트 코딩 규칙

## WPF MVVM 패턴 규칙

**절대 원칙**: MVVM 패턴 준수. View(XAML) ↔ ViewModel(바인딩) ↔ Model(데이터) 분리.

```yaml
필수:
  - ViewModel은 ViewModelBase 상속 (CommunityToolkit.Mvvm)
  - [ObservableProperty], [RelayCommand] 어트리뷰트 사용
  - View에서 ViewModel 직접 참조 금지 (DataContext 바인딩만)
  - 코드비하인드(.xaml.cs)에 비즈니스 로직 최소화

금지:
  - View에서 직접 서비스 호출
  - ViewModel에서 View 컨트롤 직접 접근
  - 코드비하인드에 DB 쿼리/API 호출
```

## DI (의존성 주입) 규칙

```yaml
필수:
  - 모든 서비스는 App.xaml.cs의 ConfigureServices()에 등록
  - 생성자 주입 사용 (서비스 로케이터 패턴 금지)
  - Scoped 서비스는 CreateScope() 사용

금지:
  - new Service() 직접 생성 (DI 컨테이너 경유 필수)
  - 서비스 간 순환 참조
  - 싱글톤에서 Scoped 서비스 직접 주입
```

## XAML 바인딩 규칙

```yaml
필수:
  - Binding Path 명시 (축약 금지)
  - Converter는 Converters/ 폴더에 별도 파일로 분리
  - ResourceDictionary로 스타일 재사용
  - WPF UI (Fluent Design) 컨트롤 우선 사용

네이밍:
  - Converter: {용도}Converter.cs (예: BoolToVisibilityConverter)
  - View: {기능}Window.xaml 또는 {기능}Page.xaml
  - ViewModel: {기능}ViewModel.cs
  - Model: {엔티티명}.cs
```

## 네임스페이스 규칙

```yaml
루트: MaiX
구조:
  - MaiX.Views: WPF Window/Page
  - MaiX.Views.Dialogs: 다이얼로그 창
  - MaiX.ViewModels: MVVM ViewModel
  - MaiX.Models: 데이터 모델
  - MaiX.Models.Settings: 설정 모델
  - MaiX.Services.{카테고리}: 비즈니스 로직
  - MaiX.Data: EF Core DbContext
  - MaiX.Converters: XAML 값 변환기
  - MaiX.Controls: 커스텀 컨트롤
  - MaiX.Utils: 유틸리티
```

## SQLite / EF Core 규칙

```yaml
필수:
  - DbContext는 MaiXDbContext 사용
  - 마이그레이션: dotnet ef migrations add {Name}
  - 스키마 변경 시 DATABASE.md 업데이트
  - using 블록으로 DbContext 관리

금지:
  - 직접 SQL 실행 (EF Core LINQ 사용)
  - Migration 파일 수동 편집
  - DbContext를 싱글톤으로 등록
```

## Microsoft Graph API 규칙

```yaml
필수:
  - GraphAuthService를 통한 인증만 사용
  - TokenCacheHelper로 토큰 캐시 관리
  - 권한 변경 시: GraphAuthService.cs + appsettings.json + Azure Portal 동시 수정
  - Delta Query 사용 (전체 동기화 금지)

금지:
  - 직접 HTTP 호출 (Microsoft.Graph SDK 사용)
  - 토큰을 코드에 하드코딩
  - Rate Limit 무시 (재시도 로직 필수)
```

## 로깅 규칙

```yaml
필수:
  - Serilog + log4net 이중 로깅
  - 적절한 레벨: Debug/Info/Warn/Error/Fatal
  - 예외 처리 블록에 Error 로그
  - 클래스명/메서드명 포함

금지:
  - Console.WriteLine (Serilog/log4net 사용)
  - 민감 정보 로그 출력 (토큰, 비밀번호)
```

## AI 분석 규칙

```yaml
필수:
  - IAIProvider 인터페이스 구현
  - AIService를 통한 프로바이더 선택
  - 비동기 호출 (async/await)

금지:
  - AI API 키를 코드에 하드코딩 (설정 파일 사용)
  - 동기 API 호출 (UI 블로킹)
```

## 프롬프트 관리 규칙 (L-044 — 절대 규칙)

```yaml
원칙: 프롬프트는 반드시 외부 파일로 관리. 코드 내 하드코딩/DB 조회 절대 금지.

필수:
  - 프롬프트 템플릿은 Resources/Prompts/*.txt 외부 파일로 관리
  - .csproj에 Content + CopyToOutputDirectory=PreserveNewest 설정
  - 변수 치환은 {{변수명}} 플레이스홀더 + string.Replace() 사용
  - 프롬프트 수정 시 .txt 파일만 수정 (C# 코드 변경 불필요)

금지:
  - C# 코드에 프롬프트 문자열 하드코딩 (raw string literal, $""" 등 포함)
  - PromptService/DB를 통한 프롬프트 조회 (DB 우선 시 외부 파일 무력화)
  - Fallback 하드코딩 ("보험"이라는 명목도 금지)
  - 프롬프트 내용을 코드 변경 없이 수정 불가능한 구조

근거:
  - L-044: DB 프롬프트가 Fallback 하드코딩을 무력화하여 2차례 수정이 무효화됨
  - 프롬프트는 설정(configuration)이지 코드(code)가 아님
  - 외부 파일 = 배포 포함 + 코드 변경 없이 프롬프트 수정 가능
```

## REST API 서버 규칙

```yaml
포트: 5858 (고정)
필수:
  - RestApiServer.cs에서 엔드포인트 관리
  - JSON 응답 형식
  - 에러 시 적절한 HTTP 상태 코드 반환
  - restapi.md 문서 업데이트

금지:
  - 5858 포트 변경 (자동화 스크립트 의존)
  - 인증 없는 위험한 엔드포인트 추가
```
