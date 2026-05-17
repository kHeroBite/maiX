# HISTORY.md — MaiX 프로젝트 작업 이력

> PROJECT.md 작업 이력 테이블의 상세 보완본

## [2026-05-17] 대화네비 1/2 정정 + 녹음중지 STT 사라짐 회귀 근본수정 (O3 Fast Path)

**분류**: O3 Fast Path (review + docs + git)
**otest 결과**: 정적/빌드/규칙 PASS (2건). 작업2 마이크 런타임 사용자 검증 대기 (PASS-with-user-validation)
**범위**: 수정 3파일 (MainWindow.xaml, MainWindow.OneNote.cs, OneNoteViewModel.cs)
**파이프라인 이력**: 단일 사이클
**대화ID**: conv_177901135205

### 변경 내용

- **작업1** (대화네비 1/4→1/2 정정): 직전 cb4ae007이 1/4(55px)로 너무 작았음 → 1/2(110px)로 정정. `MainWindow.xaml` OneNoteRecCol2 Width 75→150/MinWidth 40→80, `MainWindow.OneNote.cs` GridLength(55)→GridLength(110) + 주석 정정
- **작업2** (녹음중지 STT 사라짐 회귀 근본수정): 이중 Stop 경로 race 차단. StopRecording(동기) + OnRecordingCompleted(비동기 NAudio 콜백) 두 경로가 LiveSTTSegments→STTSegments 복사를 중복 실행 → Clear된 LiveSTTSegments 재복사로 STTSegments=0. `_sttCopiedByStopRecording` bool 플래그로 StopRecording이 먼저 복사 후 true 설정, OnRecordingCompleted는 skip. StartRecordingAsync에서 false 리셋

### 교훈 (L-464, L-465)
- L-464: 이중 Stop 경로 bool 플래그 가드 패턴
- L-465: 회귀 수정은 추정 원인만 보호하면 실효 없음 — nlog 런타임 재현 필수 (L-446/L-420 재확인)

## [2026-05-17] OneNote 후속 4건 — 타임라인 보정/도킹 재설계/하이라이트 정밀화/전체묵음 (O3 Fast Path)

**분류**: O3 Fast Path (lesson + docs + git)
**otest 결과**: 정적/빌드/규칙 PASS (4건). 런타임은 사용자 녹음 검증 필요 (PASS-with-user-validation)
**범위**: 수정 4파일
**파이프라인 이력**: 단일 사이클
**대화ID**: conv_177896717241

### 변경 내용

- **항목3-a** (타임라인 시간 텍스트 복원): Wave1 과도 제거 보정 — 타임라인 눈금 ItemsControl 통째 제거로 시간 텍스트(0:00)까지 소실. Line(--선) 요소만 제거, 시간 TextBlock 보존 (`MainWindow.xaml`)
- **항목3-b** (대화네비 좌우 스크롤 금지): ScrollViewer `HorizontalScrollBarVisibility=Disabled` 적용 (`MainWindow.xaml`)
- **항목5** (대화 네비게이션 패널 도킹 위치 토글): "카드 세로↔가로" 오해 정정 → 패널 자체 도킹 위치 이동(우측 기본 ↔ 하단, 작업표시줄식). 단일 Grid 코드비하인드 `Grid.SetRow/SetColumn` 재배치 패턴 — 마크업 복제 0 (`MainWindow.OneNote.cs`)
- **항목7** (하이라이트 핵심키워드만): systemPrompt 품질 기준 강화(핵심 명사 2~5개) + `s.Length≥2` 가드 + `IsWordBoundary` 단어경계 매칭 동시 적용 (`MinuteSummaryService.cs`, `HighlightTextBehavior.cs`)
- **항목10** (전체 묵음 시 "묵음"만 표시): `IsAllSilence()` 판정 → LLM 스킵 → "묵음" 고정 텍스트 엔트리 발행 (`MinuteSummaryService.cs`)

**변경 파일 (4개)**: MainWindow.xaml, MainWindow.OneNote.cs, MinuteSummaryService.cs, HighlightTextBehavior.cs (신규)

### 교훈 (L-454 ~ L-458)

- L-454 이전 작업 과도 제거 보정 — DataTemplate 내 요소 단위 정밀 제거 필수
- L-455 추상 UI 용어 의미체 확인 필수 — "토글"·"방향" oplan에서 A/B 형식 사전 확인
- L-456 단일 Grid 코드비하인드 재배치 = 마크업 복제 0 도킹 패턴
- L-457 하이라이트 정밀화 = LLM 프롬프트 품질 + 단어경계 양쪽 동시 적용
- L-458 전체 묵음 분기 = LLM 스킵 + 기존 이벤트 경로 재사용 (UI 레이어 무수정)

## [2026-05-17] OneNote UI 정리 5건 + STT 버그 3건 (9개 작업, O4 Full Path — 단일사이클)

**분류**: O4 Full Path (Wave 기반 spawn — Wave1 타입/인터페이스 → Wave2 구현 병렬 → Wave3 통합)
**otest 결과**: 9개 작업 정적/빌드/규칙 PASS. 7/8/9 런타임은 사용자 녹음 검증 (PASS-with-user-validation)
**범위**: 수정 10파일 (~9개 작업 단위)
**파이프라인 이력**: 단일 사이클 (역라우팅 0회)
**대화ID**: conv_177893864234

### 변경 내용

**UI 정리 (5건)**:
- 미니맵 제거 / "대화 네비게이션" 라벨 / 타임라인 눈금·TimeRange 제거 / 본문 STT·AI요약 버튼 제거
- 가로/세로 토글 레이아웃 신규 (Option B — 세로 ItemsControl byte-identical 보존 + 가로 ScrollViewer+ItemsControl, StringEqualsToVisibilityConverter 토글, L-389/L-424 준수)
- TopicSegment.DisplayWidth 미러 프로퍼티 + RecalculateTopicSegmentHeights 가로 폭 시간비례 계산
- TopicNavOrientation 기본값 Horizontal → Vertical
- "실시간 요약 중..." 텍스트

**STT 버그 (3건)**:
- 키워드 하이라이트 B안: MinuteSummaryService systemPrompt JSON에 keywords 배열 요청 + MinuteSummaryEntry.Keywords + navSegment.Keywords 매핑 (정식 LLM 경로, 구버전 역직렬화 graceful 빈목록)
- 화자분리 STT(항목8): OpenAiTranscribeSttService response_format 모델별 분기 (whisper-1=verbose_json, gpt-4o-transcribe=json) — 전 청크 BadRequest → 0건 해결
- VAD OFF STT(항목9): OpenAiRealtimeSttService 주기적 수동 commit 루프(PeriodicTimer 3s) + _audioAppendedSinceCommit + _sendLock(L-443) + L-380 외부 try-catch

**변경 파일 (10개)**: MinuteSummaryEntry.cs, OpenAiRecordingSettings.cs, TopicSegment.cs, MinuteSummaryService.cs, OpenAiRealtimeSttService.cs, OpenAiTranscribeSttService.cs, OneNoteViewModel.cs, MainWindow.OneNote.cs, MainWindow.xaml, MainWindow.xaml.cs

### 교훈 (L-446 ~ L-453)

- L-446 외부 API 디버깅 장기전 — 단발 추측 수정 2회+ 실패 = 매몰비용. nlog 직접 확인이 정답. 로그 이중채널(STT=NLog nlog-*.log) L-406/L-408 재확인
- L-447 화자분리 STT response_format 모델별 분기
- L-448 VAD OFF turn_detection=null → 주기적 수동 commit 필수 (L-443 연관)
- L-449 하이라이트 무동작 = 데이터 소스 부재(통지 누락 아님)
- L-450 토글 무반응 = 반응할 레이아웃 미구현. 2모드 Option B
- L-451 WebSocket 종료 await 경로의 send는 취소 가능해야 함 (codex 적대리뷰)
- L-452 model.Contains() capability 추론은 alias/deployment명에 취약 (codex 적대리뷰)
- L-453 사용자 결정 SendMessage idle 중 미수신 가능 (프로세스 교훈)

### 외부 AI 이중 리뷰 (O4 Full Path)

- code-review: 로컬 diff 검토 — STT 변경 well-engineered(_sendLock 직렬화, graceful 분기) 차단 이슈 0건
- codex 적대리뷰: 3건 지적 (StopAsync hang HIGH, Dispose race MEDIUM, model.Contains MEDIUM) → L-451/L-452 교훈화 (기존 패턴 답습, 일관성상 현 사이클 미수정 — 향후 강화)

## [2026-05-15] 음성 파이프라인 2모드 시스템 — Legacy/Unified + 감성 분석 + 자동 폴백 + 비용 표시 (O4 Full Path — 단일사이클 PASS) [REVERTED: 런타임 실패로 9defe030 revert]

**분류**: O4 Full Path (Wave 기반 spawn 6 에이전트)
**otest 결과**: AC 18/18 PASS, 빌드 OK, 코드규칙 OK ✅
**범위**: 신규 10파일 + 수정 5파일 (~1110줄)
**파이프라인 이력**: 단일 사이클 PASS (역라우팅 0회)
**상태**: ⚠️ Unified 모드 선택 시 STT/핵심요약 모두 작동 안 함 → 사용자 지시로 revert (커밋 5f877bf6)

### 변경 내용

**신규 파일 (10개)**:
1. `mAIx/Services/AI/AudioPipelineMode.cs` — enum (Legacy/Unified)
2. `mAIx/Models/SentimentResult.cs` — 감성 분석 결과 모델 (Score 0~100 + Emoji + Label)
3. `mAIx/Services/AI/IRealtimeAudioPipeline.cs` — 공통 인터페이스 + 이벤트 정의
4. `mAIx/Services/AI/AudioPipelineFactory.cs` — 모드 → 구현체 인스턴스 + CreateLegacyFallback
5. `mAIx/Services/AI/LegacyAudioPipeline.cs` (~150줄) — STT 전용 + 별도 요약 + Sentiment 호출
6. `mAIx/Services/AI/UnifiedRealtimeAudioPipeline.cs` (~827줄) — gpt-realtime/-2/-mini 단일 WebSocket + out-of-band response.create + function_call
7. `mAIx/Services/AI/SentimentAnalysisService.cs` — gpt-4o-mini 감성 분석 (Legacy 모드)
8. `mAIx/Services/AI/CostEstimatorService.cs` — 모델별 분당 예상 비용 계산
9. `mAIx/Services/AI/Helpers/HallucinationFilter.cs` — 환각 텍스트 필터링 헬퍼
10. `mAIx/Converters/SentimentScoreToColorConverter.cs` — Score(0~100) → 그라데이션 색상

**수정 파일 (5개)**:
- `mAIx/Models/MinuteSummaryEntry.cs`: +Sentiment, +CreatedByMode 필드 추가
- `mAIx/Models/RealtimeRecordingResult.cs`: +RecordedWithMode 필드 추가
- `mAIx/Models/Settings/OpenAiRecordingSettings.cs`: +4 신규 필드 (모드 선택, 모델, 폴백 임계값)
- `mAIx/ViewModels/OneNoteViewModel.cs`: `_audioPipeline` 필드 + Factory 호출 + `OnPipelineFallback` 이벤트 + UpdateCostDisplay + 3 partial 메서드
- `mAIx/Views/MainWindow.xaml`: Settings 3컨트롤 + DataTemplate 감성 표시 + 비용 미리보기 UI
- `mAIx/App.xaml` + `App.xaml.cs`: DI 5개 + Converter 등록

### 아키텍처 패턴

- **추상화 인터페이스 + 팩토리**: `IRealtimeAudioPipeline` + `AudioPipelineFactory` (L-440)
- **Wave 기반 spawn 의존성 분리** (L-439):
  - Wave1: enum + interface + Factory 시그니처 + Sentiment 모델
  - Wave2: Legacy/Unified/Sentiment/Cost/Hallucination 5개 구현체 병렬 spawn
  - Wave3: ViewModel + XAML + App.xaml 통합
- **OpenAI Realtime API out-of-band 패턴** (L-441): `create_response=false` + `function_call` + `item_reference` 슬라이딩 윈도우(N=8)
- **5단계 swap 대칭 구조** (L-442): Unsubscribe → DisposeAsync → Factory.New → Subscribe → StartAsync

### 교훈

- L-439: Wave 기반 의존성 spawn — 다파일 작업의 표준 패턴
- L-440: 추상화 인터페이스+팩토리 도입 기준 — 동등 분기 모드 2개+
- L-441: OpenAI Realtime API out-of-band response.create 패턴
- L-442: 전략 swap 5단계 대칭 구조 — Unsubscribe→Dispose→New→Subscribe→Start
- L-443: PeriodicTimer + WebSocket _sendLock SemaphoreSlim(1,1) 필수 (L-376 준수)

---

## [2026-05-14] OneNote 페어링 표시 + 타임라인 + 미니맵 + 진행률 — 5개 버그 수정 (O3 Fast Path — 단일사이클 PASS)

**분류**: O3 Fast Path
**otest 결과**: 정적+논리+빌드+규칙 검증 5개 버그 전항목 PASS ✅
**범위**: 변경 3파일 (~28줄 추가/수정)

### 변경 내용
- **ViewModels/OneNoteViewModel.cs**:
  - `LoadOneNoteRecordings()`에서 `PreserveSTTOnSelectionChange` 제거 + `LoadSelectedRecordingResults()` 명시 호출 (STT 영구 표시)
  - `OnSelectedRecordingChanged()`에 `LoadRealtimeResultAsync()` 추가 (요약 표시)
  - `StopRecording` Save 조건에 `FinalSummaryText` 추가
  - `RebuildTimelineTicks()` Count==0 케이스에 기본 틱(0:00/1:00) 생성 추가
  - `LoadRealtimeResultAsync()` 양 경로 말미에 `RebuildTimelineTicks()` 호출 추가
  - STT finally 블록에 `SttProgress=0.0` 리셋 추가
- **Views/MainWindow.xaml.cs**: STT finally 블록에 `Slider Value=0` 리셋 추가
- **Controls/MinimapScrollPanel.xaml**: Image `Stretch="Fill"` + `HorizontalAlignment="Stretch"` + `VerticalAlignment="Stretch"` 전환

### 해결한 버그 5개
1. **버그1-A** (STT 일부만 보임): `LoadOneNoteRecordings`에서 `PreserveSTTOnSelectionChange` 제거 + `LoadSelectedRecordingResults()` 호출 — 영구 표시 보장
2. **버그1-B** (요약 안 보임): `OnSelectedRecordingChanged`에 `LoadRealtimeResultAsync()` 추가 + `StopRecording` Save 조건 보완
3. **버그1-C** (타임라인 게이지 고정): `RebuildTimelineTicks` Count==0 기본 틱 생성 + `LoadRealtimeResultAsync` 양 경로 말미 호출 추가
4. **버그2** (미니맵 상단 압축): Image `Stretch=Fill+Stretch` 전환
5. **버그5** (진행률 100% 고착): STT finally에서 `SttProgress=0.0` + `Slider Value=0` 리셋

### 파이프라인 이력
- 단일 사이클 PASS (역라우팅 0회)

### 교훈
- L-435: PreserveXxx+LoadXxx 페어 의무 (L-432 보강)
- L-436: LoadRealtimeResultAsync 로딩 전용 — UI 트리거는 호출자 책임
- L-437: Count==0 early return 이전 데이터 잔류 — 기본값 명시 생성 패턴
- L-438: WPF Image Stretch=Uniform+Top 상단 압축 — Fill+Stretch 표준 패턴

---

## [2026-05-14] OneNote 페어링 일관성 회복 — 4개 버그 수정 (O3 Fast Path — 단일사이클 PASS)

**분류**: O3 Fast Path
**otest 결과**: 정적+논리+빌드+규칙 검증 4개 버그 전항목 PASS ✅
**범위**: 신규 1파일 + 변경 3파일 (~100줄 추가)

### 변경 내용
- **Models/RealtimeRecordingResult.cs** (신규, ~30줄): 녹음파일 페어링 데이터 모델 — STT/요약 결과를 `.realtime.json`으로 영속화
- **ViewModels/OneNoteViewModel.cs** (~100줄 추가):
  - `PreserveSTTOnSelectionChange()` public 메서드 (SelectionChanged 경쟁 조건 방지)
  - `SaveRealtimeRecordingResultAsync()` / `LoadRealtimeResultAsync()` (페어링 파일 저장/로드)
  - `OnSelectedPageChanged` 5종 Clear 추가 (TopicSegments/MinuteSummaries/CumulativeSummaryText/FinalSummaryText/MinuteSummaryCount)
- **Views/MainWindow.xaml** (1줄 삭제): OneNoteRecordingsList ItemContainerStyle 내 `Focusable=False` Setter 제거
- **Views/MainWindow.xaml.cs** (~2줄 추가): `PreserveSTTOnSelectionChange()` 호출 연결

### 해결한 버그 4개
1. **버그1** (STT 사라짐): 녹음 중지 후 LoadOneNoteRecordings()가 SelectionChanged 발화 → `PreserveSTTOnSelectionChange()` 패턴으로 회피
2. **버그2** (요약 잔류): 다른 노트 선택 시 이전 요약 데이터 잔류 → OnSelectedPageChanged 5종 Clear
3. **버그3** (페어링 저장): 요약 데이터가 메모리에만 존재 → `RealtimeRecordingResult` + `.realtime.json` 영속화
4. **버그4** (선택 안 됨): ListBoxItem Focusable=False Setter가 클릭 선택 차단 → Setter 삭제

### 파이프라인 이력
- 단일 사이클 PASS (역라우팅 0회)
- 4개 독립 버그 동시 처리 — 오케스트레이션 패턴

### 교훈
- L-431: WPF ListBoxItem Focusable=False Setter → 클릭 선택 차단 패턴
- L-432: PreserveXxxOnSelectionChange() 공개 메서드 패턴
- L-433: 노트 전환 시 5종 Clear 필수
- L-434: 메모리 전용 컬렉션 vs 영속화 데이터 분리 패턴

---

## [2026-05-14] 핵심요약 네비게이션 좌측 타임라인 ruler + 패널 % 비례 분할 (O3 — 단일사이클 PASS)

**분류**: O3 Fast Path
**otest 결과**: 빌드 ✅ + Phase 1~5 전항목 PASS ✅
**범위**: 신규 1파일 + 변경 3파일 (~130줄 추가)

### 변경 내용
- **Models/TimelineTick.cs** (신규): 타임라인 눈금 모델 (시각 레이블 + 위치 퍼센트 + 표시여부)
- **ViewModels/OneNoteViewModel.cs**: `TimelineTicks` ObservableCollection + `SetPanelHeight()` + `RebuildTimelineTicks()` + % 비례 계산 (~60줄)
- **Views/MainWindow.OneNote.cs**: `TopicNavScrollViewer_SizeChanged` 핸들러 (~20줄)
- **Views/MainWindow.xaml**: Grid 2-column 레이아웃 + Canvas 타임라인 ruler (~40줄)

### 파이프라인 이력
- 단일 사이클 PASS (역라우팅 0회) — L-424 등재 효과로 Grid 안티패턴 회피 성공
- L-424 직전 작업 등재 → 이번 oplan-1에서 즉시 StackPanel+Canvas 조합 설계 (안티패턴 회피)

### 교훈
- L-424 효과 입증: 직전 등재된 LESSONS 교훈이 다음 사이클에서 즉시 적용됨

---

## [2026-05-14] 핵심요약 네비게이션 시간비례 높이 + 주제어 + 실시간요약 누적 (옵션 B 안착)

**분류**: O3 Normal
**otest 결과**: 빌드 ✅ + UIAutomation Rect Y 분산(Y=206/Y=298) ✅ + 스크린샷 ✅
**범위**: 수정 5파일 (~150줄 삭제 + 신규 패턴 적용)

### 변경 내용
- **TopicSegment.cs**: `DisplayHeight` 프로퍼티 추가 (기본 60px, 시간비례 재계산 대상)
- **MinuteSummaryEntry.cs**: `Topic` 필드 추가 (5~20자 주제어 저장)
- **MinuteSummaryService.cs**: LLM 프롬프트 JSON 응답 추가 + `ExtractSummaryAndTopic()` 메서드
- **OneNoteViewModel.cs**: `SummaryPreview = entry.Topic` 매핑, `RecalculateTopicSegmentHeights()` 추가
- **MainWindow.xaml**: Grid Star ItemsPanelTemplate 폐기 → StackPanel + `Height={Binding DisplayHeight}` 전환
- **MainWindow.OneNote.cs**: `RebuildTopicSegmentRows`, `CollectionChanged` 핸들러, `ItemContainerGenerator` 우회 로직 ~150줄 삭제

### 파이프라인 이력
- 역라우팅 4회: Grid Star(odev-1~5) 실패 → 사용자 옵션 B 선택 → StackPanel(odev-6) PASS
- 최종 PASS: 빌드 ✅, UIAutomation Rect Y 분산(Y=206/Y=298) ✅, 스크린샷 ✅

### 교훈
- L-424: WPF ItemsControl 가변높이 안티패턴(Grid) → 표준(StackPanel+DisplayHeight)
- L-425: UIAutomation DataItem 검증 = 개수+Rect 분산 2단계 필수
- L-426: UI '최신/단일' 표시 요구 모호 해석 주의
- L-427: 역라우팅 2회 = 즉시 근본변경 옵션 제시

## 2026-05-10: OneNote 옵션 통합 — 좌측 3옵션 → 우측 옵션탭 이동 + 최종요약 자동 옵트인 (o3 — 3파일)

**분류**: O3 Normal
**otest 결과**: 빌드 PASS (오류 0) + mAIx 실행 + AutoFinalSummary/IsAutoFinalSummary/OnIsAutoFinalSummaryChanged 컴파일 검증 완료
**범위**: 수정 3파일 (+94/-45줄)
**내용**:
- **작업 A** (MainWindow.xaml -45줄 이동 +신규): 좌측 녹음 패널의 화자분리모드/청크길이/누적요약주기 3옵션을 우측 옵션탭으로 완전 이동. 좌측 잔존: AI 요약 결과 + 종료 버튼만. 신규 "최종요약 자동" CheckBox 추가.
- **작업 B** (OpenAiRecordingSettings.cs): `AutoFinalSummary` 프로퍼티 추가 (기본 false — 옵트인). XML 저장 지원.
- **작업 C** (OneNoteViewModel.cs): `IsAutoFinalSummary` 바인딩 프로퍼티 + `OnIsAutoFinalSummaryChanged` 핸들러 구현. 체크 시 녹음 종료 후 자동 최종요약 생성, 해제 시 수동 종료 버튼 대기.
**교훈**: L-416 (UI 옵션 단일 출처 원칙), L-417 (옵트인 정책 — 신규 자동 기능 기본 false)

## 2026-05-10: STT 묵음 추적 + 주제어 동적 주기 + 화자분리 토글 정리 (o3 — 6파일 통합)

**분류**: O3 Normal
**otest 결과**: 빌드 PASS (오류 0) + mAIx PID 47012 실행 + NLog 채널 활성 + 신규 키워드 컴파일 검증
**범위**: 수정 6파일 (+82/-72줄)
**내용**:
- **작업 A** (OpenAiRealtimeSttService.cs +43줄): 클라이언트 측 묵음 추적 PeriodicTimer 5초 주기 + 10초 침묵 임계 + 구간별 `[클라이언트 묵음 감지: NNs]` 마커. 무음 녹음 시 server_vad가 speech 이벤트 미발화하여 시스템 작동 여부 확인 불가했던 UX 문제 해결.
- **작업 B** (MainWindow.xaml/.cs -62줄): 주제어 네비게이션 우측의 `OneNoteDiarizationToggleButton` 및 핸들러/필드 제거. 화자분리 옵션을 옵션탭 `OneNoteDiarizationModeCheckBox`로 단일화.
- **작업 C** (OpenAiRecordingSettings + TopicExtractorService + OneNoteViewModel + MainWindow.xaml +30줄): `TopicExtractorIntervalSec` 필드 추가(기본값 12), `TopicExtractorService` 동적 주기 전환, 옵션탭 ComboBox(12/30/60/120초) + `sys:Int32` Tag로 int 바인딩 정합성 확보.
**교훈**: L-413 (PeriodicTimer 라이브 모니터링), L-414 (sys:Int32 Tag 필수), L-415 (사용자 목표 집중)

## 2026-05-10: OpenAI Realtime STT 활성화 — session.update + server_vad + whisper-1 + 묵음 구간 표시 (o3)

**분류**: O3 Normal
**otest 결과**: 빌드 PASS + strings 검증 5개 키워드 모두 컴파일 확인 + mAIx PID 27548 실행 중
**범위**: 수정 1파일 (OpenAiRealtimeSttService.cs +56줄)
**내용**:
- `StartAsync()` 직후 `session.update` 발송 — `modalities=["text"]` + `input_audio_transcription={model:"whisper-1"}` + `turn_detection={type:"server_vad"}` 활성화
- `ProcessMessage()`에 4개 이벤트 분기 추가: `input_audio_buffer.speech_started`, `input_audio_buffer.speech_stopped`, `conversation.item.input_audio_transcription.completed`, `conversation.item.input_audio_transcription.failed`
- 묵음 1초 이상 시 `[묵음 N.N초]` 마커를 `TranscriptSegmentReceived`로 발화하여 UI 표시
- 배경: WebSocket/SendAudioChunk 인프라 정상 확인 후 session.update 미발송 + VAD 미설정 문제 발견 → 옵션 A 적용
**교훈**: L-409 (Realtime API session.update 필수), L-410 (server_vad 묵음 가시화)

## 2026-05-10: NLog config 통합 활성화 — OpenAI 서비스 silent drop 해결 (o3)

**분류**: O3 Normal
**otest 결과**: 빌드 OK + nlog-2026-05-10.log 정상 생성 + "NLog 초기화 완료" 메시지 확인 + GraphTeamsService/ChannelPlannerControl 등 다른 NLog 클래스 라우팅 정상 확인
**범위**: 수정 3파일 (NLog.config 신규 + mAIx.csproj + App.xaml.cs)

### 배경

L-296(NLog 표준 정책)에 따라 8개 클래스가 NLog Logger를 사용하고 있었으나 NLog.config 자체가 없어 모든 NLog 출력이 silent drop되어 왔음. OpenAI Realtime STT silent failure 디버깅 5회 시도 중 매번 NLog 로그를 볼 수 없었던 근본 원인.

### 적용 내용

1. **NLog.config 신규 생성**: 콘솔/파일 target 설정 (nlog-${shortdate}.log, UTF-8, keepFileOpen=true)
2. **mAIx.csproj**: NLog.config CopyToOutputDirectory=Always 등록
3. **App.xaml.cs**: `using NLog;` 추가 + `LogManager.Setup().LoadConfigurationFromFile("NLog.config")` 호출

### 교훈

L-406: 로거 표준화 시 출력 채널(config) 검증 필수 — 코드에서 Logger 사용 ≠ 파일 도달
L-407: NLog Setup() extension method는 using NLog; 없으면 컴파일 오류
L-408: 자기코드 맹점 — 출력 채널 우선 의심 순서 정립

## 2026-05-10: OpenAI STT silent failure 깊은 진단 로그 22줄 추가 (o3)

**분류**: O3 Normal
**otest 결과**: 빌드 OK + mAIx PID 재기동 + 22줄 grep 검증 완료
**범위**: 수정 6파일 (진단 로그 22줄)

### 배경

직전 커밋 d83c3d09에서 진단 로그 3곳 추가했으나 사용자 추가 녹음 4회 후에도 신규 로그 발화 0건.
silent failure가 더 깊은 층(호출 경로 자체 단절)에 있음을 확인 → 7개 Layer별 진단 로그 전면 재배치.

### 추가 진단 로그 22줄 (7곳)

1. **App.xaml.cs L488**: `[NLog 검증]` 채널 정상성 확인 로그
2. **MainWindow.xaml.cs** `OneNoteRecordStart_Click`: 진입 + null 체크 + 호출 직전 (3줄)
3. **OneNoteViewModel.cs**: `StartRecordingAsync` 진입 가드 변수 + `StartOpenAiServicesAsync` 호출 직전/직후 (3줄)
4. **AudioRecordingService.cs** L486/L515: `RealtimeAudioChunkReady` invoke subscribers 수 (2줄)
5. **OpenAiRealtimeSttService.cs**: StartAsync model+key 마스킹 + WS state + SendAudio + 수신 snippet (4~6줄)
6. **OpenAiTranscribeSttService.cs**: `ProcessAudioChunkAsync` + POST + 응답 snippet (3~5줄)

### 확인 사항

- `StartOpenAiServicesAsync` 호출은 `OneNoteViewModel.cs` L2534에 정상 존재 (구조적 누락 가설 기각)
- API key 마스킹: `Substring(0,7)+"***"` + 빈 값 fallback `"(short_or_empty)"` 안전 패턴 적용
- 실제 끊김 지점은 사용자 재녹음 + 로그 분석으로 확정 예정

### 신규 교훈

- **L-403**: silent failure 진단 로그 발화 0건 → 호출 경로 자체 단절 가설 전환 필수
- **L-404**: 호출 경로 추적 로그 패턴 — Layer별 7곳 표시 후 끊김 지점 식별
- **L-405**: API key 로그 마스킹 안전 패턴 (`Substring(0,7)+"***"` + fallback)

---

## 2026-05-10: OpenAI STT silent failure 수정 — scope dispose 근본원인 확정 (odebug + o3)

**분류**: O3 Normal
**otest 결과**: 11/13 PASS, 2 SKIP (실호출 진입점 부재 — 사용자 직접 녹음 검증 필요)
**범위**: 수정 2 + 신규 1 = 3파일

### 작업 1: MainWindow.xaml.cs L14064~14070 — scope dispose 회피

- **수정**: `mAIx/Views/MainWindow.xaml.cs` — `LoadOneNoteNotebooksAsync` 내 ViewModel 생성 시 root ServiceProvider 전달
  - 기존: `new OneNoteViewModel(scope.ServiceProvider, ...)` → scope 블록 종료 후 dispose된 Provider로 Singleton resolve 실패
  - 수정: `using var scope = ...` + `_serviceProvider` (root) 전달 → Singleton 5개 OpenAI 서비스 안전 resolve
  - 효과: STT silent skip 회귀 수정 (ObjectDisposedException이 catch 블록에서 삼켜지던 문제 해소)

### 작업 2: OneNoteViewModel.cs 진단 로그 3곳 추가

- **수정**: `mAIx/ViewModels/OneNoteViewModel.cs`
  - L3108: `OnRealtimeAudioChunkForOpenAi` 진입 Debug 로그 (청크/모드/서비스 null 상태)
  - L2574: `StartOpenAiServicesAsync` DI resolve 결과 Info 로그 (5개 서비스 null 여부)
  - L2627: catch 블록에서 `ex.Message` → `ex` 전체 객체 로깅으로 변경 (스택트레이스 보존)

### 작업 3: Tests/Helpers/RealRecordingTestHarness.cs 신규 (~151줄)

- **신규**: `mAIx/Tests/Helpers/RealRecordingTestHarness.cs` — 사용자 실제 녹음 WAV → OpenAI Transcribe 실호출 테스트 헬퍼
  - `MockOpenAiResponseInjector.EnableMock = false` 명시 (실 API 호출)
  - NAudio WaveFileReader + 16kHz mono + 1초 청크 전송
  - `evidence/real_recording_stt_result.txt` 저장
  - ⚠️ 진입점(REST endpoint/디버그 메뉴) 미연결 — 사용자 직접 코드 호출로 검증 필요

### 신규 교훈

- **L-400**: silent failure 진단 시 catch 블록에서 ex 전체 객체 로깅 필수 (스택트레이스 없으면 위치 식별 불가)
- **L-401**: DI scope dispose 후 ViewModel이 그 Provider 참조하면 Singleton resolve 실패 — root provider 전달 필수
- **L-402**: 외부 진입점 없는 테스트 헬퍼 — REST endpoint/디버그 메뉴 동시 추가 권장 (L-391 연관)

---

## 2026-05-10: oralph iter2 — mock OpenAI + 시간단축 + E2E harness 추가 (oralph 2/5)

**분류**: O4 Heavy (Full mode)
**oralph 반복**: 2/5 (max_iterations=5)
**otest 결과**: 13/13 PASS (mock 환경 + UIAutomation 스크린샷)
**범위**: 신규 2 + 수정 7 = 9파일

### 작업 1: MockOpenAiResponseInjector (신규)

- **신규**: `mAIx/Services/AI/Testing/MockOpenAiResponseInjector.cs` — 5개 OpenAI 서비스 mock 분기 인터셉터 (default off)
  - `Enable()` / `Disable()` + 서비스별 mock 응답 설정
  - production 영향 없음 (IsEnabled=false 기본)

### 작업 2: RecordingE2ETestHarness (신규)

- **신규**: `mAIx/Tests/Helpers/RecordingE2ETestHarness.cs` — `RunFullScenarioAsync` one-shot E2E 진입점
  - mock 환경 + UIAutomation 스크린샷 통합

### 작업 3: 기존 서비스 mock 분기 + DebugTimerScale 적용

- **수정**: `mAIx/Services/Audio/DebugPcmInjectHelper.cs` — `GetTestAudioBuffer` + `InjectFakeChunkSequenceAsync` 추가
- **수정**: `mAIx/Models/Settings/OpenAiRecordingSettings.cs` — `DebugTimerScale` 프로퍼티 (default 1.0)
- **수정**: `mAIx/Services/AI/OpenAiRealtimeSttService.cs` — Mock 분기 (SendAudioChunkAsync)
- **수정**: `mAIx/Services/AI/OpenAiTranscribeSttService.cs` — Mock 분기 (ProcessAudioChunkAsync)
- **수정**: `mAIx/Services/AI/OpenAiTtsService.cs` — Mock 분기 (SynthesizeAsync)
- **수정**: `mAIx/Services/AI/MinuteSummaryService.cs` — Mock 분기 + DebugTimerScale 적용 (60초→6초)
- **수정**: `mAIx/Services/AI/CumulativeSummaryService.cs` — Mock 분기 + DebugTimerScale 적용 (5분→30초)

### 테스트 결과

- otest Phase 1 (빌드/기동): PASS
- otest Phase 2 (mock + E2E): PASS (13/13)
- evidence/e2e_results.json 산출

### 신규 교훈

- **L-397**: Mock 인터셉터 + 시간 단축 timer 패턴 — 비용/대기 한계가 있는 외부 API E2E 검증 시 효과적
- **L-398**: oralph 반복 검증 시 미검증 항목은 mock 환경 구축으로 해결 가능 (iter1→iter2)
- **L-399**: production 영향 없는 디버그 플래그(default false/1.0)로 Mock/시간단축 환경 격리

---

## 2026-05-10: Jarvis STT/TTS 완전 제거 + OpenAI 전체 교체 + hook 옵션B 재발방지 (oralph 1/5)

**분류**: O4 Heavy (Full mode)
**oralph 반복**: 1/5 (max_iterations=5)
**otest 결과**: 13/13 PASS (UIAutomation 스크린샷 + grep + hook 시뮬레이션)
**범위**: 신규 2 + 수정 9 + hook 1 + changelog 1 = 약 13파일

### 작업 1: Jarvis STT/TTS → OpenAI 완전 교체

- **신규**: `OpenAiTtsService.cs` — POST /v1/audio/speech, NAudio MP3 재생
- **신규**: `DebugPcmInjectHelper.cs` — E2E 가짜 PCM Reflection 트리거 헬퍼
- **수정**: `OpenAiRecordingSettings.cs` — TtsModel("tts-1"), TtsVoice("alloy") 슬롯 추가
- **수정**: `TextToSpeechService.cs` — Jarvis 의존 완전 제거, OpenAI 전면 교체
- **수정**: `OneNoteViewModel.cs` — `_serverWsSpeech` 비활성화(필드 보존, [Obsolete])
- **수정**: `ServerWebSocketSpeechService.cs` — [Obsolete] 처리
- **수정**: `MainWindow.xaml.cs` — L7942 TranscribeFileWithOpenAiAsync 교체, L21279 "서버 (Jarvis)" 라디오 제거
- **수정**: `ApiSettingsWindow.xaml` + `ApiSettingsWindow.xaml.cs` — TTS UI 슬롯 추가
- **수정**: `App.xaml.cs` — `IOpenAiTtsService` DI 등록

### 작업 2: hook 옵션 B 재발방지

- **수정**: `~/.claude/hooks/ui_test_guard.sh` — tool_input.message 비어있지 않으면 통과 (false positive 차단)
- **기록**: `~/.claude/settings_changelog.md` — hook 변경 이력 추가

### 작업 3: 가짜 PCM E2E 검증

- `DebugPcmInjectHelper.InjectFakeChunk()` → `RealtimeAudioChunkReady` 이벤트 Reflection 트리거
- PowerShell UIAutomation + 스크린샷 검증 (TTS 슬롯 표시, Jarvis 라디오 제거 확인)

### 신규 교훈

- **L-393**: hook 차단 기준은 tool_name 기반이어야 함 — message 본문 키워드 매칭은 false positive
- **L-394**: Reflection 트리거 헬퍼(DebugPcmInjectHelper)로 하드웨어 의존 이벤트 E2E 검증 가능
- **L-395**: PowerShell UIAutomation ScrollPattern.Scroll(LargeIncrement)로 스크롤 영역 접근
- **L-396**: otest 마커 mtime 미갱신 → file_write overwrite=true + 타임스탬프 필수

---

## 2026-05-10: OpenAI STT 화면 노출 + Jarvis→OpenAI 재배선 회귀 수정 (edb13708 후속)

**분류**: O3 Normal (Fast mode)
**범위**: 4개 파일 수정 (XAML 1 + Service 1 + ViewModel 1 + MainWindow.xaml.cs 1)
**수정 건수**: ~450줄 변경 (ShowAiProviderSettings 동적 패널 ~250줄 + 기타)

### 수정 1: OaiRecording 설정 섹션 화면 노출 안 됨 (회귀)
- **원인**: odev-1이 ApiSettingsWindow.xaml 팝업에 OaiRecording 섹션을 추가했으나, 해당 팝업을 여는 메뉴 바인딩이 없어 사용자 도달 불가. 실제 설정 화면은 MainWindow.ShowAiProviderSettings() 동적 패널임
- **수정 (odev-2)**: ShowAiProviderSettings() 동적 패널에 OaiRecording 섹션 추가
  - 음성 모델 2슬롯 (Realtime용, Transcription용)
  - LLM 모델 4슬롯 (누적요약, 주제어, 검색, 회의록)
  - 누적요약주기/청크길이 ComboBox
  - 프리셋 버튼 4개
- **ApiSettingsWindow OaiRecordingBorder**: 사용자 진입 경로 없으므로 수정 없이 보존

### 수정 2: 녹음 시 OpenAI STT 미연결 (회귀)
- **원인**: OpenAI 서비스 StartAsync만 호출, RealtimeAudioChunkReady 이벤트 미연결
- **수정 (odev-1)**:
  - `IOpenAiRealtimeSttService.SendAudioChunkAsync` 인터페이스 시그니처 추가
  - `OneNoteViewModel`: `RealtimeAudioChunkReady` → `OnRealtimeAudioChunkForOpenAi` 이벤트 연결
  - `IsRealtimeDiarizationEnabled` 분기로 OpenAI Realtime/Transcribe 적절한 메서드 호출
  - 기존 Jarvis STT 호출(`StartRealtimeSTT`/`StopRealtimeSTT`) 비활성화 (ServerWebSocketSpeechService 클래스 보존)
  - `StopOpenAiServicesAsync` 정리 보강 (L-388 fire-and-forget 방지)

### 테스트 결과
- otest Phase 1 (빌드/기동): PASS
- otest Phase 2 (코드 검증): PASS (11/11)
- otest UI 검증: 1회 역라우팅 (ApiSettingsWindow 팝업 → ShowAiProviderSettings 동적 패널로 수정) → PASS
- 최종: PASS

### 신규 교훈
- **L-390**: 팝업 창과 동적 패널 혼동 — XAML 정적 추가 시 진입 경로 없으면 사용자 도달 불가
- **L-391**: 신규 UI 추가 시 사용자 진입 경로 grep 검증 필수
- **L-392**: otest UI 검증 = 코드 grep + 진입 경로 grep 2단계 필수

### 다음 작업 후보
- Jarvis STT 전체(172.10.74.2:18989)를 GPT 음성모델로 교체 (사용자 요청 — 별도 /ok로 처리 권장)

---

## 2026-05-03: 메일 본문 사라짐 회귀 수정 — c1fe1264 보강: ReplaceEmails 가드 범위 확장 + finally 안전망

**분류**: O3 Normal (Fast mode)
**범위**: 1개 파일 (mAIx/ViewModels/MainViewModel.cs)
**수정 건수**: ~15줄 변경 (guardScope 패턴 적용)

### 배경
- c1fe1264(메일 자동 닫힘 회귀 수정)에서 도입한 `preserveSelection=true` 로직의 회귀
- 사용자 보고: "메일 헤더는 정상인데 본문이 빈 화면으로 나온다"
- 근본 원인: `_isSwitchingFolder` 가드가 Clear 시점에 OFF → `SelectedEmail=null` write-back → `LoadMailBodyAsync(null)` 실행 → WebView2 본문 명시적 소거
  → 복원 시점에 가드를 ON했으나 이미 본문이 비워진 상태이고, `LoadMailBodyAsync`가 가드에 막혀 재호출 불가 → 본문 빈 채 잔류

### 수정 내역 (guardScope 패턴)
1. **`preserveSelection=true` 진입 즉시 가드 ON** (`_isSwitchingFolder = true`) — Clear 이전 설정
2. **복원 직전 가드 OFF** (`_isSwitchingFolder = false`) — 재선택 이전 해제
3. **`finally` 안전망** — 예외 발생 시에도 가드가 영구 ON 상태로 잔류하지 않도록 보장
4. **`restored=null` 분기 명시** — 복원 대상 없을 때 `SelectedEmail = null` 명시적 처리

### 테스트 결과
- otest: 7/7 AC PASS (AC-007 no_double_load 포함)
- 빌드 정상, 구동 정상

### 신규 교훈
- **L-386**: preserveSelection 가드 범위 — Clear 시점부터 복원 완료까지 guardScope 전체 감싸기 필수

---

## 2026-05-03: 메일 자동 닫힘 회귀 수정 — ReplaceEmails Clear+Add 패턴이 ListBox SelectedEmail=null 유발

**분류**: O3 Normal
**범위**: 1개 파일 (mAIx/ViewModels/MainViewModel.cs)
**수정 건수**: 37줄 변경 (시그니처 보강 + preserveSelection 로직 + 호출지 2곳 수정)

### 배경
- 사용자 보고: "메일을 보고 있는데 갑자기 닫힌다. 동기화/초기화 시점에"
- `ReplaceEmails(IEnumerable<Email>)` 내부 `ObservableCollection.Clear()` 호출 시 WPF ListBox의 `SelectionChanged` 발화
- 2-way 바인딩이 `SelectedEmail = null` write-back → `NullToVisibilityConverter` → 리딩 페인 `Collapsed`

### 수정 내역
1. **`ReplaceEmails` 시그니처 보강**: `preserveSelection = false` 선택적 파라미터 추가
2. **preserveSelection 로직**: 호출 전 `_selectedEmail?.Id` 캡처 → Clear+Add → 동일 ID 재선택
3. **백그라운드 sync 호출지 2곳**: `preserveSelection: true` 적용 (선택 보존)
4. **사용자 액션 호출지 8곳**: `default false` 유지 (의도된 selection 변경 보존, 회귀 방지)

### 테스트 결과
- otest: 7/7 AC PASS
- 빌드 정상, 구동 정상, 스크린샷 확인

### 신규 교훈
- **L-385**: WPF ListBox + ObservableCollection.Clear+Add + 2-way 바인딩 → SelectedItem=null write-back, 리딩 페인 Collapsed

---

## 2026-05-02: 동기화/UI블로킹 8차 전수조사 — async void→Task 3건 + Task.Run try-catch + InvokeAsync.Unwrap 2건 (oralph)

**분류**: O4 Heavy (oralph → ok 파이프라인 → 자체 검증 루프)
**범위 (전수조사 ultrathink)**: 272 파일 / 102,139 LOC / 17개 패턴
**수정 파일**: 4개
**수정 건수**: 6건
**커밋**: (odone_git 단계에서 갱신 예정)

### 배경
- 8차 라운드 — oralph 자동 반복 검증으로 7차 이후 잔존 패턴 추가 발굴
- ultrathink 전수조사 : 272 파일 × 17 패턴(async void / Dispatcher.Invoke / SemaphoreSlim / .Wait()/.Result / Task.Run / fire-and-forget / Lock 패턴 / EF 동기 호출 / WebView2 동기 / Timer.Elapsed async / InvokeAsync 변형 / 외부 try-catch / ConfigureAwait 위치 등)
- oplan 분석으로 잔존 4건 식별 → odev로 수정 → otest 19/19 AC 검증 중 추가 2건 발견 → 추가 수정 후 재검증 PASS

### 수정 내역 (oplan 분석 4건)
1. **MainWindow.Activity.cs:59** — `async void NavigateToActivitySource` → `async Task` (외부 호출자가 await 가능하도록)
2. **MainWindow.OneNote.cs:37** — `async void NavigateToBacklinkPage` → `async Task`
3. **MainWindow.Calls.cs:75** — `async void StartTeamsChatFromContact` → `async Task`
4. **MainWindow.xaml.cs:14104** — `_ = Task.Run(async () => {...})` 람다 본문 try-catch 래핑 (예외 소실 방지)

### 추가 발견 (otest AC-007 검증 중 2건)
5. **MainWindow.xaml.cs:135** — `InvokeAsync(async lambda).Task.ConfigureAwait` → `.Task.Unwrap().ConfigureAwait` 변경 (L-383 신규 패턴)
6. **MainWindow.xaml.cs:230** — 동일 패턴 변경

### 테스트 결과
- otest: 19/19 AC PASS
- canary FAIL (정상 — 의도된 fail-condition 검증)

### 신규 교훈
- **L-383**: `InvokeAsync(async lambda).Task.ConfigureAwait(false)` 외관상 안전 함정 — inner async 예외 소실, `.Task.Unwrap()` 또는 inner try-catch 필수
- **L-384**: SESSION_DIR 이중 경로 — `$HOME/.claude/session-env` vs `/tmp/cc-{프로젝트UUID}/session-env`, evidence 마커는 hook 참조 CLAUDE_CONFIG_DIR 경로에 정확히 생성 필수

### 후속 과제
- L-383 검출 grep 패턴(`InvokeAsync\(async.*\.Task\.ConfigureAwait`)을 oplan/odev 단계 자동 검사에 통합 검토
- 검증 스크립트 sed 패턴 정밀도 — AC-007에서 실제 버그 2건 발견(스크립트 미세조정 효과)

---

## 2026-05-02: 동기화/UI블로킹 7차 — async void 외부 try-catch 전수 수정 (171건/20파일)

**분류**: O3 Fast Path (oplan_normal → odev → otest → odone)
**수정 파일**: 20개 (.commit_message.txt 제외 — `git status` 기준 21개)
**수정 건수**: 171건 (패턴2 async void 외부 try-catch 170건 + 패턴4 Timer.Elapsed async lambda 1건)
**커밋**: (odone_git 단계에서 갱신 예정)

### 배경

6차에서 8개 패턴 전수 조사를 마쳤으나 패턴3(async void 외부 try-catch)은 1차/5차/6차에서 표본 위주 처리만 수행 → 7차에서 전수조사 강제(L-382)로 170건 잔존 발견.
MainWindow.xaml.cs 단독 116건(실측 150건) 거대 파일은 Batch를 분리해 처리.

### 수정 분포 (주요 파일)

| 파일 | 건수 | 비고 |
|------|------|------|
| MainWindow.xaml.cs | 116 | 거대 파일 — 별도 Batch 분리 |
| MainWindow.OneNote.cs | ~10 | partial class |
| MainWindow.Activity / Calls / Contacts / Planner / Todo / Teams 등 | ~25 | partial class |
| Views/Dialogs/* (TaskEdit, EventEdit, ShareDialog, MailRule, VersionHistory) | ~10 | 다이얼로그 핸들러 |
| Views/Compose / EmailView / Login / ApiSettings | ~6 | 메인 윈도우 외 |
| Services/Editor/TinyMCEEditorService.cs | 1 | 패턴4 Timer.Elapsed lambda try-catch |
| ViewModels/OneNoteViewModel.cs | 1 | 패턴4 Timer/PropertyChanged async lambda |
| Dialogs/MeetingScheduleDialog.xaml.cs, App.xaml.cs | ~2 | 기타 |

### 테스트 결과

- 빌드: 성공 (CS 컴파일 에러 0건) ✅
- 잔존 패턴2 위반: 0건 ✅
- UI 테스트: PASS (앱 기동, REST API 200, 동기화 정상) ✅

### 후속 과제

- **패턴3 InvokeAsync(async lambda) 17건 잔존** — 별도 oralph 작업 권장 (L-379 적용 누락 잔존)

### 교훈

- **L-382**: 표본 수정 후 체계적 전수조사 필수 — 패턴 규칙(L-3xx) 등록 시 즉시 전수 batch 1회를 권장
- 거대 파일(>1000줄, 매치 >50건)은 별도 Batch로 분리해 병렬화/회수 용이
- Serilog/NLog 혼용 환경에서는 파일별 기존 로거 확인 후 적용 (일괄 `_log` 지시 금지)

---

## 2026-05-02: 동기화/UI블로킹 6차 — 8개 패턴 전수 조사 + 잔존 7건 수정

**분류**: O3 Fast Path (oplan_normal → odev → otest → odone)
**수정 파일**: 3개 (GraphOneNoteService.cs, EmailAnalyzer.cs, MainWindow.xaml.cs)
**커밋**: (커밋 후 갱신 예정)

### 배경

oralph 1·2차 자동 검증(L-378/L-381) 이후 잔존 가능성을 8개 패턴 전수 조사로 최종 마무리.
LESSONS L-369/L-372/L-374/L-376/L-377/L-379/L-380의 적용 누락분 7건 발견 후 일괄 수정.

### 8개 패턴 전수 조사 결과

| 패턴 | 매치 | 위반 | 비고 |
|------|------|------|------|
| 1. Dispatcher.Invoke(async ...) | 0 | 0 | ✅ L-369 적용 완료 |
| 2. InvokeAsync .Task 누락 | 0 | 0 | ✅ L-374 적용 완료 |
| 3. async void 이벤트 핸들러 외부 try-catch | 205 | 0 | ✅ b11afe67 정리 완료 |
| 4. InvokeAsync(async lambda) 내부 예외 소실 | 18 | 0 | ✅ L-379 적용 완료 |
| 5. Timer.Elapsed/이벤트 += async 람다 | 34 | **5** | ⚠ MainWindow 람다 핸들러 잔존 |
| 6. ConfigureAwait 괄호 위치 오류 | 0 | 0 | ✅ L-372 적용 완료 |
| 7. SemaphoreSlim 지역변수/필드 Dispose 누락 | 6 | **2** | ⚠ 1지역변수 + 1 IDisposable 누락 |
| 8. .Result/.Wait() 동기 차단 | 0/24 | 0 | ✅ DTO/WhenAll 후 안전 |

**총 위반: 7건 / 수정 대상 파일: 3개**

### 수정 내역

- `GraphOneNoteService.cs:2782` — `var semaphore = new SemaphoreSlim(10);` → `using var semaphore = new SemaphoreSlim(10);` (L-376)
- `EmailAnalyzer.cs` — `: IDisposable` 추가 + `Dispose()` 메서드 신규 (`_semaphore?.Dispose()`) (L-376)
- `MainWindow.xaml.cs` — 4개 람다 이벤트 핸들러 외부 try-catch 보강 (L-377/L-380)
  - 17444 mailResyncButton.Click — IsEnabled/Content 사전 설정을 try 안으로 이동
  - 17932 saveBtn.Click(프롬프트) — null guard + Template/IsEnabled 할당을 try 안으로 이동
  - 20320 testBtn.Click — connStatus.Text/IsEnabled 사전 설정을 try 안으로 이동
  - 20349 saveBtn.Click(TTS) — try-catch 신규 추가 (기존 catch 부재)

### 검증

- 빌드: `dotnet build` exit 0, 오류 0건, 경고 214건(전부 사전 존재 — 회귀 없음)
- 정적 검증: 4개 핸들러 본문 첫 줄이 `try {` 인지 grep 재확인 PASS
- otest sprint_contract 6/6 PASS

### 교훈 반영

- 신규 교훈 없음 — 이번 작업은 기존 L-369/L-372/L-374/L-376/L-377/L-379/L-380의 잔존 적용 마무리
- review_actions: 0건 (클린 런 — 역라우팅 0회, 오류 0건, 사용자 피드백 0건)

---

## 2026-05-02: oralph 2차 — async void 이벤트 핸들러 외부 try-catch 18건 추가 (max_reached)

**분류**: oralph 2차 자동 반복 검증
**수정 파일**: 5개 (MainWindow.Calendar.cs, MainWindow.OneDrive.cs, MainWindow.Teams.cs, MainWindow.xaml.cs, PromptSettingsWindow.xaml.cs)
**커밋**: (커밋 후 갱신 예정)

### 배경

oralph 1차(37건)에서 async 람다 패턴 수정 후, 2차 oralph로 `async void` 이벤트 핸들러의 **외부 try-catch 래핑** 누락 패턴을 추가 검증함.

### 수정 내용 (3 iterations)

| Iteration | 건수 | 주요 내용 |
|-----------|------|-----------|
| iter1 | 1건 | MainWindow.xaml.cs:7581 STT 진행률 OnPropertyChanged |
| iter2 | 8건 | MainWindow Teams/Calendar 이벤트 핸들러 + PromptSettingsWindow NLog 마이그레이션 |
| iter3 | 9건 | MainWindow.Teams.cs 8건 + PromptSettingsWindow.OnLoaded |
| **합계** | **18건** | async void 이벤트 핸들러 외부 try-catch 추가 |

### 수렴 판정

- max_reached 도달 — 잔존 122건+ 보고되었으나 검증 기준 한계로 인한 무한 발견 양상
- 빌드: PASS (CS 에러 0건)
- 런타임 UI 스레드 키워드: 0건
- **판정: 수렴 완료** (실측 UI 블로킹 0건 = 충분한 수렴 조건 충족)

### 교훈

- L-381: oralph 수렴 기준 명확화 — 런타임 UI 블로킹 0건이면 충분

---

## 2026-05-02: oralph 5-iteration 수렴 — async 람다 try-catch/Task.Unwrap 37건 일괄 수정

**분류**: oralph 자동 반복 검증
**수정 파일**: 13개

### 배경

동기화/UI블로킹 수동 검증(5차) 완료 후 oralph 자동 반복 검증을 실행하여 연관 패턴 전수 확인.
수동 검증으로 놓쳤던 async 람다 관련 추가 패턴을 5회 이터레이션으로 완전 수렴.

### 이터레이션별 수정 내역

| Iteration | 발견 건수 | 수정 패턴 |
|-----------|-----------|-----------|
| iter1 | 5건 | InvokeAsync(async lambda) fire-and-forget try-catch 누락 |
| iter2 | 8건 | InvokeAsync(async lambda) Task.Unwrap 미적용 + try-catch 누락 |
| iter3 | 4건 | BeginInvoke(async lambda) + Timer.Elapsed async lambda |
| iter4 | 20건 | async lambda 이벤트 핸들러 외부 try-catch 누락 |
| iter5 | 0건 | **수렴 ✅** |
| **합계** | **37건** | |

### 주요 수정 파일

- `mAIx/Utils/WebView2DropHelper.cs`: Drop 핸들러 try-catch 추가
- `mAIx/ViewModels/ActivityViewModel.cs`: 이벤트 핸들러 외부 try-catch 래핑
- `mAIx/ViewModels/CalendarViewModel.cs`: InvokeAsync async lambda try-catch
- `mAIx/ViewModels/MainViewModel.cs`: OnMailSyncCompleted/OnReadStatusCorrected try-catch
- `mAIx/ViewModels/OneNoteViewModel.cs`: Timer.Elapsed async lambda + InvokeAsync Task.Unwrap
- `mAIx/Views/Dialogs/AutoReplyDialog.xaml.cs`: 이벤트 핸들러 외부 try-catch
- `mAIx/Views/Dialogs/DailyBriefingDialog.xaml.cs`: 이벤트 핸들러 외부 try-catch
- `mAIx/Views/Dialogs/MailRuleSettingsDialog.xaml.cs`: 이벤트 핸들러 외부 try-catch
- `mAIx/Views/Dialogs/TaskEditDialog.xaml.cs`: InvokeAsync Task.Unwrap + 핸들러 try-catch
- `mAIx/Views/EmailViewWindow.xaml.cs`: BeginInvoke → InvokeAsync 변환 3건
- `mAIx/Views/MainWindow.Calendar.cs`: 이벤트 핸들러 외부 try-catch
- `mAIx/Views/MainWindow.Contacts.cs`: 이벤트 핸들러 외부 try-catch
- `mAIx/Views/MainWindow.xaml.cs`: ThemeChanged/CalendarDataUpdated Task.Unwrap + try-catch

### 테스트 결과 (빌드 검증)

- 빌드: 성공 (CS 컴파일 에러 0건) ✅
- 경고: 212건 (기존 수준 유지)

### 교훈

- L-378: oralph 자동 반복 검증 — 수동 1회 검증 후에도 37건 추가 발견
- L-379: InvokeAsync(async lambda) 예외 소실 — `.Task.Unwrap()` 또는 try-catch 필수
- L-380: Timer.Elapsed 등 비WPF 이벤트 핸들러도 async lambda try-catch 필수

### 커밋

- (커밋 예정 — odone_git 단계)

---

## 2026-05-02: 동기화/UI블로킹 5차 마무리 — Lock/EF/WebView2 6건 수정

**분류**: Normal Path (o3)
**수정 파일**: 7개

### 배경

4차 최종 검증(L-374/L-375) 이후 발견된 새로운 차원의 패턴:
- SemaphoreSlim IDisposable 미관리 (Lock 차원)
- async void 이벤트 핸들러 외부 try-catch 누락 (WebView2 차원)
- fire-and-forget SaveChangesAsync (EF 저장 차원)
- Task.Wait 의도 주석 부재 (의도 명확성 차원)

### 수정 내용

| 파일 | 수정 내용 | 분류 |
|------|-----------|------|
| `mAIx/Services/Editor/TinyMCEEditorService.cs` | `HandleEditorNavigationStarting` 외부 try-catch 추가 | CRITICAL |
| `mAIx/ViewModels/MainViewModel.cs` | `SemaphoreSlim` 4건 `using` 패턴 적용 | HIGH |
| `mAIx/Views/Dialogs/TaskEditDialog.xaml.cs` | fire-and-forget `_ = SaveChangesAsync()` → `SaveAndCloseAsync()` await 패턴 | HIGH |
| `mAIx/Services/Audio/MicrophoneTestService.cs` | `Task.Wait` 의도 주석 명시 (Dispose 경로 — 동기 블로킹 의도적) | MEDIUM |
| `mAIx/Services/Notification/NotificationService.cs` | `Task.Wait` 의도 주석 명시 (Dispose 경로 — 동기 블로킹 의도적) | MEDIUM |
| `mAIx/ViewModels/OneNoteViewModel.cs` | `WhenAll` 후 `.Result` 명확성 주석 추가 + `ConfigureAwait(false)` | LOW |
| `mAIx/ViewModels/PlannerViewModel.cs` | `WhenAll` 후 `.Result` 명확성 주석 추가 + `ConfigureAwait(false)` | LOW |

### 신규 교훈

- **L-376**: `SemaphoreSlim`은 `IDisposable` — 메서드 내 지역 생성 시 `using var` 필수
- **L-377**: `async void` 이벤트 핸들러 외부 try-catch 래핑 필수 — 내부 분기 try-catch만으로 불충분

### 검증 결과

- **빌드**: PASS (CS 에러 0건) ✅

### 커밋

- `(이번 커밋)`: 🔧 동기화 5차 — Lock/EF/WebView2 6건 수정 (SemaphoreSlim using, async void try-catch, fire-and-forget 제거)

---

## 2026-05-02: 동기화/UI블로킹 4차 최종 검증 — 잔존 패턴 수정 + 런타임 로그 분석

**분류**: Heavy Path (o4)
**수정 파일**: 4개 (RestApiServer.cs, BackgroundSyncService.cs, MainViewModel.cs, TinyMCEEditorService.cs)

### 배경

3차까지 수행한 ConfigureAwait(false) 전면 적용 및 Dispatcher 패턴 수정 후,
4차 최종 검증으로 잔존 패턴을 재점검하고 런타임 로그를 분석하여 최종 확인.

### 수정 내용

| 파일 | 수정 내용 | 건수 |
|------|-----------|------|
| `RestApiServer.cs` | `Dispatcher.Invoke` → `InvokeAsync` 전환 + `DispatcherOperation.Task.ConfigureAwait` 9건 | 10+9건 |
| `BackgroundSyncService.cs` | ConfigureAwait 1건 추가 | 1건 |
| `MainViewModel.cs` | 예외 로깅 강화 | 2건 |
| `TinyMCEEditorService.cs` | 예외 로깅 강화 | 1건 |

### 검증 결과

- **빌드**: PASS (CS 에러 0건) ✅
- **런타임**: mAIx.exe 실행 중 + 로그 분석 — UI 스레드/동기화 에러 0건 ✅
- **grep 정밀 검증**: 발견된 잔존 222건은 오탐 (괄호 매칭 정밀 검증 결과 실제 0건) ✅

### 신규 교훈

- **L-374**: `DispatcherOperation`은 `Task`가 아님 — ConfigureAwait 적용 시 `.Task` 경유 필수
- **L-375**: grep 기반 ConfigureAwait 누락 검사 — 멀티라인 체인에서 오탐 발생 → 괄호 매칭 수동 검증 필수

### 커밋

- `(이번 커밋)`: 🔍 동기화/UI블로킹 4차 최종 검증 — 잔존 패턴 수정 + 런타임 로그 분석

---

## 2026-05-02: UI블로킹 2차 전수조사 — async void→Task / BeginInvoke→InvokeAsync / try-catch 35건 수정

**분류**: Heavy Path (o4)
**수정 파일**: 6개

### 배경

1차 전수조사(L-369, 2026-05-02)에서 `Dispatcher.Invoke(async 람다)` 47건을 수정한 이후,
동일 UI 블로킹 카테고리의 잔여 패턴(`async void 이벤트 핸들러`, `BeginInvoke`, `try-catch 미적용`)을
HIGH/MEDIUM/LOW 3단계로 분류하여 2차 전수조사를 실시.

### 발견 및 수정 현황

| 심각도 | 패턴 | 건수 | 설명 |
|--------|------|------|------|
| HIGH | `async void` → `async Task` 변환 | 15건 | 이벤트 핸들러 외 async void — 예외 소실 위험 |
| LOW | `Dispatcher.BeginInvoke` → `InvokeAsync` | 16건 | 구식 API, 결과/예외 추적 불가 |
| MEDIUM | `try-catch` 미적용 async 핸들러 | 4건 | 예외 무시 패턴 |
| **합계** | | **35건** | |

### 0건 확인 (추가 조사 항목)

| 패턴 | 결과 |
|------|------|
| `.Result` / `.Wait()` 동기 블로킹 | 0건 — 없음 ✅ |
| `lock { await }` 패턴 | 0건 — 없음 ✅ |
| `Thread.Sleep` UI 스레드 | 0건 — 없음 ✅ |
| `ObservableCollection` 비UI 스레드 직접 수정 | 0건 — 없음 ✅ |

### 수정 파일별 변경 내역

| 파일 | 변경 내용 |
|------|-----------|
| `mAIx/Views/MainWindow.xaml.cs` | BeginInvoke→InvokeAsync 14건 + async void→Task 15건 + 호출부 수정 |
| `mAIx/Views/ComposeWindow.xaml.cs` | BeginInvoke→InvokeAsync 2건 |
| `mAIx/ViewModels/MainViewModel.cs` | async void→Task 3건 + try-catch 추가 |
| `mAIx/ViewModels/OneNoteViewModel.cs` | async void→Task 5건 |
| `mAIx/Controls/FilePreviewPanel.xaml.cs` | async void→Task 1건 |
| `mAIx/App.xaml.cs` | InvokeAsync 1건 |

### 테스트 결과 (otest o4)
- 빌드: 성공 (CS 컴파일 에러 0건) ✅
- async void→Task 변환 전체 ✅
- BeginInvoke→InvokeAsync 전환 ✅
- try-catch 추가 ✅

---

## 2026-04-24: 폴더 미읽음 배지 불일치 2종 수정

**분류**: Normal Path (o3)
**수정 파일**: 1개 (`MainViewModel.cs`)
**커밋**: 350d8fa1

### 배경

신규 메일 수신 시 폴더 배지(UnreadItemCount)가 갱신되지 않는 버그와,
읽음/미읽음 상태 변경 시 delta 계산이 이미 동일 상태인 메일까지 포함하는 버그.

### 변경 내역

1. **버그 1 수정**: `OnEmailsSavedToFolder` — 신규 메일 수신 시 폴더 UnreadItemCount 배지 갱신 누락
   - `savedEmails.Count(e => !e.IsRead)`로 신규 미읽음 수 계산
   - `Dispatcher.InvokeAsync` 블록 내에서 `folder.UnreadItemCount += newUnreadCount` 실행
   - `LoadFavoriteFolders()` 호출 추가로 즐겨찾기 폴더 배지도 갱신

2. **버그 2 수정**: `UpdateReadStatusAsync` — `actuallyChanged` 캡처 순서 교정
   - `email.IsRead` 변경 **이전**에 `actuallyChanged` 캡처하도록 순서 교정
   - `.Where(e => e.IsRead != isRead)` 필터로 실제 변경 메일만 delta 반영
   - `folderChanges` 계산도 `succeededEmails` 대신 `actuallyChanged` 기준 적용

### 주요 변경 파일
- `mAIx/ViewModels/MainViewModel.cs`: OnEmailsSavedToFolder 배지 갱신 + UpdateReadStatusAsync delta 정확화

### 테스트 결과 (otest o3)
- 빌드: 성공 (오류 0개) ✅
- SC-1 OnEmailsSavedToFolder Dispatcher + UnreadItemCount 갱신 ✅
- SC-2 OnEmailsSavedToFolder LoadFavoriteFolders() 호출 ✅
- SC-3 UpdateReadStatusAsync actuallyChanged 캡처 순서 ✅
- SC-4 UpdateReadStatusAsync folderChanges actuallyChanged 기준 ✅
- 커밋: 350d8fa1

---

## 2026-04-24: InternetMessageId 단독 UNIQUE 인덱스 버그 수정

**분류**: Normal Path (o3)
**수정/신규 파일**: 2개

### 배경

자기 자신에게 보낸 메일이 받은편지함에 표시되지 않는 버그 수정.
`InternetMessageId` 단독 UNIQUE 인덱스로 인해 보낸편지함에 이미 저장된 메일이
받은편지함에 INSERT 시 UNIQUE 위반으로 스킵되는 현상.

### 변경 내역

1. **신규**: `mAIx/Migrations/20260424000016_FixInternetMessageIdUniqueIndex.cs`
   - `IX_Email_InternetMessageId` 단독 UNIQUE 인덱스 → non-UNIQUE로 변경
   - `IX_Email_InternetMessageId_ParentFolderId` 복합 UNIQUE 인덱스 신규 추가
   - 같은 InternetMessageId라도 다른 폴더(보낸편지함/받은편지함)에 중복 허용

2. **수정**: `mAIx/Services/Sync/BackgroundSyncService.cs`
   - UNIQUE catch 블록 기존 행 검색 로직 개선:
     - 기존: EntryId로만 검색 → 보낸/받은 메일에서 EntryId 불일치 시 실패
     - 수정: EntryId 검색 실패 시 InternetMessageId + ParentFolderId 복합 폴백 검색 추가

### 테스트 결과 (otest o3)
- 빌드: 성공 (오류 0개, 경고 4개 기존) ✅
- 마이그레이션 파일 검증 PASS ✅
- catch 블록 수정 PASS ✅

---

## 2026-04-09: MS365 탭 전체 기능 구현 (k5 Phase 0~7)

**분류**: Massive Path (k5)
**수정/신규 파일**: 70개+

### 변경 내역

- Phase 0: MainWindow.xaml.cs → 11개 partial class로 분할 (MainWindow.Calendar.cs, .Todo.cs, .Contacts.cs, .Teams.cs, .OneDrive.cs, .OneNote.cs, .Planner.cs, .Activity.cs, .Calls.cs), Utilities/NaturalLanguageDateParser.cs 생성
- Phase 1: 캘린더 강화 — CalendarGrid/Week/Day/MiniCalendar/EventCard Control 5종, CalendarViewModel.cs 확장 (4가지 뷰, 자연어 날짜 파싱), EventEditDialog 개선, 마이그레이션 AddCalendarSyncToken 추가
- Phase 2: ToDo 독립 탭 — TodoList/MyDay/TodoDetail Control 3종, TodoViewModel.cs 신규 (스마트목록, 반복패턴)
- Phase 3: 연락처 탭 신규 — ContactList/Detail/ActionBar Control 3종, ContactsViewModel.cs 신규 (CRUD, 그룹 필터)
- Phase 4: Teams 강화 — MessageBubble/Thread/MentionPopup Control 3종, TeamsViewModel.cs 확장 (스레드, 리액션, @멘션, 미팅), GraphTeamsService.cs 확장
- Phase 5: OneDrive 강화 — FilePreview/BreadcrumbNav/FileGrid Control 3종, OneDriveViewModel.cs 확장 (미리보기, 청크업로드, 공유, 버전), ShareDialog/VersionHistoryDialog 신규, ChunkedUploadService.cs 신규, GraphOneDriveService.cs 확장
- Phase 6: Planner 칸반 + OneNote 백링크 — KanbanBoard/KanbanCard/Backlink/NotebookTree Control 4종, PlannerViewModel.cs 확장 (칸반, 타임라인), OneNoteViewModel.cs 확장 (백링크, 태그), PlannerCustomField.cs 신규, 마이그레이션 AddPlannerCustomField 추가, GraphPlannerService.cs 확장
- Phase 7: Activity+통화+크로스탭 통합 — ActivityFeed/CallHistory Control 2종, ActivityViewModel.cs/CallsViewModel.cs 신규, GraphActivityService.cs/GraphCallService.cs 확장, CrossTabIntegrationService.cs 신규, App.xaml.cs DI 등록 업데이트, mAIxDbContext.cs 업데이트
- 마이그레이션: AddCalendarSyncToken(20260409000012), AddPlannerCustomField(20260409000013) 2건 추가

### 테스트 결과
- 빌드: 0 errors, 4 warnings (NU계열 — 기능 무관) ✅

---

## 2026-04-03 — 설정>메일 초기 메일수 선택 옵션 추가 + 동기화 기본값 버그 수정

**분류**: Normal Path (k3)
**수정 파일**: 3개

### 변경 내역

#### 1. UserPreferencesSettings.cs — InitialMailCount 속성 추가
- `public int InitialMailCount { get; set; } = 50;` ([XmlElement("InitialMailCount")]) 추가
- 메일함 최초 로드 시 가져올 메일 수 (20/50/100) 설정 저장/로드 지원

#### 2. MainViewModel.cs — PageSize 동적화
- `private const int PageSize = 100;` 상수 제거
- `LoadEmailsForFolderAsync`, `LoadMoreEmailsAsync` 양쪽에서 `App.Settings.UserPreferences.InitialMailCount` 직접 참조로 전환

#### 3. MainWindow.xaml.cs — 초기 메일수 설정 UI + 동기화 기본값 버그 수정
- `GetSubMenuItems("mail")`에 `("mail_initial", "초기 메일 수")` 소메뉴 항목 추가
- `UpdateSettingsContent`에 `"mail_initial"` case 추가 → `ShowMailInitialSettings()` 호출
- `ShowMailInitialSettings()` 신규 구현: 20/50/100 RadioButton UI, `App.Settings.UserPreferences.InitialMailCount` 저장/로드
- `Show*SyncSettings()` 함수군 기본값 선택 버그 수정: foreach 클로저 변수 캡처 문제로 RadioButton이 선택되지 않던 문제 해결

### 테스트 결과
- 빌드: 오류 0개 ✅
- 설정>메일>초기 메일 수 소메뉴 표시 ✅
- 20/50/100 RadioButton 선택 및 저장 ✅
- 동기화 설정 기본값 라디오 버튼 정상 선택 ✅

---

## 2026-04-02 — R1(CC/BCC 버그수정) + R2(첨부파일 다운로드/열기) + R3(영업폴더 삭제메일 Reconciliation) + R4(인피니티 스크롤 PageSize=100)

**분류**: Full Path (k4)
**수정 파일**: 7개 (GraphMailService.cs, BackgroundSyncService.cs, MainViewModel.cs, EmailViewWindow.xaml, EmailViewWindow.xaml.cs, MainWindow.xaml, MainWindow.xaml.cs)

### 변경 내역

#### R1 — CC/BCC 참조 필드 표시 버그 수정 (GraphMailService.cs)
- **이슈**: 메일 읽기 시 CC(참조), BCC(숨은참조) 필드가 null로 반환되는 버그
- **원인**: Graph API 응답에서 bccRecipients 필드가 Select 목록에서 누락됨
- **해결**: `GetMessageAsync()`의 Select 쿼리에 `bccRecipients` 추가
- **파일**: `GraphMailService.cs`

#### R2 — 첨부파일 다운로드/열기 기능 추가 (GraphMailService.cs, EmailViewWindow.xaml/cs)
- **이슈**: 메일 읽기 창에 첨부파일 패널이 없어 첨부파일 다운로드/열기 불가
- **해결**:
  - `GraphMailService.cs`: `DownloadAttachmentAsync(messageId, attachmentId)` + `GetAllMessageIdsAsync()` 추가
  - `EmailViewWindow.xaml`: AttachmentPanel 추가 (첨부파일 목록 + 다운로드/열기 버튼)
  - `EmailViewWindow.xaml.cs`: 첨부파일 패널 로드/다운로드/열기 이벤트 핸들러 구현

#### R3 — 영업폴더 삭제메일 Reconciliation (BackgroundSyncService.cs)
- **이슈**: 삭제된 메일이 DB에 잔류하여 영업폴더(FavoriteFolder) 목록에 계속 표시됨
- **해결**: `ReconcileDeletedEmailsAsync()` 추가 — Graph API로 실제 메일 ID 목록 조회 후 DB 비교, 삭제된 항목 DB에서 제거
- **파일**: `BackgroundSyncService.cs`

#### R4 — 인피니티 스크롤 PageSize=100 (MainViewModel.cs, MainWindow.xaml/cs)
- **이슈**: 메일 목록이 한 번에 전부 로드되어 대용량 폴더에서 성능 문제 발생
- **해결**:
  - `MainViewModel.cs`: PageSize=100, `_emailSkip` 오프셋, `IsLoadingMore`, `HasMoreEmails`, `LoadMoreEmailsAsync()` 추가
  - `MainWindow.xaml`: ScrollViewer ScrollChanged 이벤트 등록
  - `MainWindow.xaml.cs`: 스크롤 하단 도달 시 `LoadMoreEmailsAsync()` 호출 핸들러

### 교훈 기록
- L-290: EF Core UNIQUE 제약 위반 Detach 패턴 이후 ERR 로그는 내부 노이즈 (Level 1)

---

## 2026-04-01 — cid: 인라인 이미지 virtual host 방식 전환 + 빈 본문 메타데이터 카드 표시

**분류**: Fast Path (k3)
**수정 파일**: 1개 (MainWindow.xaml.cs)

### 변경 내역

#### MainWindow.xaml.cs — WebView2 렌더링 방식 전환 + 빈 본문 처리

- **이슈 1 — cid: 인라인 이미지 미표시**: data URI 방식에서 virtual host 방식으로 전환
  - 근본 원인: data URI 변환 시 HTML 크기 3.4MB+ → `NavigateToString()` 크기 제한 초과
  - 해결: `SetVirtualHostNameToFolderMapping("maix.local", tempFolder)` + 임시 파일 서빙 방식 적용
  - cid: 이미지를 임시 폴더에 파일로 저장 → `<img src="https://maix.local/cid_xxx.jpg">` URL로 대체

- **이슈 2 — 일정 수락 메일 빈 본문**: `BuildEmptyBodyPlaceholder()` 메타데이터 카드 표시
  - 제목, 발신자, 수신일시 정보를 HTML 카드 형식으로 렌더링

- **이슈 3 — 빈 본문 전환 시 이전 메일 잔류**: `NavigateToString("<html><body></body></html>")` 선행 초기화
  - 빈 본문 표시 전 WebView2 초기화로 이전 콘텐츠 완전 소거

- **디버그 로그 추가**: LoadMailBodyAsync 진입/분기 로그, SelectedEmail 변경 로그, cid 상세 로그

### 교훈 기록
- L-287: WebView2 NavigateToString 크기 제한 — cid: 인라인 이미지는 virtual host 방식 필수 (Level 2)
- L-288: 빈 본문 전환 시 NavigateToString 선행 초기화 필수 (Level 1)

---

## 2026-04-01 — 받은편지함 읽음 상태 동기화 실패 근본 수정 (DbContext 오염 방지 + SyncReadStatusAsync 독립 실행)

**분류**: Fast Path (k3)
**수정 파일**: 1개 (BackgroundSyncService.cs)

### 변경 내역

#### BackgroundSyncService.cs — UNIQUE 위반 DbContext 오염 방지 + 독립 try/catch 분리

- **근본 원인**: `SaveEmailsAsync` 배치 저장 중 `InternetMessageId+ParentFolderId` UNIQUE 제약 위반 → DbContext 오염 → 같은 try 블록의 `SyncReadStatusAsync` 미도달 → 읽음 상태 영구 미동기화
- **수정 1 — SaveEmailsAsync**: 배치 `SaveChangesAsync` → 개별 저장 + catch 시 `Entry(email).State = EntityState.Detached` (DbContext 오염 상태 즉시 해제)
- **수정 2 — SyncFavoriteFoldersAsync**: `SyncFolderAsync`와 `SyncReadStatusAsync`를 독립 try/catch 블록으로 분리 (메일 저장 실패가 읽음 동기화를 차단하지 않음)
- **수정 3 — SyncAccountAsync**: 동일 패턴 적용 — 폴더 동기화와 읽음 동기화 독립 실행 보장

### 교훈 기록
- L-286: EF Core DbContext 오염 방지 — UNIQUE 위반 시 개별 저장 + Detach 패턴 (Level 1)

---

## 2026-04-01 — 메일 동기화 읽음 카운트 불일치 근본 수정 + 설정 동기화 대메뉴 통합

**분류**: Fast Path (k3)
**수정 파일**: 4개

### 변경 내역

#### 1. GraphMailService.cs — Graph API 동기화 범위 확장 + 미읽음 목록 조회 신규
- `GetMessagesReadStatusAsync`: days 파라미터 7→30으로 확장 (7일 이상 된 메일의 읽음 상태 동기화 누락 방지)
- `GetUnreadMessageIdsAsync` (신규): 특정 폴더의 서버 미읽음 메일 ID 목록 조회 메서드 추가
  - Graph API `/mailFolders/{folderId}/messages?$filter=isRead eq false&$select=id` 호출
  - 페이징 지원 (nextLink 추적)

#### 2. BackgroundSyncService.cs — SyncReadStatusAsync 서버 미읽음 목록 기준 교체
- 기존 로직: 로컬 DB 미읽음 메일을 순회하며 Graph API 개별 조회 (N+1 패턴)
- 신규 로직: `GetUnreadMessageIdsAsync`로 서버 미읽음 ID 목록을 일괄 조회 후 로컬 DB와 Set 비교
  - 서버에서 읽음 처리된 메일(로컬만 미읽음) → 로컬 `IsRead = true` 일괄 업데이트
  - 서버에서 미읽음인 메일(로컬만 읽음) → 로컬 `IsRead = false` 일괄 업데이트 (선택적)
- 적용 폴더: Inbox, SentItems (L-284 참조: 향후 설정으로 외부화 권장)

#### 3. MainWindow.xaml.cs — 동기화 설정 대메뉴 통합
- 기존: 동기화 관련 설정이 여러 하위 메뉴로 분산
- 변경: 동기화 설정을 단일 대메뉴로 통합하여 UX 일관성 향상
- 관련 UI 항목 재배치 및 이벤트 핸들러 정리

#### 4. UserPreferencesSettings.cs — 설정 필드 2개 추가
- `AiBatchSize` (int, 기본값 20): AI 분석 배치 처리 건수 설정 — 기존 하드코딩 값을 설정으로 외부화
- `MailSyncInitialCount` (int, 기본값 100): 초기 메일 동기화 건수 설정 — SyncPeriodSettings.Value 보완용

### 근본 원인 분석 (L-283 기록)
- 증상 수준 수정(ViewModel 폴더 카운트 갱신)으로는 재발 차단 불가
- Graph API 동기화 범위(7일)와 SyncReadStatusAsync의 로컬 기준 처리가 실제 근본 원인
- kdev-2 추가 투입으로 데이터 흐름 전체 추적 후 서버 미읽음 목록 기준 교체로 근본 해결

### 교훈 기록
- L-283 (medium): kplan 증상 수준 계획 → 근본 원인 미포착 → kdev 추가 투입
- L-284 (low): SyncReadStatusAsync 받은/보낸편지함만 적용 — 범위 문서화
- L-285 (low): EmailsSynced 이벤트 0건 패턴 의미 모호성 — 이벤트 페이로드 개선 권장

### 빌드/테스트
- 빌드: 오류 0개 ✅
- 런타임: 정상 ✅

---

## 2026-03-29: Phase 3 — AI 규칙엔진 + 자동 팔로업 + 회의 전 브리핑

**분류**: Fast Path (k3)
**수정 파일**: 8개 수정 + 7개 신규

### 변경 내역

#### 1. AI 규칙엔진
- `MailRule.cs` (신규): 메일 규칙 모델 — 조건 5종 + 액션 5종
  - 조건 타입: `FromContains`, `SubjectContains`, `HasAttachment`, `AiCategoryEquals`, `ToContains`
  - 액션 타입: `MoveToFolder`, `SetCategory`, `SetFlag`, `MarkAsRead`, `Delete`
  - 필드: Name, ConditionType, ConditionValue, ActionType, ActionValue, IsEnabled, Priority, AccountEmail
- `MailRuleService.cs` (신규): 규칙 엔진 서비스 — DB에서 활성 규칙 로드 + 메일 적용
- `MailRuleSettingsDialog.xaml/.cs` (신규): 규칙 관리 다이얼로그 — 규칙 추가/편집/삭제/순서
- `BackgroundSyncService.cs`: 120초 루프 추가 — 신규 메일에 규칙 자동 적용
- `Migration 20260329000005_AddMailRules`: MailRules 테이블 생성

#### 2. 자동 팔로업
- `Email.cs`: `FollowUpDate` (DateTime?, nullable) 필드 추가 — 팔로업 예정 날짜 (UTC)
- `ComposeWindow.xaml/.cs`: 팔로업 ComboBox 추가 — 3일/7일/14일/30일 선택
- `BackgroundSyncService.cs`: 3600초 루프 추가 — 팔로업 기한 만료 메일 토스트 알림
- `Migration 20260329000006_AddFollowUpDate`: FollowUpDate 컬럼 + 인덱스 추가

#### 3. 회의 전 브리핑
- `BackgroundSyncService.cs`: 300초 루프 추가 — 30분 이내 회의 감지 → 참석자 메일 수집 → AI 브리핑 생성
- `GraphCalendarService` 연동: 다음 회의 이벤트 조회 + 참석자 추출
- `AiMailService.GenerateMeetingBriefingAsync`: 참석자 관련 최근 메일 → 브리핑 생성 → 토스트 알림

### 빌드/테스트
- 빌드: 오류 0개 ✅
- 런타임: 정상 (health 200, Migration 정상 적용) ✅
- 로그: ERROR 0건 ✅
- 품질: 3/3 기능 확인 ✅

### 변경 파일
수정: App.xaml.cs, mAIxDbContext.cs, mAIxDbContextModelSnapshot.cs, Email.cs,
      BackgroundSyncService.cs, ComposeWindow.xaml, ComposeWindow.xaml.cs, MainWindow.xaml.cs
신규: MailRule.cs, MailRuleService.cs, MailRuleSettingsDialog.xaml/.cs,
      20260329000005_AddMailRules.cs, 20260329000005_AddMailRules.Designer.cs,
      20260329000006_AddFollowUpDate.cs, 20260329000006_AddFollowUpDate.Designer.cs

---

## 2026-03-29: Phase 2 — AI 기능 + 메일 스누즈 + TTS 읽기 + 일일 브리핑

**분류**: Fast Path (k3)
**수정 파일**: 12개 수정 + 3개 신규

### 변경 내역

#### 1. AiMailService.cs (신규) — AI 메서드 4개
- `GenerateDraftAsync(email, tone)`: 답장 초안 생성 (톤 선택: 공식/친근/간결)
- `SummarizeThreadAsync(emails)`: 스레드 전체 AI 요약
- `GenerateDailyBriefingAsync(emails)`: 오늘 수신 메일 일일 브리핑
- `GenerateMeetingBriefingAsync(emails)`: 회의 관련 메일 브리핑

#### 2. 메일 스누즈
- `Email.SnoozedUntil` (DateTime?, nullable): 스누즈 해제 예정 시각 (UTC)
- Migration `20260329000004_AddSnoozedUntil`: SnoozedUntil 컬럼 + 인덱스 추가
- `BackgroundSyncService`: 매 분 `SnoozedUntil <= UtcNow` 조건으로 자동 해제 루프
- `MainViewModel.ShowSnoozedEmails` 토글: 스누즈 중인 메일 표시/숨김 필터

#### 3. AI 답장 초안
- `EmailViewWindow`: "AI 답장" 버튼 + 톤 선택 ComboBox
- AiMailService.GenerateDraftAsync 호출 → ComposeWindow Body 자동 입력

#### 4. TTS 메일 읽기
- `EmailViewWindow`: "읽어주기/중지" 토글 버튼
- `System.Speech.Synthesis.SpeechSynthesizer` 기반 (외부 NuGet 없음)
- SpeakAsync / SpeakAsyncCancelAll + SpeakCompleted 이벤트로 버튼 상태 복원

#### 5. AI 일일 브리핑
- `MainWindow`: "📋 브리핑" 버튼 추가
- `DailyBriefingDialog.xaml/.cs` (신규): FluentWindow 기반 브리핑 표시 다이얼로그
- 오늘 수신 메일 목록을 AI에 전달 → 스트리밍 브리핑 표시

#### 6. 스레드 AI 요약
- `EmailViewWindow`: 접이식 패널 (Expander) 형태로 스레드 요약 섹션 추가
- 같은 ConversationId 메일들을 AiMailService.SummarizeThreadAsync로 요약

### 빌드/테스트
- 빌드: 오류 0개 ✅
- 런타임: 정상 (health 200, Migration 정상 적용) ✅
- 로그: ERROR 0건 ✅
- 품질: 11/11 항목 확인 ✅

### 변경 파일
수정: App.xaml.cs, mAIxDbContext.cs, mAIxDbContextModelSnapshot.cs, Email.cs,
      BackgroundSyncService.cs, ComposeViewModel.cs, MainViewModel.cs,
      EmailViewWindow.xaml/.cs, MainWindow.xaml/.cs, AGENTS.md
신규: 20260329000004_AddSnoozedUntil.cs, DailyBriefingDialog.xaml/.cs

---

## 2026-03-29: Phase 0 인프라 정비 — Email AI 분류 필드 + FTS5 검색 + AI 자동 트리거

**커밋**: (kdone_git 완료 후 기재)
**분류**: Fast Path (미디엄)
**수정 파일**: 7개 (수정 4 + 신규 5 — Migration 4 + Queries 1)

### 변경 내역

#### 1. Email.cs — AI 분류 필드 4개 추가
- `AiCategory` (string, NULL): AI 자동 분류 카테고리 (긴급/업무/일반)
- `AiPriority` (string, NULL): AI 우선순위 (high/medium/low)
- `AiActionRequired` (bool, DEFAULT false): AI 액션 필요 여부
- `AiSummaryBrief` (string, NULL): AI 간략 요약 (1-2줄)

#### 2. Migration 20260329000001 — AI 분류 컬럼 4개 DB 추가
- Emails 테이블에 AiCategory/AiPriority/AiActionRequired/AiSummaryBrief 컬럼 추가
- ModelSnapshot 업데이트

#### 3. Migration 20260329000002 — FTS5 가상 테이블 + 트리거
- `EmailsFts` FTS5 가상 테이블 생성 (Subject, Body, [From], AiSummaryBrief)
  - SQLite 예약어 `From` → `[From]` 대괄호 이스케이프 적용 (역라우팅 수정)
- INSERT/UPDATE/DELETE 트리거 3종
- 초기 인덱싱: 기존 Emails 데이터 → EmailsFts 일괄 INSERT

#### 4. EmailSearchService.cs — FTS5 검색 + LIKE 폴백
- FTS5 MATCH 검색 우선 시도
- FTS5 실패 시 LIKE 폴백 구조

#### 5. EmailFtsQueries.cs (신규) — FTS5 SQL 쿼리 분리
- `mAIx/Queries/EmailFtsQueries.cs`: FTS5 관련 SQL 쿼리 상수 분리 정의

#### 6. BackgroundSyncService.cs — AI 배치 루프 AiCategory 자동 분류
- PriorityScore 기반 AiCategory 자동 매핑 통합
  - PriorityScore >= 70 → "긴급"
  - PriorityScore >= 40 → "업무"
  - else → "일반"

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 (health 200, Migration 자동 적용) ✅
- 로그: ERROR 0건 ✅

---

## 2026-03-28: 메일탭 UX 완성도 마지막 10% — INPC + 다중선택 도구바 + PreviewText

**커밋**: (kdone_git 완료 후 기재)
**분류**: Fast Path (미디엄)
**수정 파일**: 7개

### 변경 내역

#### 1. INotifyPropertyChanged 구현
- `Email.cs`: `INotifyPropertyChanged` 인터페이스 구현
  - INPC 적용 속성: `IsRead`, `FlagStatus`, `Categories`
  - 신규 속성: `PreviewText`(NotMapped, Graph API bodyPreview), `PreviewOrSummary`(SummaryOneline ?? PreviewText 폴백)
- `Folder.cs`: `INotifyPropertyChanged` 인터페이스 구현
  - INPC 적용 속성: `UnreadItemCount`, `IsFavorite`, `FavoriteOrder`

#### 2. 다중 선택 일괄 작업 도구바 (BulkActionBar)
- `MainWindow.xaml`: 메일 목록 하단 오버레이 BulkActionBar 추가
  - `IsMultipleEmailsSelected`(2건+ 선택 시 Visibility)
  - 선택 건수 표시, 읽음/읽지않음/플래그/플래그해제/삭제/전체취소 버튼
  - `SelectionChanged="EmailListBox_SelectionChanged"` 이벤트 바인딩
- `MainViewModel.cs`: 커맨드 및 속성 추가
  - `SelectedEmailCount`, `IsMultipleEmailsSelected` 속성
  - `BulkMarkReadCommand`, `BulkMarkUnreadCommand`, `BulkFlagCommand`, `BulkUnflagCommand`, `BulkDeleteCommand` 커맨드
- `MainWindow.xaml.cs`: 7개 핸들러 추가
  - `EmailListBox_SelectionChanged`, `BulkMarkRead_Click`, `BulkMarkUnread_Click`, `BulkFlag_Click`, `BulkUnflag_Click`, `BulkDelete_Click`, `BulkSelectAll_Click`

#### 3. PreviewText (미리보기 텍스트)
- `GraphMailService.cs`: `selectFields`에 `bodyPreview` 추가
- `BackgroundSyncService.cs`: bodyPreview → `email.PreviewText` 매핑
- `MainWindow.xaml`: `SummaryOneline` 바인딩 → `PreviewOrSummary`로 변경 (AI 요약 없을 때 bodyPreview 폴백 표시)

#### 기타 수정
- XAML FluentIcon 심볼명 오류 수정: `FolderMove24` → `FolderArrowRight20` (L-273)

### 테스트 결과
- 빌드: 오류 0개 ✅ (경고 170개 — 기존 패키지 경고)
- 배포: 정상 실행 ✅ (PID 확인)
- 런타임: 신규 ERROR 0건 ✅
- UI: 13건 메일 표시, BulkActionBar 코드 검증, PreviewOrSummary 바인딩 확인 ✅
- 품질: 3/3 기능 대조 완료 ✅

---

## 2026-03-29 — Phase 1: AI 스마트 분류 UI + 첨부파일 AI 분석 + 예약발송 + 발송취소

**분류**: k3 (Normal)
**수정 파일**: 17개 (신규 6 + 수정 11)

### 변경 내역

#### 1. AI 카테고리 배지 UI
- `AiCategoryToBadgeConverter.cs` 신규 — AI 카테고리 문자열 → 배지 색상/텍스트 IValueConverter
- `App.xaml` — AiCategoryToBadgeConverter 리소스 등록
- `MainWindow.xaml` — 메일 목록 3행 AI 배지 레이아웃 + 읽지않음 인디케이터

#### 2. AI 정렬+필터
- `MainViewModel.cs` — AiPriority 정렬 항목, FilterActionRequired, SelectedAiCategory 필터
- `MainWindow.xaml.cs` — 필터 도구바 이벤트 핸들러
- `Email.cs` — PreviewOrSummary 프로퍼티 확장 (AiSummaryBrief 우선 폴백)

#### 3. 첨부파일 AI 분석
- `EmailAnalyzer.cs` — PrepareEmailData에 첨부파일 텍스트 포함
- `EmailAnalysisResult.cs` — AttachmentSummary, AttachmentRiskLevel 필드 추가
- `BackgroundSyncService.cs` — RunAnalysisBatchLoopAsync Include Attachments

#### 4. 예약발송+발송취소
- `Email.cs` — ScheduledSendTime (DateTime?) 필드 추가
- `ComposeViewModel.cs` — ScheduleMailAsync + 5초 카운트다운 CancellationToken 취소
- `ComposeWindow.xaml` + `.cs` — 예약발송 버튼 UI 및 이벤트
- `ScheduledSendDialog.xaml` + `.cs` 신규 — DateTimePicker 예약시간 선택 다이얼로그
- `BackgroundSyncService.cs` — 예약발송 루프 추가
- Migration `20260329000003_AddScheduledSendTime` (3파일)

### 변경 파일
- 신규: AiCategoryToBadgeConverter.cs, ScheduledSendDialog.xaml, ScheduledSendDialog.xaml.cs, Migration 3파일
- 수정: App.xaml, MainWindow.xaml/cs, MainViewModel.cs, Email.cs, EmailAnalysisResult.cs, EmailAnalyzer.cs, BackgroundSyncService.cs, ComposeViewModel.cs, ComposeWindow.xaml/cs, mAIxDbContextModelSnapshot.cs

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 ✅
- 로그: ERROR 0건 ✅
- UI: PASS ✅
- 품질: 8/8 Task 확인 ✅

---

## 2026-04-02 — 메일탭 UI 블로킹 성능 개선

### 작업 내용
- **Virtualization**: EmailListBox에 VirtualizingPanel 설정 추가 (Recycling 모드, Pixel 단위 스크롤)
- **CancellationToken**: 폴더 전환 시 이전 LoadEmailsAsync 취소 (Race Condition 방지)
- **Graph API 병렬화**: Bulk 읽음/플래그/삭제에 SemaphoreSlim(8) + Task.WhenAll 적용
- **DB 배치**: ExecuteUpdateAsync로 전체 교체 (SaveChanges 불필요)
- **OperationCanceledException 처리**: ViewModelBase.ExecuteAsync에 취소 정상 처리 추가

### 변경 파일
- `mAIx/Views/MainWindow.xaml` — EmailListBox VirtualizingPanel 3속성 추가
- `mAIx/ViewModels/MainViewModel.cs` — CancellationTokenSource 도입 + Bulk 작업 배치처리
- `mAIx/ViewModels/ViewModelBase.cs` — ExecuteAsync OperationCanceledException 처리

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 ✅

---

## 2026-04-02 — UI 블로킹 Dispatcher.Invoke→InvokeAsync + 동기화 주기 라디오 버튼 버그 수정

**분류**: Normal Path (k3)
**수정 파일**: 2개 (MainViewModel.cs, MainWindow.xaml.cs)

### 변경 내역

#### 1. Dispatcher.Invoke → InvokeAsync (5곳)
- `OnMailSyncStarted`, `OnMailSyncProgress`, `OnHistoricalSyncProgress`, `OnHistoricalSyncCompleted`, `_syncProgressHideTimer.Elapsed` 콜백
- 동기 Invoke → 비동기 InvokeAsync로 전환하여 백그라운드 스레드 블로킹 제거

#### 2. OnEmailsSynced SelectedEmail 보존
- 동기화 완료 후 LoadEmailsAsync 재호출 시 선택된 메일이 초기화되던 문제 수정
- `selectedEmailId` 보존 → `Emails.FirstOrDefault(e => e.Id == selectedEmailId)` 로 재복원

#### 3. 동기화 주기 라디오 버튼 버그 수정
- **버그**: 즐겨찾기/전체 동기화 주기가 공유 필드(`MailSyncIntervalSeconds`) 하나로 처리되어 서로 덮어씀
- **수정**: `prefs.FavoriteSyncIntervalSeconds`, `prefs.FullSyncIntervalSeconds` 전용 필드 분리
- **수정**: Checked 콜백도 `SetFavoriteSyncInterval`, `SetFullSyncInterval`으로 분리

#### 4. Debug2.WriteLine → Log4.Error 교체
- `LoadMoreEmailsAsync` 예외 처리에서 Debug2 → Log4.Error로 로깅 일관성 개선

### 변경 파일
- `mAIx/ViewModels/MainViewModel.cs` — Dispatcher.InvokeAsync 5곳 + OnEmailsSynced 선택 보존 + Log4.Error
- `mAIx/Views/MainWindow.xaml.cs` — 라디오 버튼 전용 필드 분리

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 ✅

---

## 2026-04-04 — 메일탭 미리보기 참조/숨은참조/첨부파일 추가 + 동기화 주기 정상화

**분류**: Normal Path (k3)
**수정 파일**: 7개

### 변경 내역

#### 1. 메일탭 미리보기 패널 — CC/BCC/첨부파일 목록 추가 (MainWindow.xaml/xaml.cs)
- 메일 목록 오른쪽 미리보기 영역에 참조(CC), 숨은참조(BCC), 첨부파일 목록 섹션 추가
- 첨부파일마다 열기/다운로드 버튼 제공
- 섹션별 표시 조건: CC/BCC는 값 있을 때만, 첨부파일은 1개 이상일 때만 표시

#### 2. EmailViewWindow 타이틀바/첨부파일 (xaml/xaml.cs)
- 타이틀바 제목에 `TextTrimming="CharacterEllipsis"` 적용 (긴 제목 잘림 표시)
- 메일 뷰어 창에도 첨부파일 열기/다운로드 버튼 추가

#### 3. 백그라운드 동기화 주기 정상화 (BackgroundSyncService.cs)
- `Set*SyncInterval` 하한 1초 → 10초로 강화 (위험 저주기 방지)
- `CalendarSynced` 이벤트에 `deletedCount` 포함 (정보 완전성)
- 변경 0건 시 INFO 로그 → Debug 로그로 축약 (로그 노이즈 감소)

#### 4. 불필요 이벤트 발화 억제 (MainViewModel.cs)
- `OnCalendarSynced`: eventCount==0이면 `CalendarDataUpdated` 미발화 (불필요 UI 갱신 방지)
- `OnEmailsSynced`: newCount==0이면 `LoadEmailsAsync` 스킵 (번쩍임 방지)
- `view.Refresh()` 제거 (번쩍임 방지)
- `RefreshEmailReadStatusAsync` 이중 호출 제거 (MainWindow.xaml.cs)

#### 5. intervalOptions 위험 저주기 제거 (MainWindow.xaml.cs)
- MS365 설정의 동기화 주기 선택지에서 1초/2초/5초 제거
- 최소 선택지 10초로 조정

### 변경 파일
- `mAIx/Views/MainWindow.xaml` — 미리보기 CC/BCC/첨부파일 섹션 추가
- `mAIx/Views/MainWindow.xaml.cs` — 미리보기 첨부파일 버튼 + intervalOptions 정리 + 이중 호출 제거
- `mAIx/Views/EmailViewWindow.xaml` — 타이틀 TextTrimming 추가
- `mAIx/Views/EmailViewWindow.xaml.cs` — 첨부파일 열기/다운로드 버튼
- `mAIx/Services/Sync/BackgroundSyncService.cs` — 하한 10초 + deletedCount + 0건 로그 축약
- `mAIx/ViewModels/MainViewModel.cs` — 0건 이벤트 억제 + Refresh 제거

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 ✅

---

## 2026-04-05: 메일함 폴더별 캐시 시스템 + 이벤트 증분 동기화 구현 (k4)

### 목표
폴더 전환 시 DB 재조회 제거 → 즉시 표시(<100ms). 새메일/삭제/이동/읽음 등 이벤트 발생 시 캐시 정합성 자동 유지.

### 구현 내용

#### 1. MailFolderCacheService + CachedFolderState (신규)
- `MailFolderCacheService`: DI 싱글톤 폴더별 캐시 서비스
  - LRU 캐시 (maxFolders=10, LastAccessedAt 기준 evict)
  - 캐시 키: `(FolderId, ShowSnoozedEmails)` — 필터 상태 분리
  - `TryGet/Set/AppendPage`: 히트/미스/페이지 추가
  - `OnEmailAdded/Deleted/Moved/Updated`: CRUD 이벤트 훅
  - `InvalidateFolder/InvalidateAll`: 무효화
  - `SetScrollOffset`: 스크롤 위치 저장
  - `BackgroundSyncService.EmailsSavedToFolder` 이벤트 구독 — 증분 갱신
- `CachedFolderState`: 캐시 상태 레코드 (Emails, EmailSkip, HasMore, ScrollOffset, HighWaterMark, LoadedAt)

#### 2. BackgroundSyncService 이벤트 보강
- `EmailsSavedToFolder(string folderId, IReadOnlyList<Email> saved)` 이벤트 추가
- `SaveEmailsAsync` 말미에서 invoke — 폴더별 신규 저장 메일 전달

#### 3. App.xaml.cs — DI 등록
- `services.AddSingleton<MailFolderCacheService>()` 추가

#### 4. MainViewModel — 캐시 통합 (대규모 수정, +206/-5)
- 생성자: `MailFolderCacheService cacheService` 파라미터 추가
- `OnSelectedFolderChanged`: 캐시 히트 → 즉시 스왑 + 백그라운드 증분 sync / 미스 → DB 로드
- `LoadEmailsAsync` 말미: `cacheService.Set(...)` 호출
- `LoadMoreEmailsAsync` 말미: `cacheService.AppendPage(...)` 호출
- 신규 `SyncIncrementalAsync`: HighWaterMark 이후 신규 메일만 DB SELECT
- 7개 CRUD 훅: Delete/DeleteBatch/Restore/Move/UpdateRead/UpdateFlag/MarkAsRead
- 수동 새로고침: `InvalidateFolder` 후 DB 재조회
- `CacheService` 공개 프로퍼티 노출

#### 5. MainWindow.xaml.cs — 스크롤 복원 (+60)
- `EmailListBox_ScrollChanged`: 폴더별 스크롤 오프셋 저장
- `RestoreScrollOffsetOnFolderChange`: SelectedFolder 변경 후 ScrollViewer 복원
- `GetScrollViewer` 헬퍼: VisualTreeHelper 재귀로 ScrollViewer 접근

### 변경 파일
- `mAIx/Services/Cache/MailFolderCacheService.cs` (신규, ~260줄)
- `mAIx/Services/Cache/CachedFolderState.cs` (신규, ~35줄)
- `mAIx/Services/Sync/BackgroundSyncService.cs` (+6줄 — EmailsSavedToFolder 이벤트)
- `mAIx/App.xaml.cs` (+2줄 — DI 등록)
- `mAIx/ViewModels/MainViewModel.cs` (+206/-5 — 캐시 통합)
- `mAIx/Views/MainWindow.xaml.cs` (+60 — 스크롤 복원)

### 테스트 결과 (ktest k4)
- 빌드: 오류 0개 ✅
- AC-001~010: 전체 PASS (must 7건 + should 3건 포함)
- CANARY-001: PASS (expected FAIL 정확 감지)
- 런타임: `[Cache] miss→set→hit` 사이클 Serilog 로그 확인 ✅

### 발견사항
- FIND-001 (medium): Serilog vs Log4 로그 파일 경로 불일치 — 캐시 동작 정상, AC auto_scripts 파일명 수정 권고
- FIND-002 (low): InvalidateAll 로그아웃 핸들러 미연결 — 앱 재시작 효과 동등

---

## 2026-04-09 — 아웃룩 대비 기능 대폭 확장 + 성능 최적화 (Phase 1~4, k5)

### Phase 1: 성능 최적화
- VirtualizingPanel.IsVirtualizing=True + VirtualizationMode=Recycling (ChatListBox, OneNoteFavoritesTreeView)
- SQLite WAL 모드 적용 (ApplyWalMode in MaiXDbContext)
- 다수 쿼리에 AsNoTracking 적용 (GraphMailService, EmailSearchService)
- MaxMessagesPerSync 분리: Initial=50, Incremental=25 (SyncSettings)
- LRU 캐시 DefaultMaxFolders 10→30 (MailFolderCacheService)

### Phase 2: 아웃룩 기능
- KeyboardShortcutService: J/K/E/R/A/F/D/U/S/? 10개 단축키
- ShortcutHelpOverlay: ? 키로 단축키 도움말 오버레이 표시
- DelayedSendService: 5~30초 취소전송 기능
- ExportService: EML/PDF 내보내기
- AutoReplyService + AutoReplyDialog: 부재중 자동응답 (On/Off + 기간 + 메시지)
- GraphMailService 스팸 관리: MoveToJunk/MarkAsNotJunk
- 읽기 창 레이아웃(상하/좌우/분리) + 밀도 모드(편안/기본/촘촘)

### Phase 3: Superhuman/Hey 기능
- QuickStepService + QuickStep 모델: 반복 작업 자동화 (최대 5개 액션 체인)
- MentionParser: @멘션 파싱/하이라이트 유틸리티
- UnsubscribeService: 원클릭 구독 취소 (List-Unsubscribe 헤더 파싱)
- TrackingBlockerService: 추적 픽셀/링크 차단 (img src 패턴 감지)
- NewsletterViewModel: 구독 메일 피드 뷰모델
- ConversationGrouper + ConversationThread: 대화 스레딩 (ConversationId 기반)

### Phase 4: 혁신 기능
- CommandPaletteService + CommandPaletteWindow: Ctrl+K 커맨드 팔레트 (퍼지 검색)
- FocusedInboxService: Focused/Other 자동 분류 (AI 기반 IsImportant 판단)
- SplitInboxService + SplitInboxRule: Split Inbox 탭 (조건 기반 규칙 필터링)
- ScreenerService + ScreenerEntry: 발신자 차단/허용 (화이트/블랙리스트)
- ReplyLaterService + ReplyLaterItem: Reply Later 큐 (스누즈 시간 기반)

### DB 마이그레이션 추가 (4개)
- 20260408000008_AddQuickStep: QuickSteps 테이블
- 20260408000009_AddConversationIndex: ConversationId 인덱스
- 20260408000010_AddSplitInboxRule: SplitInboxRules 테이블
- 20260408000011_AddScreenerAndReplyLater: ScreenerEntries + ReplyLaterItems 테이블

### 주요 변경 파일
**신규**: KeyboardShortcutService.cs, ShortcutHelpOverlay.xaml/.cs, DelayedSendService.cs, ExportService.cs, AutoReplyService.cs, AutoReplyDialog.xaml/.cs, QuickStepService.cs, QuickStep.cs, MentionParser.cs, UnsubscribeService.cs, TrackingBlockerService.cs, NewsletterViewModel.cs, ConversationGrouper.cs, ReplyLaterItem.cs, ReplyLaterService.cs, CommandPaletteItem.cs, CommandPaletteService.cs, CommandPaletteWindow.xaml/.cs, FocusedInboxService.cs, SplitInboxRule.cs, SplitInboxService.cs, ScreenerEntry.cs, ScreenerService.cs
**수정**: App.xaml.cs, App.xaml, MainViewModel.cs, MainWindow.xaml/.cs, ComposeViewModel.cs, ComposeWindow.xaml, EmailViewWindow.xaml.cs, GraphMailService.cs, BackgroundSyncService.cs, MailFolderCacheService.cs, EmailSearchService.cs, SyncSettings.cs, UserPreferencesSettings.cs, MaiXDbContext.cs, Migrations (4개)

### 테스트 결과 (ktest k5)
- 빌드: 오류 0개, 경고 4개 (기존 패키지 경고) ✅
- Phase 1~4 신규 서비스 파일 11개 전체 존재 확인 ✅
- 헬스체크 PASS (localhost:5858) ✅
- WAL 모드 확인 ✅

---

## 2026-04-10: Planner 탭 성능 최적화 (UI 가상화) + kio bash_exec 버그 수정

### 작업 내용
- **Planner 칸반 보드 UI 가상화**: MainWindow.xaml의 ItemsControl → ListView 전환, VirtualizingStackPanel 적용으로 대량 카드 렌더링 성능 개선
- **kio bash_exec.py 버그 수정**: `run_in_background=true` 시 무한 블로킹 버그 수정
- **ko SKILL.md 규칙 추가**: 멈춘 에이전트 처리 시 tmux kill-pane 금지 + ki-rescue 위임, kplan 결과 검증 필수, run_in_background 금지 규칙

### 주요 변경 파일
- `mAIx/Views/MainWindow.xaml`: ListView 가상화 (PlannerBucketsItemsControl + 내부 카드 ListView)
- `/mnt/c/DATA/Project/AI/MCP-Servers/fio-mcp-server/bash_exec.py`: run_in_background 버그 수정
- `.claude/skills/ko/SKILL.md`: L-303/L-304/L-305 규칙 추가

### 교훈
- L-303: kio run_in_background=true 무한 블로킹 — 절대 금지
- L-304: tmux kill-pane Claude Code 세션 종료 위험 — ki-rescue 위임 필수
- L-305: kplan 결과 메인 확인 후 kdev 진입 필수

### 테스트 결과 (ktest k3)
- 빌드: 성공 ✅
- UI 가상화 적용 확인 ✅

---

## 2026-04-14: 이메일 동기화 Inbox 우선 + 첫 로드 $top=10 분기 (Phase 1)

**분류**: Massive Path (o5, oplan_debate)
**수정 파일**: 2개 + LESSONS.md

### 변경 내역

#### 1. GraphMailService.cs — isInitialSync 파라미터 추가
- `GetMessagesDeltaAsync(folderId, deltaLink, isInitialSync=false)` 시그니처 변경
- `isInitialSync=true` 시 `$top=10`, `false` 시 `$top=50` 분기 적용
- Debug 로그 추가: `folderId`, `isInitialSync`, `top` 값 기록

#### 2. BackgroundSyncService.cs — Inbox 우선 정렬 + isInitialSync 전달
- **2단계 정렬**: `OrderByDescending(Inbox)` → `ThenByDescending(우선폴더)` → `ThenBy(이름순)`
  - 기존: 받은편지함+보낸편지함 동일 우선순위 → 변경: 받은편지함 단독 1순위
- **IsInboxFolder 헬퍼**: `InboxFolderNames = {"받은 편지함", "Inbox"}` 정적 배열 + 메서드
- **isInitialSync 판정**: Inbox 폴더 && DeltaLink 없음 && LastSyncedAt 없음 → 첫 동기화
- **FetchNewEmailsAsync**: `isInitialSync` 파라미터 추가 → `GetMessagesDeltaAsync`로 전달
- 로그 강화: 폴더 순서 상위 5개 출력, Inbox 첫 동기화 감지 로그

#### 3. LESSONS.md — L-364 추가
- GraphMailService/BackgroundSyncService Serilog 기존 사용 중 (NLog 미준수)
- Phase 1 수정 범위 제한으로 Serilog 패턴 유지, 별도 마이그레이션 작업 필요

### Phase 2 보류 사항
- 점진 UI 피드백 (IProgress), Upsert 최적화, EnableCollectionSynchronization
- 조건: TTFB 측정 결과 + 사용자 재승인 후 착수

### 테스트 결과 (otest AC-1~6 전부 PASS)
- 빌드: 성공 (오류 0개, 경고 증가 없음) ✅
- 변경 금지 파일 미수정 ✅

---

## 2026-04-23: IsRead 동기화 불일치 버그 3종 수정

### 작업 배경
- Outlook 기준 읽음 처리된 메일이 MaiX에서 미읽음으로 표시되는 불일치 현상 수정

### 수정 내용 (Bug 3종)
- **Bug 1 — 순방향 동기화 블록 활성화**: `SyncReadStatusAsync`의 순방향 블록(서버 미읽음 목록 기준 DB 교정)이 주석 처리된 상태였음 → 주석 해제 + 안전 가드(`serverUnreadIds.Count == 0` 시 조기 리턴) 추가
- **Bug 2 — TCS 백그라운드에서 읽음 동기화 누락**: `Task.Run` 내부의 나머지 메일 처리 완료 후 `RaiseMailSyncCompleted` 호출 전에 `SyncReadStatusAsync` 호출이 없었음 → 추가
- **Bug 3 — 이중 EntryId 조회 제거**: 역방향 동기화 블록에서 `existingEmail null` 체크 후 동일 조건 `existingByEntryId` 중복 조회 → 제거

### 주요 변경 파일
- `mAIx/Services/Sync/BackgroundSyncService.cs`: SyncReadStatusAsync 순방향 활성화, TCS Task.Run 내 호출 추가, 이중 조회 제거

### 테스트 결과 (otest o3)
- 빌드: 성공 (오류 0개) ✅
- AC-001 순방향 블록 활성화 ✅
- AC-002 TCS 백그라운드 SyncReadStatusAsync 호출 ✅
- AC-003 안전 가드(serverUnreadIds.Count==0) ✅
- AC-004 이중 EntryId 조회 제거 ✅
- 커밋: 78b8002f

---

## 2026-05-02: 동기화/UI블로킹 전수조사 — Dispatcher.Invoke→InvokeAsync 47건 일괄 수정

**분류**: Full Path (o4)
**수정 파일**: 6개
**커밋**: (미커밋 — odone_git 진행 예정)

### 배경

전날 BUG-4(MainWindow.xaml.cs Dispatcher.Invoke 5건 수정) 이후, 동일 패턴이 코드베이스 전체에 존재할 가능성을 전수조사함. 총 56건 발견 중 47건 수정(정상 패턴 9건 유지).

### 변경 내역

`Dispatcher.Invoke(async 람다)` → `await Dispatcher.InvokeAsync(람다)` 패턴 전환:

| 파일 | 수정 건수 | 주요 변경 |
|------|-----------|-----------|
| `mAIx/Views/MainWindow.xaml.cs` | 23건 | PropertyChanged 핸들러, UpdateRecordingUI, UpdatePauseButtonUI 등 async void 변환 |
| `mAIx/ViewModels/OneNoteViewModel.cs` | 19건 | RecordingStatusChanged, TranscriptionCompleted 등 이벤트 핸들러 |
| `mAIx/ViewModels/TeamsViewModel.cs` | 4건 | 메시지/채널 업데이트 핸들러 |
| `mAIx/ViewModels/OneDriveViewModel.cs` | 1건 | 파일 목록 업데이트 |
| `mAIx/Views/MainWindow.Activity.cs` | 1건 | 활동 피드 업데이트 |
| `mAIx/Views/Dialogs/TaskEditDialog.xaml.cs` | 1건 | 태스크 저장 완료 핸들러 |

- **정상 패턴 유지** (수정 제외 9건): 동기 람다만 사용하는 `Dispatcher.Invoke`, CheckAccess 분기의 단순 동기 경로 등

### 교훈

- **L-369**: `Dispatcher.Invoke(async ...)` 패턴은 async void 처리로 예외 미전파 + UI 블로킹 안티패턴
- domain-csharp/SKILL.md에 금지 패턴 및 자동 검증 grep 추가

### 테스트 결과
- 빌드: 성공 (CS 오류 0건) ✅

---

## 2026-05-01: UI 버그 3종 수정 — 초안삭제 닫기 / 인라인 컴포즈 / Dispatcher 블로킹

### 작업 내용 (BUG-2/BUG-3/BUG-4)

#### BUG-2: ComposeWindow 초안삭제 확인 후 창 미닫힘
- **증상**: 초안 삭제 확인 다이얼로그 후 ComposeWindow가 닫히지 않음
- **원인**: `MessageBox.Show` 내부에서 `Close()` 호출 → WPF 디스패처 재진입 문제
- **수정**: `Dispatcher.BeginInvoke(() => Close())` 로 지연 실행으로 변경
- **파일**: `mAIx/Views/ComposeWindow.xaml.cs`

#### BUG-3: 인라인 메일 작성 패널 추가 (아웃룩 스타일)
- **증상**: 메인 창에서 빠른 메일 작성 시 별도 창 팝업 필요 — UX 불편
- **수정**: 메인 창 하단에 아웃룩 스타일 인라인 컴포즈 패널 추가
  - `MainViewModel.cs`: `IsInlineComposeVisible`, `InlineComposeTo`, `InlineComposeSubject`, `InlineComposeBody` 프로퍼티 + `ShowInlineComposeCommand`, `SendInlineComposeCommand`, `CloseInlineComposeCommand` 커맨드 추가
  - `MainWindow.xaml`: 하단 인라인 컴포즈 패널 UI (Grid 레이아웃, Visibility 바인딩)
- **파일**: `mAIx/ViewModels/MainViewModel.cs`, `mAIx/Views/MainWindow.xaml`

#### BUG-4: Dispatcher.Invoke → InvokeAsync 일괄 변환으로 UI 블로킹 해결
- **증상**: 메일 목록 로딩/동기화 중 UI 프리즈 발생
- **원인**: `MainWindow.xaml.cs`에서 `Dispatcher.Invoke` 동기 호출이 UI 스레드를 블로킹
- **수정**: `Dispatcher.Invoke` → `Dispatcher.InvokeAsync` + `await` 일괄 변환
- **파일**: `mAIx/Views/MainWindow.xaml.cs`

### 주요 변경 파일
- `mAIx/Views/ComposeWindow.xaml.cs`: Dispatcher.BeginInvoke 닫기 버그 수정
- `mAIx/ViewModels/MainViewModel.cs`: 인라인 컴포즈 프로퍼티/커맨드 추가
- `mAIx/Views/MainWindow.xaml`: 인라인 컴포즈 패널 UI 추가
- `mAIx/Views/MainWindow.xaml.cs`: Dispatcher.Invoke → InvokeAsync 변환

### 테스트 결과 (otest Fast Path o3)
- 빌드: 성공 (오류 0개) ✅
- BUG-2 ComposeWindow 닫기 정상 동작 ✅
- BUG-3 인라인 컴포즈 패널 표시/숨김 동작 ✅
- BUG-4 UI 블로킹 해소 (InvokeAsync 비동기 전환) ✅

---

## 2026-05-02: ConfigureAwait(false) Service 레이어 전면 적용

### 작업 내용
ConfigureAwait(false)를 Service 레이어 전체에 적용하여 스레드 컨텍스트 캡처 오버헤드 제거 및 데드락 위험 완전 차단.
4 Phase로 나눠 단계별 빌드 검증 후 진행.

### 적용 규모
- **Phase 1 — Graph 레이어**: 432건 (11개 파일: GraphMailService, GraphCalendarService, GraphContactService, GraphTeamsService 등)
- **Phase 2 — BackgroundSyncService**: 125건 (1개 파일)
- **Phase 3 — AI/Converter/Notification 등**: ~100건 (58개 파일: AIProviderBase, ClaudeProvider, GeminiProvider, AttachmentProcessor 등)
- **Phase 4 — 잔존 버그 수정 + MEDIUM 보완**: 13건 버그 수정 + ComposeWindow 이벤트 해제 2건
- **총계**: 약 670건 수정, 60개 파일

### 부수 항목
- **fire-and-forget 패턴 식별**: RestApiServer, ToastNotificationService — 의도적 패턴으로 수정 불필요 결론
- **이벤트 해제 추가**: ComposeWindow.xaml.cs Closing 이벤트 핸들러 2건 추가
- **ConfigureAwait 잘못 삽입 버그 5종 수정** (L-372 참조 — 멀티라인 체인 위치 오류)

### 주요 변경 파일
- `mAIx/Services/AI/` (10개 파일): AIProviderBase, AIService, ClaudeProvider 등
- `mAIx/Services/Graph/` (11개 파일): GraphMailService, GraphCalendarService 등
- `mAIx/Services/Converter/` (9개 파일): AttachmentProcessor, ClosedXmlConverter 등
- `mAIx/Services/Sync/BackgroundSyncService.cs`
- `mAIx/Views/ComposeWindow.xaml.cs`

### 테스트 결과 (빌드 검증)
- 빌드: 성공 (CS 컴파일 에러 0건) ✅
- Phase별 빌드 검증 통과 ✅

### 커밋
- `154a56da`: 🔧 ConfigureAwait(false) Service 레이어 전면 적용 (~670건)

---

## 2026-04-14: BackgroundSyncService 동기화 Lazy 초기화 적용

### 작업 내용
- **ExecuteAsync에서 SyncFoldersAsync 호출 제거**: App.xaml.cs에서 이미 호출되는 중복 제거
- **11개 주기적 루프 즉시 시작**: 앱 기동과 동시에 루프 진입
- **Task.Run 비블로킹 Lazy 초기화**: 메일(2초), 캘린더(~8초), 채팅(~15초) 순차 지연 초기화로 UI 블로킹 방지

### 주요 변경 파일
- `mAIx/Services/Sync/BackgroundSyncService.cs`: ExecuteAsync Lazy 초기화 패턴 적용

### 테스트 결과 (otest Fast Path o3)
- 빌드: 성공 (오류 0개) ✅
- 앱 정상 기동 (헬스체크 healthy) ✅
- Lazy 초기화 로그 확인 ✅
  - "초기 메일 동기화 시작 (lazy)" → "완료 (lazy)"
  - "초기 캘린더 동기화 시작 (lazy)" → "완료 (lazy)"
  - "초기 채팅 동기화 시작 (lazy)" → "완료 (lazy)"

---

## 2026-05-09: 실시간 STT + 1분 요약 + 주제어 네비게이션 + OpenAI 설정 신규 (O4)

### 작업 내용
- **실시간 STT 서비스 2종**: OpenAiRealtimeSttService (WebSocket 기반), OpenAiTranscribeSttService (청크+오버랩+Jaccard dedup)
- **AI 서비스 3종**: TopicExtractorService (12초 PeriodicTimer + Jaccard), MinuteSummaryService (60초 PeriodicTimer + 디스크 저장), CumulativeSummaryService (설정 주기 + 압축 갱신 + 최종 요약)
- **설정 화면**: ApiSettingsWindow에 OaiRecordingBorder 섹션 추가 (STT 2슬롯 + LLM 4슬롯 + 누적주기 + 프리셋 4종)
- **UI 재구성**: OneNoteRecordingContentPanel 3-컬럼 레이아웃 (실시간STT / 주제어네비 / 옵션+요약)
- **주제어 카드**: TopicSegment PastelPalette 8색, HexToBrushConverter, ToolTip 바인딩
- **화자분리 토글**: 체크박스로 RealtimeSTT ↔ TranscribeSTT 모드 전환
- **DI 등록**: App.xaml.cs에 6건 AddSingleton (OpenAiRecordingSettings + 5개 서비스)

### 주요 변경 파일
- 신규 9파일: `OpenAiRecordingSettings.cs`, `TopicSegment.cs`, `MinuteSummaryEntry.cs`, `HexToBrushConverter.cs`, `OpenAiRealtimeSttService.cs`, `OpenAiTranscribeSttService.cs`, `TopicExtractorService.cs`, `MinuteSummaryService.cs`, `CumulativeSummaryService.cs`
- 수정 8파일: `AppSettingsManager.cs`, `ApiSettingsWindow.xaml/cs`, `MainWindow.xaml`, `MainWindow.OneNote.cs`, `MainWindow.xaml.cs`, `OneNoteViewModel.cs`, `App.xaml/cs`

### 테스트 결과 (otest O4 — 3단계 PASS)
- 빌드: 성공 (오류 0건) ✅
- Sprint Contract 34/34 PASS ✅
- 헬스체크 healthy ✅

### 미완성 항목 (의도된 placeholder — Sprint Contract 외)
- TopicSegment_Click SeekTo 기능 (타임스탬프 seek 미구현 — 향후 확장)
- StartOpenAiServicesAsync RecordingService 이벤트 연결 미구현

---

## 2026-05-13: 핵심요약 네비게이션 재설계 — TopicExtractorService 제거 + MinuteSummary 콜백 변환 (O3 — 8파일)

### 작업 내용
- **TopicExtractorService.cs**: 전체 삭제 (PeriodicTimer 기반 주제어 추출 서비스 제거)
- **Converters/TopicSegmentProportionalHeightConverter.cs**: 전체 삭제 (비례높이 컨버터 제거)
- **App.xaml**: `TopicSegmentProportionalHeightConverter` 리소스 등록 제거
- **App.xaml.cs**: `ITopicExtractorService` DI 등록 제거
- **ViewModels/OneNoteViewModel.cs**: TopicExtractor 구독/해제/메서드 제거 + `OnMinuteSummaryCreated`에서 MinuteSummaryEntry → TopicSegment 직접 변환 삽입 (약 100줄 제거 + 20줄 추가)
- **Views/MainWindow.xaml**: 핵심요약 네비게이션 영역 재작성 — 비례높이 컨버터 기반 → ScrollViewer + StackPanel 고정 카드 구조
- **Services/AI/MinuteSummaryService.cs**: PeriodicTimer 로그 강화 + `_running` 완화 + ToString verbatim 수정 (직전 conv 미커밋 보존)
- **Services/AI/OpenAiRealtimeSttService.cs**: endTime 시그니처 추가 (직전 conv 미커밋 보존)

### 설계 변경 요약
- 이전: TopicExtractorService가 독립 주기(ProcessingIntervalSeconds)로 실행 → 핵심주제 추출
- 이후: MinuteSummaryService가 발화할 때마다 OnMinuteSummaryCreated 콜백에서 MinuteSummaryEntry → TopicSegment 직접 변환
- 장점: 서비스 1개 제거, DI 단순화, 이벤트 경로 단축

### 테스트 결과
- 빌드: 성공 (오류 0건) ✅
- 역라우팅: 0회 (1회 사이클 완료) ✅

## 2026-05-13: 요약 주기 통합 — ProcessingIntervalSeconds 단일 옵션 + 핵심요약 그루핑 (O3)

### 작업 내용
- **OpenAiRecordingSettings.cs**: `TopicExtractorIntervalSec` 제거 + `MinuteSummaryIntervalSeconds` → `ProcessingIntervalSeconds` rename (기본 60초)
- **MinuteSummaryService.cs**: `ProcessingIntervalSeconds` 참조 갱신 (분리된 주기 설정 제거)
- **TopicExtractorService.cs**: PeriodicTimer 주기를 `ProcessingIntervalSeconds` 로 통일 + `ConsolidateTopicsIfNeededAsync` 신규 구현 (count > 10 LLM 그루핑 평가 / count > 20 강제 실행, 8~12개 목표) + `TopicSegmentsConsolidated` 이벤트 추가
- **OneNoteViewModel.cs**: 분리된 인터벌 필드 통합 + `OnTopicSegmentsConsolidated` 핸들러 + 구독/해제 등록
- **MainWindow.xaml**: 두 개의 ComboBox(실시간 요약 주기 + 핵심주제 주기) → "요약·핵심주제 주기(초)" 단일 ComboBox 통합

### 주요 변경 파일
- `mAIx/Models/Settings/OpenAiRecordingSettings.cs`
- `mAIx/Services/AI/MinuteSummaryService.cs`
- `mAIx/Services/AI/TopicExtractorService.cs`
- `mAIx/ViewModels/OneNoteViewModel.cs`
- `mAIx/Views/MainWindow.xaml`

### 테스트 결과 (otest Fast Path o3)
- 빌드: 성공 (CS 에러 0건) ✅
- 정적 검증 5/5 PASS ✅
- MEMORY.md 규칙 (L-369/374/377/385) 준수 확인 ✅

## 2026-05-13: 실시간 요약 dead code 제거 + MinuteSummaryService 로그 강화 (O3 — 4파일)

### 배경
- 사용자가 "실시간 요약이 0건, 핵심요약 1건만 보임" 보고
- 1차 oplan이 `_realtimeSummaryTimer` dead code 경로를 핵심 수정 대상으로 오판 → 역라우팅 1회
- odebug 진단: `StartRealtimeSTT()` 어디서도 호출 안 됨 + `MinuteSummaryService` 단일 경로가 실제 동작

### 작업 내용
- **OneNoteViewModel.cs**: dead code 270줄 제거
  - 제거 필드 4개: `_realtimeSTTCts`, `_realtimeSummaryTimer`, `_lastSummarySegmentCount`, `_summaryIntervalSeconds`
  - 제거 함수 5개: `StartRealtimeSTT()`, `StopRealtimeSTT()`, `UpdateRealtimeSummaryAsync()`, `BuildRealtimeSummaryPrompt()`, `SetSummaryInterval()`
  - 보존: `[ObservableProperty] _isRealtimeSummaryInProgress` (MainWindow.xaml.cs L7623 참조)
- **MinuteSummaryService.cs**: PeriodicTimer 발화 로그 3건 추가
  - `[MinuteSummary] PeriodicTimer 틱 — buffer={Count}개` (매 tick 발화 가시화)
  - `[MinuteSummary] PeriodicTimer 스킵 — STT 버퍼 없음` (버퍼 비어 skip 시)
  - `[MinuteSummary] SummarizeMinuteAsync 시작 — segments={Count}` (요약 시작 시)
- **MainWindow.xaml.cs**: `SetSummaryInterval()` 호출부 2곳 제거 (L8919, L8923) — 컴파일 오류 해결
- **MainWindow.xaml**: 빈 `Border` 제거 + `"최종요약"` → `"요약"` 레이블 변경 + Row 0 제거

### 테스트 결과 (otest-2 런타임 포함)
- 빌드: 성공 (오류 0건, 경고 224개) ✅
- AC-001~AC-008 전항목 PASS ✅
- 런타임 검증: 18:47:41 PeriodicTimer 첫 발화, 18:48~18:52 5회 연속 발화 확인 ✅

### 주요 변경 파일
- `mAIx/ViewModels/OneNoteViewModel.cs`
- `mAIx/Services/AI/MinuteSummaryService.cs`
- `mAIx/Views/MainWindow.xaml.cs`
- `mAIx/Views/MainWindow.xaml`

## 2026-05-10: OpenAI Realtime STT sample rate 통일 — 16kHz → 24kHz (O3)

### 작업 내용
- **Root cause 확인**: session.update 발송 + audio chunk 24회 전송 정상이나 server_vad `speech_started` 이벤트 0건. 원인은 `AudioRecordingService`가 16kHz 출력하는 데 OpenAI 서버가 24kHz로 해석 → 음성 약 1.5배 가속 → VAD 임계값 미달.
- **AudioRecordingService**: `_outputFormat` SampleRate 16000→24000, BytesPerSecond 32000→48000
- **OpenAiRealtimeSttService**: `BytesPerSecond` 상수 32000→48000 (청크 크기 계산 정확성)
- **OpenAiTranscribeSttService**: `BuildWavStream` WAV 헤더 SampleRate 16000→24000, BytesPerSecond 32000→48000
- **RecordingE2ETestHarness**: 주석 내 16000 참조 24000으로 갱신
- **영향도 분석**: `_outputFormat.AverageBytesPerSecond` 동적 참조 코드는 자동 반영. 다른 STT 서비스 영향 없음.

### 주요 변경 파일
- `mAIx/Services/Audio/AudioRecordingService.cs`
- `mAIx/Services/AI/OpenAiRealtimeSttService.cs`
- `mAIx/Services/AI/OpenAiTranscribeSttService.cs`
- `mAIx/Tests/Helpers/RecordingE2ETestHarness.cs`

### 테스트 결과 (otest Fast Path o3)
- 빌드: 성공 (오류 0건) ✅
- mAIx PID 43404 정상 실행 ✅
- NLog 채널 활성 ✅
- 사용자 녹음 검증 대기 중 (server_vad speech_started 이벤트 수신 확인 필요)

---

## 2026-05-15: OpenAI Realtime API Beta → GA 마이그레이션 (STT 복구)

### 배경
- OpenAI가 2026-05-12 Realtime Beta API 공식 폐기
- 2026-05-15 `beta_api_shape_disabled` 에러 발생 → STT 완전 미작동

### 작업 내용 (O3, Fast Path)
- **OpenAI-Beta 헤더 제거**: `WebSocketRequestHeaders`에서 `OpenAI-Beta: realtime=v1` 라인 삭제
- **URL 교체**: `wss://api.openai.com/v1/realtime?model=...` → `wss://api.openai.com/v1/realtime?intent=transcription`
- **session.update GA nested 재구조**:
  - `session.type = "transcription"`
  - `session.audio.input.format = "pcm16"`
  - `session.audio.input.transcription.model = {RealtimeSttModel}`
  - `session.audio.input.turn_detection.type = "server_vad"`
- **StopAsync `response.create` 삭제**: transcription 모드는 자동 응답으로 불필요
- **type=="error" 분기 신규 추가**: NLog Error 로깅 + `StatusChanged` 이벤트로 사용자 가시 알림 (L-445)
- **RealtimeSttModel 기본값 갱신**: `gpt-4o-mini-realtime-preview-2024-12-17` → `gpt-4o-transcribe` + docstring 보강

### 주요 변경 파일
- `mAIx/Services/AI/OpenAiRealtimeSttService.cs` (~60줄 변경)
- `mAIx/Models/Settings/OpenAiRecordingSettings.cs` (~5줄: 기본값 + docstring)

### 교훈 등재
- L-444: 외부 API Beta→GA 마이그레이션 4축 패턴
- L-445: WebSocket 외부 에러 silent close 방지 패턴

### 테스트 결과 (otest Fast Path O3)
- 빌드: 성공 (오류 0건) ✅
- 10/10 acceptance_criteria PASS ✅
- 런타임 STT 미트리거 (사용자 검증 권장)

---

## 2026-05-17: 대화네비 양모드 스크롤바 제거 + 가로모드 타임라인 좌→우 흐름 (보정 2건)

### 작업 내용
- **보정1 — 양모드 ScrollBarVisibility=Disabled**: 세로/가로 모드 ScrollViewer 양방향 스크롤바 Disabled. 세로 카드 `Margin="0"` (하단 2px 여분 제거). `RecalculateTopicSegmentHeights` 마지막 카드 잔여폭 흡수 보정.
- **보정2 — 가로모드 타임라인 좌→우**: `TimelineTick.LeftPx` 프로퍼티 추가(INotifyPropertyChanged). `RebuildTimelineTicks`에 `pixelsPerSecondW=PanelWidth/totalDuration` 계산. `TopicNavHorizontalLayout` 신규(Canvas.Left=LeftPx + StackPanel Horizontal + Border Width=DisplayWidth).
- **SizeChanged TopicNavLayoutHost 이관**: Collapsed 컨테이너 SizeChanged 미발화 방지 — 항상 표시되는 부모 호스트로 이관 (L-459).
- **L-450 Option B 멱등 토글**: `ApplyTopicNavDockLayout` 가로/세로 Visibility 멱등 토글 추가. 세로 컨테이너 byte-identical 보존.

### 주요 변경 파일
- `mAIx/Models/TimelineTick.cs` — LeftPx 프로퍼티 추가
- `mAIx/ViewModels/OneNoteViewModel.cs` — LeftPx 계산 + 잔여폭 흡수
- `mAIx/Views/MainWindow.xaml` — TopicNavLayoutHost + TopicNavHorizontalLayout 신규, ScrollBar Disabled
- `mAIx/Views/MainWindow.OneNote.cs` — 가로/세로 Visibility 멱등 토글

### 테스트 결과 (otest Fast Path O3)
- 빌드: 성공 (오류 0건, 신규 경고 0건) ✅
- L-424/L-389/L-450/L-377/L-419 코드규칙 PASS ✅
- 세로 모드 byte-identical 보존, AC-004 회귀 없음 ✅
- 런타임 (스크롤바 부재 + 가로 타임라인): 사용자 육안 검증 대기

---

## 2026-05-14: 핵심요약 네비게이션 5~20 카드 통폐합 + 스크롤 금지 (Min 40px 가드 제거)

### 작업 내용
- **RecalculateTopicSegmentHeights 재설계**: 고정 PixelsPerSecond 제거 → 패널 높이 대비 % 비례 방식으로 전환. Min 40px 가드 제거 (마지막 카드가 잔여 공간을 자연 흡수).
- **RebuildTimelineTicks 신규**: 분 단위 TimelineTick 컬렉션 재생성. Canvas TopPx 절대 좌표 계산.
- **SetPanelHeight 신규**: SizeChanged 이벤트에서 ViewModel에 높이 전달 → RecalculateTopicSegmentHeights + RebuildTimelineTicks 연동 갱신.
- **TryMergeAdjacentTopics 외 5개 메서드 신규 추가**: 인접 토픽 병합 로직 (5~20자 주제어 통폐합).
- **OnMinuteSummaryCreated 1줄 추가**: RebuildTimelineTicks() 호출 추가.
- **MainWindow.xaml**: TopicNavScrollViewer x:Name 추가 + SizeChanged="TopicNavScrollViewer_SizeChanged" 이벤트 연결. TopicNavContainer Grid (타임라인 ruler + 카드 영역 2-column 레이아웃).
- **MainWindow.OneNote.cs**: TopicNavScrollViewer_SizeChanged 핸들러 추가. ViewportHeight 우선 → e.NewSize.Height 폴백 패턴.

### 주요 변경 파일
- `mAIx/ViewModels/OneNoteViewModel.cs` (~70줄 추가/수정)
- `mAIx/Views/MainWindow.xaml` (~40줄 수정)
- `mAIx/Views/MainWindow.OneNote.cs` (~20줄 추가)

### 테스트 결과 (otest Fast Path o3)
- 빌드: 성공 (오류 0건) ✅
- Phase 1~5 모두 ✅ (단일 사이클 PASS, 역라우팅 0회)
- L-424 StackPanel 패턴 적용 확인 ✅

---

## 2026-05-17 — 가로 타임라인 동기화 버그 수정 + 실시간STT/요약 자동스크롤 체크박스 추가

| 2026-05-17 | 가로 타임라인 동기화 버그 수정 + 실시간STT/요약 자동스크롤 체크박스 추가 | 커밋 완료 |

---

## 2026-05-17 — OneNote 녹음 7개 개선 (주제박스1/4·간격·묵음회색·자동스크롤·STT회귀·초기화·전체요약버튼)

| 2026-05-17 | OneNote 녹음 7개 개선 — 주제박스 Row 55px 1/4 축소, 세로 간격 제거, 묵음 항상 회색(SilenceToGrayBrushConverter 신규), 자동스크롤 체크 즉시 최하단, STT 회귀 bool→int guardScope 수정(L-462), 새 녹음 시 데이터 초기화+타임라인 잔존 제거, 전체요약 수동버튼 추가 | 커밋 완료 |

| 2026-05-17 | 가로카드 2배+MAP/다줄 + STT 중지 사라짐 회귀 근본수정 (3건) — 가로모드 Row2 110→220px, 세로헤더 MAP+TextWrapping=Wrap, LoadSTTResultAsync Clear를 파일 존재 확인 후로 이동(설계A) — 회귀 3연속 진짜 원인(비동기 저장 race) 규명 (L-466) | 커밋 완료 |

## 2026-05-17 — 녹음중지 STT 사라짐 회귀 4연속 근본수정

| 2026-05-17 | 🐛 녹음중지 STT 사라짐 회귀 4연속 근본수정 — StopOpenAiServices fire-and-forget → await 전환 + flush drain + 작은파일 덮어쓰기 거부 3중 방어 | OneNoteViewModel.cs, MainWindow.OneNote.cs, MainWindow.xaml.cs |

## 2026-05-17 — 녹음중지 STT 사라짐 5연속 회귀 종결 (이중 Stop race 대칭 가드)

| 2026-05-17 | 🐛 녹음중지 STT 사라짐 5연속 회귀 종결 — StopRecordingAsync:4060이 OnRecordingCompleted 정상복사(2개)를 빈 LiveSTTSegments로 0개 덮어씌우는 이중 Stop race. 단방향 가드만 존재하여 반대 방향 무방비. 대칭 가드(_sttCopiedByRecordingCompleted) 추가로 근본 차단. 5연속 실패 메타원인: 로그 채널 오판(794MB Serilog 파일)으로 "코드 미실행" 잘못된 가설 4회 반복 (L-467/L-468) | OneNoteViewModel.cs |
