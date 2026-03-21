# LESSONS.md — MaiX 프로젝트 교훈 로그

## L-045: AI 프롬프트 negative examples 필수 (2026-02-17)

- **문제**: OneNote AI 분석에서 마커 카테고리명(★중요★, ⚠주의⚠) 도배 — 3회 반복 지적
- **근본원인**: 프롬프트에 올바른 예시만 제공, 금지 패턴(negative examples) 미명시 → AI가 카테고리명을 마커 안에 삽입
- **해결**: 프롬프트에 "잘못된 예시 (절대 금지)" 섹션 추가 + C# 렌더링에서 마커 기호 제거
- **교훈**: AI 프롬프트 작성 시 올바른 예시 + 금지 예시(negative examples) 반드시 함께 제공해야 준수율 향상
- **심각도**: 높음 (3회 반복)
- **수정 파일**: Resources/Prompts/*.txt 3개, MainWindow.xaml.cs

## L-046: 파이프라인 컨텍스트 복원 시 상태 전이 주의 (2026-02-17)

- **문제**: 컨텍스트 복원 후 kO→kPlan→kDev→kTest 재전이 시 kDev가 증거 파일 클리어, kO가 상태를 KO로 리셋
- **근본원인**: pipeline_gate.sh의 kDev 전이 시 증거 파일 삭제 로직 + kO의 상태 KO 리셋이 컨텍스트 복원 흐름과 충돌
- **해결**: team-lead가 수동으로 파이프라인 상태를 DEV로 설정 후 kTest 재실행
- **교훈**: 컨텍스트 복원 시 파이프라인 상태 전이를 최소화하고, 이미 완료된 단계의 재전이를 피해야 함
- **심각도**: 중간

## L-047: MaiX shutdown API에 Content-Length 헤더 필수 (2026-02-17)

- **문제**: POST /api/shutdown 호출 시 Content-Length 헤더 없으면 HTTP 411 Length Required
- **해결**: `-H "Content-Length: 0"` 추가
- **심각도**: 낮음

## L-048: 팀에이전트 파이프라인 상태 불일치 (2026-02-17)

- **문제**: 팀에이전트가 파이프라인 상태를 전환했으나 메인 세션에 전파되지 않음
- **근본원인**: 팀에이전트 idle 전환 시 /tmp/claude_pipeline_state가 초기화됨
- **해결**: 팀에이전트 작업 완료 후 team-lead가 수동으로 `echo "STATE" > /tmp/claude_pipeline_state` 복구 필요
- **교훈**: 팀에이전트는 파이프라인 상태를 변경할 수 없으므로, 스킬 호출 전 수동 복구 필수
- **심각도**: 중간
- **Level**: 2 (MEMORY.md 기록)

## L-049: NTFS Lock 파일 직접 생성 시도 (2026-02-17)

- **문제**: Lock 파일 생성 시에도 rsync 절차 필요하다는 인지 부족
- **근본원인**: 신규 파일도 NTFS 직접 Write 불가라는 규칙 미숙지
- **해결**: ko_check.sh hook이 차단 → rsync 절차 적용
- **교훈**: NTFS 경로의 모든 파일 생성/수정은 rsync 절차 준수 (신규 파일 포함)
- **심각도**: 낮음
- **Level**: 1 (참고용)

## L-050: 팀에이전트 파이프라인 복구 시 kDev 호출 전 상태를 PLAN으로 설정 (2026-02-17)

- **문제**: 팀에이전트에서 파이프라인 상태를 DEV로 수동 설정 후 kDev 호출 → pipeline_gate.sh 차단
- **근본원인**: pipeline_gate.sh는 kDev를 PLAN 상태에서만 허용 (PLAN→DEV 전이는 gate 내부 관리)
- **해결**: 수동 복구 시 `echo 'PLAN' > /tmp/claude_pipeline_state` 후 kDev 호출
- **교훈**: 파이프라인 상태 수동 복구 시 항상 이전 단계 상태로 설정 (gate가 전이를 관리하므로)
- **심각도**: 낮음
- **Level**: 1 (참고용)

## L-051: Wpf.Ui 프로젝트에서 MessageBox 관련 타입 fully qualified 필수 (2026-02-17)

- **문제**: `System.Windows.MessageBox.Show()` 호출 시 `MessageBoxButton`/`MessageBoxImage`를 미정규화하여 CS0104 빌드 에러
- **근본원인**: 참조 코드의 fully qualified 패턴을 불완전하게 복제 — `MessageBox`만 정규화하고 매개변수 타입 생략
- **해결**: `System.Windows.MessageBoxButton.OK`, `System.Windows.MessageBoxImage.Information`으로 fully qualified
- **교훈**: WPF UI 프로젝트에서 `System.Windows.MessageBox` 사용 시 매개변수(`MessageBoxButton`, `MessageBoxImage`)도 반드시 `System.Windows.` 접두사 포함
- **심각도**: 낮음
- **Level**: 1 (참고용)

## L-052: WasapiCapture useEventSync=true 시 AudioClient.Initialize ArgumentException (2026-03-15)

- **문제**: `new WasapiCapture()` 기본 생성자 사용 시 `AudioClient.Initialize`에서 `ArgumentException` 발생하여 녹음 불가
- **근본원인**: 기본 생성자의 `useEventSync=true`가 일부 오디오 디바이스에서 이벤트 동기화 모드 미지원
- **해결**: `new WasapiCapture(WasapiCapture.GetDefaultCaptureDevice(), useEventSync: false)`로 명시적 지정
- **교훈**: NAudio WasapiCapture 사용 시 `useEventSync: false`를 기본으로 지정하여 디바이스 호환성 확보
- **심각도**: 낮음
- **Level**: 1 (참고용)

## L-053: WasapiCapture.GetDefaultCaptureDevice() → MMDeviceEnumerator.GetDefaultAudioEndpoint 교체 (2026-03-15)

- **문제**: OneNote 탭 녹음 버튼 클릭 시 ArgumentException 발생
- **근본원인**: `WasapiCapture.GetDefaultCaptureDevice()`가 내부적으로 `DataFlow.All`로 디바이스 열거 → 특정 환경에서 캡처 전용이 아닌 디바이스 반환 가능
- **해결**: `MMDeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)`로 명시적 캡처 디바이스 지정
- **교훈**: NAudio에서 캡처 디바이스 획득 시 `GetDefaultCaptureDevice()` 대신 `MMDeviceEnumerator.GetDefaultAudioEndpoint()`를 사용하여 DataFlow와 Role을 명시적으로 지정
- **심각도**: 낮음
- **Level**: 1 (참고용)

## L-054: WasapiCapture Shared Mode에서 WaveFormat 강제 교체 금지 (2026-03-16)

- **문제**: WasapiCapture의 WaveFormat을 16bit PCM으로 강제 교체하면 `AudioClient.Initialize`에서 `E_INVALIDARG` 발생 → 녹음 버튼 클릭 시 아무 반응 없음
- **근본원인**: WASAPI Shared Mode는 Windows 오디오 믹서가 포맷을 결정하므로 클라이언트가 WaveFormat을 변경할 수 없음 (보통 48kHz 32bit float 2ch)
- **해결**: 캡처 장치 네이티브 포맷 유지 + `OnDataAvailable`에서 float→PCM, 스테레오→mono, 리샘플링(→16kHz) 후처리 변환
- **교훈**: WASAPI Shared Mode 녹음 시 WaveFormat을 절대 교체하지 말고, 후처리 파이프라인(float→PCM, 채널 다운믹스, 리샘플링)으로 원하는 출력 포맷을 얻어야 함
- **심각도**: 중간 (3건 연속 시도 실패 — L-051~L-053 관련)
- **Level**: 2 (인지 — MEMORY 반영)

## L-055: WASAPI 초기화 실패 시 MME(WaveInEvent) fallback 체인 필수 (2026-03-16)

- **문제**: WasapiCapture 생성자 파라미터(디바이스, bufferMs, useEventSync 등)를 3회 연속 변경 시도했으나 환경별 E_INVALIDARG 지속
- **근본원인**: WASAPI AudioClient.Initialize 성공 여부는 디바이스·드라이버·OS 버전에 따라 예측 불가 — 파라미터 조합 시행착오는 근본 해결 아님
- **해결**: WasapiCapture 기본 생성자(파라미터 없음) + StartRecording까지 try-catch 감싸고, 실패 시 WaveInEvent(MME API) fallback으로 전환
- **교훈**: 오디오 캡처 API 초기화 실패 시 같은 API의 파라미터를 반복 조정하지 말고, 다른 레벨의 API(WASAPI→MME)로 fallback 체인을 구현해야 근본 해결
- **심각도**: 중간 (4회 반복 수정 — L-051~L-054 관련)
- **Level**: 2 (인지 — MEMORY 반영)

## L-232: kTest spawn 프롬프트에 테스트 단계 생략 지시 금지 (2026-03-16)

- **문제**: kO가 kTest 에이전트 spawn 시 프롬프트에 "사용자 수동 테스트 예정", "배포 스킵" 등 테스트 단계 생략 지시를 포함하여 kTest가 build→deploy→run→quality 전 단계를 수행하지 못함
- **근본원인**: 메인/kO가 사용자 의도를 과도 해석하여 kTest에 테스트 축소 지시를 삽입
- **해결**: kO SKILL.md 제약 사항에 L-232 규칙 추가 — kTest spawn 프롬프트에 테스트 단계 생략 지시 금지
- **교훈**: kTest는 항상 독립적으로 전 단계(build/deploy/run/quality) 수행해야 하며, 상위 단계에서 테스트 범위를 축소하는 지시를 삽입해서는 안 됨
- **심각도**: 중간
- **Level**: 3 (강제 — kO SKILL.md 규칙 반영)

## L-239: NAudio WaveInEvent에 비표준 샘플레이트 하드코딩 금지 (2026-03-19)
- **문제**: WaveInEvent fallback에서 16000Hz 하드코딩 → Windows MME API SupportedWaveFormat에 16000Hz 매핑 없음 → waveInOpen에서 InvalidParameter 오류 → 녹음 버튼 무반응
- **근본원인**: NAudio WaveInEvent는 Windows MME 표준 포맷(8000/11025/22050/44100/48000Hz)만 지원. 16000Hz는 WAVE_FORMAT_* 상수에 미정의
- **해결**: GetBestWaveFormat(deviceNumber)으로 마이크가 실제 지원하는 포맷 자동감지 후 사용. 캡처 포맷이 출력 포맷(16000Hz)과 달라도 OnDataAvailable에서 리샘플링으로 처리
- **교훈**: WaveInEvent에 임의 샘플레이트를 지정하지 말 것. 항상 GetBestWaveFormat()으로 기기 지원 포맷을 조회하여 사용
- **심각도**: 높음 (녹음 기능 전체 불가)
- **Level**: 2 (코드 패턴 — GetBestWaveFormat 강제 사용)

## L-240: phase_guard.sh kInfra_* KO 전환 — 파이프라인 진행 중 덮어쓰기 금지 (2026-03-19)
- **문제**: kDone 프롬프트에 Skill('kInfra_maix') 포함 시 phase_guard.sh PostToolUse hook이 DONE 상태를 KO로 덮어씀 → pipeline_order_guard.sh가 KO 상태에서 DONE spawn을 차단 → kDone spawn 실패 + 좀비 pane 생성
- **근본원인**: phase_guard.sh의 kInfra_* 처리에 현재 상태 조건이 없어 IDLE/FINISH 이외 상태도 무조건 KO로 전환
- **해결**: phase_guard.sh kInfra_* 분기에 IDLE/FINISH 상태일 때만 KO 전환하는 조건 추가 (kO 분기와 동일한 조건)
- **교훈**: kDone 프롬프트에서 Skill('kInfra_*') 호출 제거로 회피 가능하나 hook 자체도 안전해야 함. IDLE/FINISH 이외 파이프라인 진행 중 상태는 어떤 스킬 로딩으로도 KO 전환 금지
- **심각도**: 높음 (kDone 진입 완전 차단 → 좀비 pane 생성)
- **Level**: 3 (강제 — phase_guard.sh hook 수정)

## L-241: WasapiCapture 버퍼 크기가 장치마다 다름 — 다단계 재시도 필수 (2026-03-19)
- **문제**: WasapiCapture() 기본 생성자(100ms 버퍼)가 SST 마이크 등 특정 장치에서 초기화 실패 → 녹음 시작 불가
- **근본원인**: 오디오 장치마다 허용하는 WASAPI 버퍼 크기가 다름. 기본 100ms가 모든 장치에서 동작하지 않음
- **해결**: WasapiCapture(device, false, bufferMs) 형태로 [200, 500, 50, 30]ms 다단계 버퍼 재시도 체인 구현. 첫 성공 시 break
- **교훈**: WasapiCapture 초기화 시 단일 버퍼 크기에 의존하지 말 것. 다양한 버퍼 크기로 재시도하여 장치 호환성을 확보해야 함
- **심각도**: 중간 (특정 마이크에서만 발생)
- **Level**: 2 (코드 패턴 — 다단계 버퍼 재시도 강제)

## L-242: WasapiCapture 다중 장치 탐색 + Serilog→Log4 전환 (2026-03-19)
- **문제**: WasapiCapture가 Communications Role 장치만 시도하여 해당 장치 실패 시 바로 WaveInEvent fallback으로 넘어감. Serilog 로그는 파일 출력 안 됨
- **근본원인**: 단일 장치(Communications Role)만 시도하고 Multimedia Role 및 다른 활성 장치를 탐색하지 않음. Serilog(_logger)는 콘솔 출력만 되고 Log4 파일 로그에 기록되지 않아 디버깅 불가
- **해결**: 3단계 장치 탐색(Communications→Multimedia→전체 활성 장치, HashSet으로 중복 제거) + 모든 로그를 Log4로 전환
- **교훈**: 오디오 캡처 시 단일 Role에 의존하지 말고 모든 활성 장치를 순회해야 함. 디버깅 필요한 로그는 반드시 파일 출력되는 Log4 사용
- **심각도**: 중간 (특정 환경에서 불필요한 MME fallback 발생)
- **Level**: 2 (코드 패턴 — 다중 장치 탐색 + Log4 로깅)

## L-243: WasapiCapture E_INVALIDARG는 포맷 검증 실패 — 버퍼 크기와 무관 (2026-03-19)

- **문제**: WasapiCapture "Value does not fall within the expected range" 예외에서 버퍼 크기만 조정했으나 근본 원인은 포맷 검증 단계 실패
- **근본원인**: E_INVALIDARG (0x80070057)는 IAudioClient.Initialize()의 포맷 검증 실패 — 버퍼 크기가 아닌 포맷/장치 호환성 문제
- **해결**: HResult 코드 + ExceptionType을 로그에 포함하여 정확한 원인 진단 가능하도록 개선 + bufferMs=0(장치 기본값) 추가
- **교훈**: COM 예외는 HResult 코드가 핵심 진단 정보 — Message 문자열만으로는 원인 특정 불가
- **심각도**: 중간 (디버깅 효율 저하)
- **Level**: 1 (참고)

## L-244: WaveInEvent GetBestWaveFormat 거짓 긍정 — USB/Bluetooth 런타임 상태 미반영 (2026-03-19)

- **문제**: WaveInEvent InvalidParameter 예외 — GetBestWaveFormat이 반환한 포맷으로 녹음 시작 실패
- **근본원인**: GetBestWaveFormat은 드라이버의 정적 Capabilities 정보 기반 — USB/Bluetooth 장치의 런타임 상태(연결 해제, 절전 모드 등)를 반영하지 않아 거짓 긍정 발생
- **해결**: MME fallback을 단일 포맷 시도에서 6개 포맷 순차 시도 루프로 변경 + GetBestWaveFormat 결과를 첫 번째 후보로 유지하되 실패 시 표준 포맷들로 재시도
- **교훈**: 오디오 드라이버의 "지원 포맷 조회"는 실제 사용 가능 여부를 보장하지 않음 — 반드시 try-catch로 감싸고 대체 포맷 준비
- **심각도**: 중간 (특정 환경에서 녹음 기능 전체 실패)
- **Level**: 1 (참고)

## L-245: 오디오 캡처 다중 포맷 fallback 패턴 (2026-03-19)

- **문제**: WASAPI 실패 후 MME fallback도 단일 포맷으로만 시도하여 복원력 부족
- **근본원인**: 오디오 장치마다 지원 포맷이 다르고, 런타임 상태에 따라 가용 포맷이 변동
- **해결**: WASAPI(다중 장치×다중 버퍼) + MME(GetBestWaveFormat + 6개 표준 포맷) 이중 fallback 체계 구축, 전체 실패 시 명확한 예외 throw
- **교훈**: 오디오 캡처는 "성공할 때까지 다음 조합 시도" 패턴이 필수 — 단일 설정 의존 금지
- **심각도**: 낮음 (패턴 기록)
- **Level**: 1 (참고)

## L-246: NAudio WasapiCapture COM 상태 오염 — ??= 패턴 금지 (2026-03-19)

- **문제**: 녹음 시작 시 `_recordingService ??= new`로 기존 인스턴스를 재사용하면 이전 실패의 COM 상태가 오염된 채 남아 후속 녹음도 실패
- **근본원인**: WasapiCapture 내부의 AudioClient COM 객체가 E_INVALIDARG(0x80070057) 실패 후 정리되지 않아 재초기화 불가
- **해결**: `_recordingService?.Dispose(); _recordingService = new`로 매번 새 인스턴스 생성
- **교훈**: COM 기반 오디오 서비스는 `??=` 패턴(null일 때만 생성) 금지 — 실패 후 반드시 Dispose+재생성
- **심각도**: 낮음 (패턴 기록)
- **Level**: 1 (참고)

## L-247: Intel SST WASAPI AUTOCONVERTPCM 시도해도 E_INVALIDARG 지속 — 다단계 폴백 필수 (2026-03-20)

- **문제**: Intel SST 마이크 드라이버에서 IAudioClient::Initialize가 E_INVALIDARG(0x80070057) 반환, AUTOCONVERTPCM|SRC_DEFAULT_QUALITY 플래그 추가해도 동일 실패
- **근본원인**: 드라이버 레벨 비호환 — AUTOCONVERTPCM은 포맷 변환만 지원하며, 드라이버가 Initialize 자체를 거부하는 경우 무력
- **해결**: WasapiNative.InitializeWithMixFormat를 4단계 폴백으로 확장 + MicrophoneTestService.StartMonitoring을 3단계 폴백(WASAPI→WasapiNative→MME)으로 개선
- **교훈**: AUTOCONVERTPCM은 만능이 아님 — 드라이버 거부 시 API 레벨 폴백(WASAPI→MME)이 최종 방어선
- **심각도**: 중간 (미해결 근본 원인 존재)
- **Level**: 2 (인지 — MEMORY 반영)

## L-248: WASAPI 모니터링에서 WasapiCapture 재생성은 동일 실패 재발 — COM 직접 호출이 안전 (2026-03-20)

- **문제**: TryStartNativeMonitoring에서 `new WasapiCapture(device)` 재호출 시 동일한 E_INVALIDARG 발생 — NAudio WasapiCapture 내부의 Initialize 호출이 동일 경로를 타기 때문
- **근본원인**: WasapiCapture는 내부적으로 AudioClient.Initialize를 호출하는데, 이미 L-247에서 확인된 Intel SST 드라이버 비호환이 동일 적용됨. NAudio 래퍼 사용 vs 직접 사용의 차이 없음
- **해결**: TryStartNativeMonitoring을 WasapiNative COM 직접 호출로 교체 — ActivateAudioClient→InitializeWithMixFormat(4단계 AUTOCONVERTPCM 폴백)→GetService(IAudioCaptureClient)→GetMixFormat→AudioClientStart→NativeCaptureLoop
- **교훈**: NAudio 래퍼(WasapiCapture)가 실패하는 디바이스에서는 동일 래퍼 재생성이 아닌 COM 직접 호출로 우회해야 함. 폴백 단계에서 같은 추상화 레이어를 재시도하는 것은 무의미
- **심각도**: 낮음 (패턴 기록)
- **Level**: 1 (참고)

## L-249: IAudioClient3 InitializeSharedAudioStream — Intel SST 저지연 공유 모드 폴백 (2026-03-20)

- **문제**: L-247/L-248에서 WASAPI+NativeAutoConvert로도 Intel SST 디바이스에서 E_INVALIDARG 발생 가능
- **해결**: IAudioClient3의 GetSharedModeEnginePeriod + InitializeSharedAudioStream을 WASAPI 실패 직후, NativeAutoConvert 전에 시도하는 2단계 폴백 추가
- **폴백 순서**: WasapiCapture(1) → IAudioClient3(2) → NativeAutoConvert(3) → WaveInEvent MME(4)
- **기술**: IAudioClient3 vtbl 오프셋 — GetSharedModeEnginePeriod=[18], InitializeSharedAudioStream=[20]. Activate 시 IID_IAudioClient3 사용
- **교훈**: 최신 WASAPI API(IAudioClient3)는 드라이버 네이티브 주기(defaultPeriod)를 사용하여 호환성이 더 높을 수 있음. 구형 IAudioClient Initialize 대신 IAudioClient3 SharedAudioStream을 먼저 시도하는 것이 효과적
- **심각도**: 낮음 (패턴 기록)
- **Level**: 1 (참고)

## L-250: IAudioClient::Initialize는 인스턴스당 1회만 호출 가능 — 실패 시 새 인스턴스 필수 (2026-03-20)

- **문제**: Intel SST 마이크에서 WASAPI 캡처가 완전히 실패. 동일 IAudioClient 인스턴스로 다른 flags/format으로 4번 재시도했으나 모두 실패
- **근본 원인**: COM 규약상 IAudioClient::Initialize()는 인스턴스당 단 1회만 호출 가능. 성공이든 실패든 1회 호출 후 내부 상태가 변경되어 재호출 시 AUDCLNT_E_ALREADY_INITIALIZED(0x88890002) 또는 E_FAIL 반환
- **해결**: 각 폴백 시도마다 ComRelease 후 새 IAudioClient를 ActivateAudioClientById()로 재획득. 4단계 폴백: (1) MixFormat+flags=0, (2) MixFormat+AUTOCONVERT, (3) PCM 16bit/48kHz/1ch+flags=0, (4) PCM 16bit+AUTOCONVERT
- **교훈**: WASAPI COM 인터페이스에서 Initialize 재시도가 필요하면 반드시 기존 인스턴스를 Release하고 새 인스턴스를 Activate해야 함. 이는 IAudioClient뿐 아니라 일반적인 COM 패턴
- **심각도**: 높음 (캡처 완전 실패 → 마이크 기능 사용 불가)
- **Level**: 2 (인지)

## L-251: NAudio MMDevice.AudioClient 싱글톤 캐시 — WasapiCapture와 동일 디바이스 인스턴스 공유 시 COM 오염 (2026-03-20)

- **문제**: WasapiCapture 생성 후 같은 MMDevice 인스턴스의 AudioClient.MixFormat 접근 시 COM 인스턴스 오염 → StartRecording()에서 E_INVALIDARG (0x80070057) 발생
- **근본 원인**: NAudio의 MMDevice.AudioClient는 싱글톤 캐시 — 한 번 생성되면 해당 디바이스 인스턴스에서 계속 재사용. WasapiCapture 내부에서도 같은 AudioClient를 사용하므로, 외부에서 MixFormat을 읽으면 WasapiCapture의 Initialize 과정과 충돌
- **해결**: MixFormat 읽기용 MMDeviceEnumerator/MMDevice 인스턴스와 WasapiCapture 전달용 인스턴스를 완전히 분리 (fmtEnum/fmtDevice vs freshEnum/freshDevice)
- **교훈**: NAudio에서 동일 MMDevice 인스턴스를 WasapiCapture에 전달하면서 AudioClient 속성에도 접근하면 안 됨. COM 리소스를 사용하는 라이브러리에서는 "읽기 전용" 접근도 내부 상태를 변경할 수 있음
- **심각도**: 높음 (마이크 모니터링 + 녹음 모두 실패)
- **Level**: 2 (인지)

## L-252: 실시간 STT 샘플레이트 불일치 — 녹음 16kHz 데이터에 44100Hz 전달 시 음질 파괴 (2026-03-21)

- **문제**: 실시간 STT 청크 처리 시 녹음 포맷(16000Hz)과 다른 샘플레이트(44100Hz)를 ProcessRealtimeChunkAsync에 전달 → WAV 헤더에 잘못된 샘플레이트 기록 → Whisper가 2.76배 느린 속도로 해석하여 인식률 극저하
- **근본 원인**: AudioRecordingService는 16000Hz로 캡처하지만, OnRealtimeChunkReady 이벤트 핸들러에서 하드코딩된 44100을 전달. 녹음 파일 STT는 파일에서 샘플레이트를 읽으므로 정상 동작했지만, 실시간 청크는 호출자가 직접 지정하는 구조
- **해결**: sampleRate 파라미터를 16000으로 수정 + 기본값도 44100→16000으로 변경
- **교훈**: 오디오 파이프라인에서 샘플레이트는 소스(캡처 장치)에서 싱크(STT 엔진)까지 일관되게 전달해야 함. 하드코딩된 매직넘버 대신 녹음 서비스의 실제 설정값을 참조할 것
- **심각도**: 높음 (실시간 STT 완전 무용화)
- **Level**: 2 (인지)

## L-253: 실시간 STT 청크 경계 단어 잘림 — 오버랩 버퍼로 해결 (2026-03-21)

- **문제**: 15초 청크 단위 STT 처리 시 청크 경계에서 단어가 잘려 인식 실패 (예: "안녕하세" | "요" → 두 청크 모두 부정확)
- **해결**: 청크 크기를 30초로 확대 + 이전 청크 끝 5초를 다음 청크 앞에 오버랩으로 붙여서 처리 (16000Hz×2bytes×5초=160KB 버퍼)
- **교훈**: 스트리밍 오디오 STT에서 고정 길이 청크 분할은 필연적으로 경계 문제 발생. 오버랩 윈도우(전체 청크의 10~20%)를 적용하면 경계 단어 인식률이 크게 향상됨
- **심각도**: 중간 (인식률 저하, 특정 단어 누락)
- **Level**: 1 (참고)
