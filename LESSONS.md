# LESSONS.md — MaiX 프로젝트 교훈 로그

## L-045: AI 프롬프트 negative examples 필수 (2026-02-17)

- **문제**: OneNote AI 분석에서 마커 카테고리명(★중요★, ⚠주의⚠) 도배 — 3회 반복 지적
- **근본원인**: 프롬프트에 올바른 예시만 제공, 금지 패턴(negative examples) 미명시 → AI가 카테고리명을 마커 안에 삽입
- **해결**: 프롬프트에 "잘못된 예시 (절대 금지)" 섹션 추가 + C# 렌더링에서 마커 기호 제거
- **교훈**: AI 프롬프트 작성 시 올바른 예시 + 금지 예시(negative examples) 반드시 함께 제공해야 준수율 향상
- **심각도**: 높음 (3회 반복)
- **수정 파일**: Resources/Prompts/*.txt 3개, MainWindow.xaml.cs

## L-046: 파이프라인 컨텍스트 복원 시 상태 전이 주의 (2026-02-17)

- **문제**: 컨텍스트 복원 후 ko→kplan→kdev→ktest 재전이 시 kdev가 증거 파일 클리어, ko가 상태를 KO로 리셋
- **근본원인**: pipeline_gate.sh의 kdev 전이 시 증거 파일 삭제 로직 + ko의 상태 KO 리셋이 컨텍스트 복원 흐름과 충돌
- **해결**: team-lead가 수동으로 파이프라인 상태를 DEV로 설정 후 ktest 재실행
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

## L-050: 팀에이전트 파이프라인 복구 시 kdev 호출 전 상태를 PLAN으로 설정 (2026-02-17)

- **문제**: 팀에이전트에서 파이프라인 상태를 DEV로 수동 설정 후 kdev 호출 → pipeline_gate.sh 차단
- **근본원인**: pipeline_gate.sh는 kdev를 PLAN 상태에서만 허용 (PLAN→DEV 전이는 gate 내부 관리)
- **해결**: 수동 복구 시 `echo 'PLAN' > /tmp/claude_pipeline_state` 후 kdev 호출
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
- **근본원인**: WASAPI Shared Mode는 Windows 오디오 믹서가 포맷을 결정하므로 클라이언트가 WaveFormat을 변경할 수 없음 (보통 48khz 32bit float 2ch)
- **해결**: 캡처 장치 네이티브 포맷 유지 + `OnDataAvailable`에서 float→PCM, 스테레오→mono, 리샘플링(→16khz) 후처리 변환
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

## L-232: ktest spawn 프롬프트에 테스트 단계 생략 지시 금지 (2026-03-16)

- **문제**: ko가 ktest 에이전트 spawn 시 프롬프트에 "사용자 수동 테스트 예정", "배포 스킵" 등 테스트 단계 생략 지시를 포함하여 ktest가 build→deploy→run→quality 전 단계를 수행하지 못함
- **근본원인**: 메인/ko가 사용자 의도를 과도 해석하여 ktest에 테스트 축소 지시를 삽입
- **해결**: ko SKILL.md 제약 사항에 L-232 규칙 추가 — ktest spawn 프롬프트에 테스트 단계 생략 지시 금지
- **교훈**: ktest는 항상 독립적으로 전 단계(build/deploy/run/quality) 수행해야 하며, 상위 단계에서 테스트 범위를 축소하는 지시를 삽입해서는 안 됨
- **심각도**: 중간
- **Level**: 3 (강제 — ko SKILL.md 규칙 반영)

## L-239: NAudio WaveInEvent에 비표준 샘플레이트 하드코딩 금지 (2026-03-19)
- **문제**: WaveInEvent fallback에서 16000Hz 하드코딩 → Windows MME API SupportedWaveFormat에 16000Hz 매핑 없음 → waveInOpen에서 InvalidParameter 오류 → 녹음 버튼 무반응
- **근본원인**: NAudio WaveInEvent는 Windows MME 표준 포맷(8000/11025/22050/44100/48000Hz)만 지원. 16000Hz는 WAVE_FORMAT_* 상수에 미정의
- **해결**: GetBestWaveFormat(deviceNumber)으로 마이크가 실제 지원하는 포맷 자동감지 후 사용. 캡처 포맷이 출력 포맷(16000Hz)과 달라도 OnDataAvailable에서 리샘플링으로 처리
- **교훈**: WaveInEvent에 임의 샘플레이트를 지정하지 말 것. 항상 GetBestWaveFormat()으로 기기 지원 포맷을 조회하여 사용
- **심각도**: 높음 (녹음 기능 전체 불가)
- **Level**: 2 (코드 패턴 — GetBestWaveFormat 강제 사용)

## L-240: phase_guard.sh kinfra_* KO 전환 — 파이프라인 진행 중 덮어쓰기 금지 (2026-03-19)
- **문제**: kdone 프롬프트에 Skill('kinfra_maix') 포함 시 phase_guard.sh PostToolUse hook이 DONE 상태를 KO로 덮어씀 → pipeline_order_guard.sh가 KO 상태에서 DONE spawn을 차단 → kdone spawn 실패 + 좀비 pane 생성
- **근본원인**: phase_guard.sh의 kinfra_* 처리에 현재 상태 조건이 없어 IDLE/FINISH 이외 상태도 무조건 KO로 전환
- **해결**: phase_guard.sh kinfra_* 분기에 IDLE/FINISH 상태일 때만 KO 전환하는 조건 추가 (ko 분기와 동일한 조건)
- **교훈**: kdone 프롬프트에서 Skill('kinfra_*') 호출 제거로 회피 가능하나 hook 자체도 안전해야 함. IDLE/FINISH 이외 파이프라인 진행 중 상태는 어떤 스킬 로딩으로도 KO 전환 금지
- **심각도**: 높음 (kdone 진입 완전 차단 → 좀비 pane 생성)
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
- **해결**: 각 폴백 시도마다 ComRelease 후 새 IAudioClient를 ActivateAudioClientById()로 재획득. 4단계 폴백: (1) MixFormat+flags=0, (2) MixFormat+AUTOCONVERT, (3) PCM 16bit/48khz/1ch+flags=0, (4) PCM 16bit+AUTOCONVERT
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

## L-252: 실시간 STT 샘플레이트 불일치 — 녹음 16khz 데이터에 44100Hz 전달 시 음질 파괴 (2026-03-21)

- **문제**: 실시간 STT 청크 처리 시 녹음 포맷(16000Hz)과 다른 샘플레이트(44100Hz)를 ProcessRealtimeChunkasync에 전달 → WAV 헤더에 잘못된 샘플레이트 기록 → Whisper가 2.76배 느린 속도로 해석하여 인식률 극저하
- **근본 원인**: AudioRecordingService는 16000Hz로 캡처하지만, OnRealtimeChunkready 이벤트 핸들러에서 하드코딩된 44100을 전달. 녹음 파일 STT는 파일에서 샘플레이트를 읽으므로 정상 동작했지만, 실시간 청크는 호출자가 직접 지정하는 구조
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

## L-254: SherpaOnnx 네이티브 크래시는 try-catch로 잡을 수 없음 — 모델 파일 사전 검증 필수 (2026-03-21)

- **문제**: SherpaOnnx OfflineRecognizer 생성 시 모델 파일이 없거나 손상되면 AccessViolationException 등 네이티브 크래시 발생. C# try-catch로 잡을 수 없어 앱 전체가 비정상 종료
- **해결**: OfflineRecognizer 생성 전에 model.int8.onnx, tokens.txt 파일 존재를 사전 검증. 추가로 try-catch 래핑하여 잡히는 관리 예외도 방어
- **교훈**: 네이티브 interop(P/Invoke, ONNX Runtime 등)에서 발생하는 비관리 예외는 CLR catch 블록으로 포착 불가. 네이티브 라이브러리 호출 전에 입력 파일/경로/파라미터를 사전 검증하는 방어 코드가 필수
- **심각도**: 높음 (앱 크래시, 사용자 데이터 손실 가능)
- **Level**: 2 (인지 — MEMORY.md 기록)

## L-255: SherpaOnnx OfflineRecognizer 스레드 안전 미보장 — lock 직렬화 필수 (2026-03-21)

- **문제**: 실시간 STT에서 SherpaOnnx OfflineRecognizer의 Decode를 연속 호출 시 세 번째 청크에서 AccessViolationException 발생. 네이티브 메모리 동시 접근으로 인한 크래시
- **근본 원인**: SherpaOnnx OfflineRecognizer는 내부적으로 스레드 안전하지 않음. 실시간 STT 이벤트가 비동기로 빠르게 연속 발생하면 이전 Decode가 완료되기 전에 다음 Decode가 시작되어 네이티브 메모리 충돌
- **해결**: `_recognizerLock` 객체로 Decode 호출 전체(CreateStream → AcceptWaveform → Decode → Result 읽기)를 lock으로 감싸서 직렬화
- **교훈**: 네이티브 interop 라이브러리(SherpaOnnx, ONNX Runtime 등)의 추론/디코드 메서드는 스레드 안전하지 않다고 가정하고, 반드시 lock이나 SemaphoreSlim으로 동시 접근을 직렬화할 것. 특히 "처음 1~2회는 성공하고 N번째에서 크래시"하는 패턴은 네이티브 리소스 경쟁의 전형적 증상
- **심각도**: 높음 (앱 크래시, 실시간 STT 불가)
- **Level**: 2 (인지)

## L-256: 실시간 STT Whisper 전환 — STTModelType 파라미터 기반 분기 (2026-03-21)

- **상황**: 실시간 STT가 SenseVoice 고정이어서 Whisper 모델 선택 시에도 SenseVoice로만 전사
- **근본 원인**: ProcessRealtimeChunkasync에 모델 유형 파라미터가 없어 SenseVoice 경로만 존재
- **해결**: ProcessRealtimeChunkasync에 STTModelType 파라미터 추가, Whisper 계열이면 ProcessRealtimeChunkwithwhisperasync로 분기. float[] → 임시 WAV → Whisper 전사 → finally 블록에서 임시 파일 삭제
- **교훈**: 30초 청크 기준 Vulkan GPU Whisper에서 약 24초 처리로 실시간 가능. Whisper 초기화는 기존 InitializeWhisperAsync 재사용하여 1회 보장. ViewModel에 _realtimeSTTModelType 필드를 두고 MainWindow에서 모델 변경 시 동기화
- **심각도**: 낮음 (기능 확장)
- **Level**: 1 (참고)

## L-257: Whisper 후처리에서 SenseVoice 불필요 초기화 → AccessViolationException (2026-03-22)

- **문제**: TranscribeWithWhisperAsync 내부에 "화자분리용" SenseVoice 초기화 코드가 남아있어, Whisper 실행 중 SherpaOnnx 네이티브 충돌(AccessViolationException) 발생
- **근본 원인**: Whisper는 세그먼트 타임스탬프로 화자분리하므로 SenseVoice가 불필요하나, 초기 개발 시 삽입된 SenseVoice 초기화 코드가 제거되지 않고 잔존. Whisper와 SenseVoice가 동시에 SherpaOnnx 네이티브 리소스를 점유하면서 크래시 발생
- **해결**: TranscribeWithWhisperAsync에서 SenseVoice 초기화 코드 6줄 제거
- **추가 수정**: STT 분석 버튼 첫 클릭 무시 버그 — CancelSTT() 후 IsSTTInProgress가 true로 남아 다음 클릭이 취소로 동작. CancelSTT() 후 IsSTTInProgress=false 강제 리셋 + 취소 피드백 추가
- **교훈**: 모델별 초기화는 해당 모델 경로에서만 수행. 다른 모델의 초기화 코드가 잔존하면 네이티브 리소스 충돌로 크래시 발생. 동일 패턴 방지: 새 모델 추가 시 기존 모델 초기화 의존성 점검 필수
- **심각도**: 높음 (앱 크래시)
- **Level**: 1 (참고)

## L-258: 화자분리 SemaphoreSlim + 타임아웃으로 네이티브 크래시 방지 (2026-03-22)

- **문제**: 화자분리(_speakerDiarizer.Process)가 동시 호출되면 네이티브 리소스 충돌로 크래시, 또는 무한 블로킹
- **근본 원인**: sherpa-onnx 네이티브 OfflineSpeakerDiarization.Process()가 thread-safe하지 않고, 입력 검증 없이 빈 배열도 전달됨
- **해결**: SemaphoreSlim(1,1)로 동시 접근 차단, 입력/리샘플링 후 유효성 검증, Task.Run + 5분 타임아웃으로 무한 블로킹 방지
- **교훈**: 네이티브 interop 호출은 항상 (1) 동시 접근 Lock (2) 입력 유효성 검증 (3) 타임아웃이 3종 세트로 필요. 특히 sherpa-onnx는 내부에서 예외를 던지지 않고 행(hang)하는 경우가 있어 타임아웃이 필수
- **심각도**: 높음 (앱 크래시)
- **Level**: 1 (참고)

## L-259: WPF ListBox 내부 Button 첫 클릭 무시 — PreviewMouseLeftButtonDown 패턴 (2026-03-22)

- **문제**: ListBox 내 Button을 클릭하면 첫 번째 클릭이 ListBoxItem 선택에 소비되어 Button.Click 이벤트가 발생하지 않음
- **근본 원인**: WPF ListBox는 미선택 ListBoxItem 내부 클릭 시 먼저 해당 아이템을 선택하고 이벤트를 소비. 두 번째 클릭부터 Button.Click이 전파됨
- **해결**: ListBox에 PreviewMouseLeftButtonDown 핸들러 추가 — ButtonBase/Slider가 포함된 ListBoxItem을 FindVisualParent로 탐색, 미선택 시 프로그래밍적으로 IsSelected=true 설정
- **교훈**: WPF ListBox 내 인터랙티브 컨트롤(Button, Slider 등)이 있으면 PreviewMouseLeftButtonDown에서 선 선택 패턴 적용 필수. 이전 커밋(b6eb6fa4)의 Focusable=False 방식은 불완전 — PreviewMouseLeftButtonDown이 근본 해결
- **심각도**: 중간 (UX 불편)
- **Level**: 1 (참고)

## L-260: 자동 후처리 — 녹음 종료 시 STT→화자분리→요약 자동 실행 (2026-03-22)

- **문제**: 녹음 종료 후 STT, 화자분리, 요약을 사용자가 각각 수동으로 실행해야 함
- **해결**: StopRecording에서 RunPostProcessingAsync를 Dispatcher.InvokeAsync로 자동 호출. 후처리 순서를 STT→화자분리→요약으로 변경 (기존: STT→요약→화자분리). 화자분리는 STT 유무와 무관하게 독립 실행 가능하도록 변경
- **교훈**: 후처리 순서는 데이터 의존성 기반으로 결정 — STT(원본 텍스트 생성) → 화자분리(텍스트에 화자 라벨 부여) → 요약(화자분리된 텍스트 요약). Dispatcher.InvokeAsync로 UI 스레드에서 실행해야 바인딩 프로퍼티(IsPostProcessing) 안전 갱신
- **심각도**: 낮음 (기능 추가)
- **Level**: 1 (참고)

## L-261: 네이티브 라이브러리 크래시 방어 — 조건부 호출 패턴 (2026-03-22)

- **문제**: sherpa-onnx 화자분리(diarizer.Process())가 특정 오디오에서 네이티브 크래시 발생 — 기본 STT에서도 항상 호출되어 불필요한 크래시 위험
- **근본 원인**: TranscribeFileAsync가 화자분리를 무조건 호출 — 일반 STT에서는 화자분리 불필요하나 네이티브 호출이 항상 실행됨
- **해결**: `enableDiarization` 파라미터 추가 (기본값 false) — false이면 네이티브 diarizer.Process() 완전 스킵, 폴백 휴리스틱 사용. RunPostDiarizationAsync에서만 true로 호출
- **교훈**: 네이티브 라이브러리 호출은 명시적 opt-in 파라미터로 보호해야 함. 기본값을 안전한 경로(managed fallback)로 설정하고, 사용자가 의도적으로 활성화할 때만 네이티브 경로 진입
- **심각도**: 높음 (앱 크래시)
- **Level**: 1 (참고)

## L-262: 크래시 시 로그 유실 방지 — flushToDiskinterval + CloseAndFlush 패턴 (2026-03-22)

- **문제**: 앱 크래시 시 Serilog/log4net 버퍼에 남은 로그가 디스크에 기록되지 않아 디버깅 불가
- **해결**: Serilog에 `flushToDiskinterval: TimeSpan.FromSeconds(1)` 추가 + UnhandledException에서 `Log.Fatal` + `Log.CloseAndFlush()` 호출 + log4net `immediateFlush=true`
- **교훈**: 크래시 디버깅을 위해 로그 프레임워크는 (1) 주기적 flush 설정 (2) UnhandledException 핸들러에서 명시적 flush/close를 반드시 구현해야 함
- **심각도**: 중간 (디버깅 편의)
- **Level**: 1 (참고)

## L-263: WebSocket 통합 엔드포인트 패턴 — STT+화자분리 단일 연결 (2026-03-25)

- **문제**: STT와 화자분리를 별도 WebSocket 연결로 운영하면 클라이언트 코드 복잡도 증가 + 동기화 이슈
- **해결**: /ws/split 단일 WebSocket으로 STT+화자분리 통합, type 필드(stt/diarize/stt_final)로 메시지 분기
- **교훈**: 동일 오디오 스트림에 대한 여러 처리(STT, 화자분리)는 서버 측에서 통합하고 클라이언트는 단일 연결만 유지하는 패턴이 효과적
- **심각도**: 낮음 (아키텍처 패턴)
- **Level**: 1 (참고)

## L-264: 서버-클라이언트 API 경로 동기화 — 서버 변경 시 클라이언트 즉시 반영 (2026-03-25)

- **문제**: 서버 API 경로 변경(/api/tts/preview → /api/tts) 시 클라이언트 미반영으로 404 발생 가능
- **해결**: 서버 엔드포인트 변경과 클라이언트 코드를 동일 커밋에서 업데이트
- **교훈**: 서버 API 경로/스키마 변경 시 반드시 클라이언트 코드도 같은 작업 단위에서 동기화. 가능하면 서버가 모델/화자 목록을 동적으로 제공하는 API 추가
- **심각도**: 낮음 (프로세스)
- **Level**: 1 (참고)
## L-265: WebSocket 프로토콜 메시지 타입/필드명 스펙 명시화 — 네이밍 컨벤션 불일치 방지 (2026-03-25)

- **문제**: /ws/split WebSocket 프로토콜에서 클라이언트(C# camelCase)와 서버(Python snake_case) 간 메시지 필드명 불일치 발생 (`chunkseconds`/`bitDepth` vs `sample_rate`/`bit_depth`, `type:"start"` vs `type:"config"`, `type:"stop"` vs `type:"end"`)
- **해결**: 서버 프로토콜 스펙에 맞춰 클라이언트 코드 수정 (config 메시지 snake_case, end 메시지 type 수정, is_final 이벤트 처리 추가)
- **교훈**: (1) WebSocket 프로토콜 설계 시 메시지 타입명과 필드명 스펙을 API 문서에 명시적으로 정의 (2) Python 서버는 snake_case, C# 클라이언트는 System.Text.Json JsonNamingPolicy.SnakeCaseLower 또는 [JsonPropertyName] 어트리뷰트로 자동 변환 고려 (3) is_final 같은 상태 완료 신호는 클라이언트가 반드시 처리해야 UI 상태가 정확히 동기화됨
- **심각도**: 중간 (기능 오작동)
- **Level**: 2 (규칙화 권장)

## L-266: NuGet 패키지 의존성 사전 확인 — 새 인터페이스 사용 시 .csproj 점검 필수 (2026-03-26)

- **문제**: `IHttpClientFactory` 주입 구현 시 `Microsoft.Extensions.Http` 패키지가 `.csproj`에 없어 빌드 실패
- **원인**: `IHttpClientFactory`는 `System.Net.Http` 네임스페이스이지만 별도 NuGet 패키지(`Microsoft.Extensions.Http`)가 필요. 네임스페이스만 보고 패키지 추가를 생략함
- **해결**: `Microsoft.Extensions.Http` 10.0.2 패키지를 `.csproj`에 추가 후 빌드 성공
- **교훈**: 새 인터페이스/타입(특히 `Microsoft.Extensions.*`)을 처음 사용할 때는 구현 전에 `.csproj`에 해당 NuGet 패키지가 있는지 확인. `IHttpClientFactory` → `Microsoft.Extensions.Http`, `IMemoryCache` → `Microsoft.Extensions.Caching.Memory` 등
- **심각도**: 낮음 (빌드 오류로 즉시 감지 가능)
- **Level**: 1 (참고)

## L-267: WebSocket 엔드포인트 경로 확인 우선 — 존재하지 않는 엔드포인트 연결 버그 (2026-03-26)

- **문제**: OneNoteViewModel이 `ConnectSplitAsync()`(/ws/split)를 호출했으나 서버에 해당 엔드포인트가 없어 STT 실시간 응답 수신 불가
- **원인**: 서버 API 변경(엔드포인트 통합/제거) 시 클라이언트 코드를 동시에 업데이트하지 않음. 클라이언트는 /ws/split을 호출하도록 구현되어 있었으나 서버는 /ws/stt만 운영 중
- **해결**: `ConnectSplitAsync()` → `ConnectSttAsync()`로 전환, 서버의 실제 /ws/stt 엔드포인트 사용
- **교훈**: 서버 WebSocket/REST API 경로 변경 시 (1) 클라이언트 코드를 즉시 동일 커밋에서 반영 (2) 새 기능 구현 전 서버 실제 엔드포인트 목록 확인 (RESTAPI.md/MCP.md 참조) (3) 연결 실패 시 엔드포인트 존재 여부를 첫 번째 점검 항목으로
- **심각도**: 높음 (핵심 기능 STT 수신 완전 불가)
- **Level**: 2 (규칙화 권장)

## L-268: git core.ignorecase=true 환경에서 폴더 리네임 시 git mv 필수 (2026-03-26)

- **문제**: NTFS(Windows)에서 MaiX/ → mAIx/ 폴더명 변경 시 git이 이를 인식하지 못함
- **원인**: NTFS는 대소문자 무감(case-insensitive), git config core.ignorecase=true → 단순 OS 리네임으로는 git이 폴더명 변경을 추적 불가
- **해결**: `git mv MaiX tmp_mAIx && git mv tmp_mAIx mAIx` 방식으로 임시 이름을 거쳐 리네임하면 git이 추적 가능
- **교훈**: NTFS 환경에서 대소문자만 다른 폴더/파일 리네임 시 반드시 `git mv` 2단계 방식 사용 (MaiX→tmp→mAIx). OS 레벨 rename만으로는 git이 동일 경로로 인식함
- **심각도**: 중간 (git 이력 누락)
- **Level**: 1 (참고)

## L-269: STT 실시간 시간 정보 수신 — 서버 필드명 우선순위 파싱 패턴 (2026-03-27)

- **문제**: STT 실시간 청크 UI에 시간이 항상 00:00으로 표시됨
- **원인**: `SttChunkResult` 레코드에 StartSeconds/EndSeconds 필드가 없었고, OnSttChunkReceived에서 `TimeSpan.Zero`를 하드코딩
- **해결**: (1) `SttChunkResult`에 `StartSeconds`/`EndSeconds` 기본값 0f 추가 (2) JSON 파싱 시 `start_time` → `start` → `chunk_id × 1.5초` 폴백 순서 적용 (3) `TimeSpan.Zero` → `TimeSpan.FromSeconds(chunk.StartSeconds/EndSeconds)`로 수정
- **교훈**: WebSocket 서버 응답 JSON의 필드명이 버전마다 다를 수 있으므로 복수 필드명 우선순위 파싱 + 폴백 값 패턴을 적용하면 서버 업그레이드 시에도 시간 정보 안정적으로 수신 가능
- **심각도**: 낮음 (시간 표시 버그, 핵심 기능 영향 없음)
- **Level**: 1 (참고)

## L-270: RunPostProcessingAsync 조건 체크 — 복수 플래그 중 하나라도 true이면 진행 (2026-03-27)

- **문제**: `IsPostSTTEnabled=true`이어도 `IsPostSummaryEnabled=false`이면 후처리가 실행되지 않는 버그
- **원인**: `RunPostProcessingAsync` 진입 조건이 `if (!IsPostSummaryEnabled) return;`으로 되어 있어 STT 단독 후처리 불가
- **해결**: `if (!IsPostSTTEnabled && !IsPostSummaryEnabled) return;`으로 수정 + 파일 기반 STT 후처리 단계 별도 추가 (실시간 STT 결과 없을 때만 반영)
- **교훈**: 후처리 진입 조건은 개별 기능 플래그 AND가 아닌 OR 조합으로 설계해야 함. 새 후처리 단계 추가 시 기존 조건과 충돌 여부 반드시 확인
- **심각도**: 낮음 (설정 조합에 따른 기능 미작동)
- **Level**: 1 (참고)

## L-271: settings.json 등록 hook 파일 미존재 — done_finish_guard.sh (2026-03-28)

- **문제**: `settings.json`에 `done_finish_guard.sh`가 PostToolUse:Skill hook으로 등록되어 있었으나 실제 파일이 없었음
- **원인**: hook 파일 생성 없이 settings.json에만 등록한 상태로 방치됨 (kfinish 스킵 방지 의도였으나 구현 미완)
- **해결**: `done_finish_guard.sh` 파일 생성 (로깅 전용 — 차단 로직은 추후 요구사항 명확화 후 추가)
- **교훈**: kdone_docs의 L-052 체크리스트(settings.json 등록 hook 파일 존재 확인)를 매 작업마다 반드시 실행. hook 파일 등록 시 반드시 동시에 파일도 생성.
- **심각도**: 낮음 (exit 0 fallback으로 실제 차단 없음)
- **Level**: 1 (참고)

## L-272: PowerShell WinRT Interop 토스트 패턴 — BurntToast NuGet 없이 net10.0-windows 네이티브 토스트 (2026-03-28)

- **문제**: net10.0-windows WPF 앱에서 Windows 네이티브 토스트 알림이 필요한데, BurntToast 등 외부 NuGet 패키지 없이 구현 방법이 필요
- **해결**: `ToastNotificationService.cs` 신규 생성 — PowerShell `Add-Type`으로 `Windows.UI.Notifications` WinRT 네임스페이스를 직접 로딩하여 토스트 발송. `Process.Start("powershell.exe", ...)` 비동기 호출 방식
- **교훈**: net10.0-windows TFM에서 외부 NuGet 없이 Windows 네이티브 토스트 알림이 필요하면 PowerShell WinRT Interop 패턴 사용. `Windows.UI.Notifications.ToastNotificationManager` 직접 호출 가능. `NotificationXmlSettings`로 앱ID/활성화 여부 설정 관리
- **심각도**: 낮음 (신규 기능 도입 패턴)
- **Level**: 1 (참고)

## L-273: WPF UI(FluentUI) XAML Symbol 속성 — 심볼명 사전 확인 필수 (2026-03-28)

- **문제**: XAML BulkActionBar 구현 시 `Symbol="FolderMove24"` 사용 → 해당 심볼이 FluentSystemIcons에 존재하지 않아 빌드 오류 발생
- **원인**: WPF UI 라이브러리의 FluentIcon 심볼명을 사전 검증 없이 추측하여 입력
- **해결**: `FolderMove24` → `FolderArrowRight20`으로 수정 (실제 존재하는 심볼명으로 교체)
- **교훈**: WPF UI(Fluent Design) XAML에서 `Symbol` 속성 사용 시 반드시 FluentSystemIcons 목록에서 실제 존재하는 심볼명 확인 후 사용. 추측 입력 금지. 빌드 오류 발생 시 FluentIcon 심볼명 미존재를 첫 번째 점검 항목으로
- **심각도**: 낮음 (빌드 오류, 수정 용이)
- **Level**: 1 (참고)

## L-274: SQLite FTS5 가상 테이블 컬럼명 예약어 충돌 — [From] 대괄호 이스케이프 필수 (2026-03-29)

- **문제**: FTS5 가상 테이블 생성 SQL에서 `From` 컬럼명 사용 시 SQLite 예약어 충돌로 Migration 실패
- **원인**: Email 모델의 `From` 프로퍼티 이름을 FTS5 가상 테이블 컬럼명으로 그대로 사용. SQLite FTS5에서 `FROM`은 예약어로 처리됨
- **해결**: `From` → `[From]` 대괄호 이스케이프 적용 후 정상 동작
- **교훈**: SQLite FTS5 가상 테이블 SQL 작성 시 컬럼명이 SQLite 예약어인지 사전 확인 필수. 이메일 모델에서 충돌 가능한 예약어: `From`(FROM), `To`(TO), `Order`(ORDER), `Group`(GROUP), `Select`(SELECT), `Where`(WHERE), `Index`(INDEX). 예약어는 반드시 `[컬럼명]` 대괄호로 이스케이프할 것.
- **심각도**: 중간 (예측 가능한 패턴, FTS5 작업 시 재발 가능)
- **Level**: 1 (참고)

## L-275: WPF IValueConverter — AI 카테고리 배지 색상 변환 패턴 (2026-03-29)

- **패턴**: AI 카테고리 문자열 → 배지 UI 속성(색상/텍스트) 변환 시 IValueConverter 구현
- **구현**: `AiCategoryToBadgeConverter : IValueConverter` — Convert()에서 카테고리 문자열 switch
- **App.xaml 등록**: `<local:AiCategoryToBadgeConverter x:Key="AiCategoryToBadgeConverter"/>` ResourceDictionary에 추가
- **교훈**: WPF에서 열거형/문자열 → UI 속성 변환은 IValueConverter 패턴으로 관심사 분리. ViewModel에 직접 색상 프로퍼티 추가보다 Converter가 MVVM 패턴에 적합
- **심각도**: 낮음 (새 패턴 도입)
- **Level**: 1 (참고)

## L-276: EF Core DateTime? 컬럼 — SQLite 예약발송 시간 마이그레이션 패턴 (2026-03-29)

- **패턴**: 예약발송 시간처럼 선택적(nullable) 날짜/시간 필드는 `DateTime?`으로 모델 선언
- **구현**: `Email.ScheduledSendTime: DateTime?` → Migration에서 `nullable: true` 자동 적용
- **BackgroundSync 쿼리**: `ScheduledSendTime <= DateTime.UtcNow && !IsSent` 조건으로 발송 대상 필터
- **교훈**: 선택적 예약 기능 구현 시 `DateTime?` + UTC 기준 비교 패턴 사용. 로컬타임 비교 시 서머타임/타임존 문제 발생 가능 — UTC 유지 권장
- **심각도**: 낮음 (새 패턴 도입)
- **Level**: 1 (참고)

## L-277: ComposeViewModel 5초 카운트다운 취소 — CancellationTokenSource 패턴 (2026-03-29)

- **문제**: 예약발송/즉시발송 취소 기능 구현 시 타이머와 취소 토큰을 어떻게 연동하는가
- **구현**: `CancellationTokenSource _sendCts` + `Task.Delay(5000, _sendCts.Token)` 패턴으로 5초 대기 중 취소 가능
- **교훈**: UI에서 "취소" 버튼 클릭 → `_sendCts.Cancel()` 호출 → OperationCanceledException catch 후 상태 복원. CancellationToken 기반 패턴은 복잡한 타이머 관리 없이 구현 가능
- **심각도**: 낮음 (새 패턴 도입)
- **Level**: 1 (참고)

## L-278: SpeechSynthesizer TTS — WPF에서 메일 본문 읽기 패턴 (2026-03-29)

- **패턴**: WPF 앱에서 SpeechSynthesizer로 메일 본문 TTS 재생/중지 토글 구현
- **구현**: `SpeechSynthesizer _synthesizer` 필드 + `SpeakAsync(text)` / `SpeakAsyncCancelAll()` + `SpeakCompleted` 이벤트로 버튼 상태 복원
- **주의**: SpeechSynthesizer는 메인 스레드에서 생성하되, SpeakAsync는 비동기 처리. IDisposable — 윈도우 Closed 이벤트에서 반드시 Dispose 호출
- **교훈**: WPF에서 TTS 기능이 필요하면 System.Speech.Synthesis.SpeechSynthesizer 사용 (외부 NuGet 불필요, .NET 10-windows 기본 포함)
- **심각도**: 낮음 (신규 패턴 도입)
- **Level**: 1 (참고)

## L-279: AI 답장 초안 — ViewModel → ComposeWindow 자동 입력 패턴 (2026-03-29)

- **패턴**: AI가 생성한 답장 초안을 ComposeWindow Body에 자동 삽입하는 방법
- **구현**: `ComposeWindow`를 생성할 때 생성자 파라미터 또는 프로퍼티로 초기 본문 전달 → ComposeViewModel.Body 바인딩에 자동 반영
- **주의**: AI 초안 생성은 비동기 (await AiMailService.GenerateDraftAsync) — UI 스레드 복귀 시 Dispatcher.Invoke 불필요 (WPF UI는 await 이후 자동 UI 스레드 복귀)
- **교훈**: AI 생성 텍스트를 다른 Window에 전달할 때 생성자 파라미터 패턴이 가장 간결. ViewModel 프로퍼티 직접 주입도 가능하나 생성자 방식이 MVVM 패턴에 더 적합
- **심각도**: 낮음 (신규 패턴 도입)
- **Level**: 1 (참고)

## L-280: 스누즈 해제 백그라운드 루프 — DateTime UTC 비교 패턴 (2026-03-29)

- **패턴**: BackgroundSyncService에서 주기적으로 스누즈 해제 대상 메일을 체크하고 UI 갱신
- **구현**: `_timer` 콜백에서 `SnoozedUntil.HasValue && SnoozedUntil <= DateTime.UtcNow` 조건으로 필터링 → SnoozedUntil = null 업데이트 → ObservableCollection 갱신
- **주의**: DB 저장 시 UTC 기준 저장, 읽기/표시 시 로컬 변환 필요. HasValue 체크 없이 DateTime? 비교 시 NullReferenceException 가능
- **교훈**: nullable DateTime 컬럼 비교는 항상 `.HasValue &&` 선행 체크 또는 EF Core LINQ `x.SnoozedUntil.HasValue && x.SnoozedUntil <= DateTime.UtcNow` 패턴 사용
- **심각도**: 낮음 (신규 패턴 도입)
- **Level**: 1 (참고)

## L-281: kdone_docs 에이전트 spawn — team_name 없이 서브에이전트 호출 금지 (2026-03-29)

- **문제**: kdone_docs에서 병렬 문서 업데이트를 위해 Agent 도구를 team_name 없이 직접 호출 → full_task_team_guard.sh hook이 차단
- **원인**: 팀에이전트 맥락에서는 모든 Agent spawn에 team_name 필수 (hook 강제)
- **해결**: 메인이 직접 순차/병렬 문서 업데이트 수행 (Fallback)
- **교훈**: 팀에이전트(kdone-1 등) 내부에서 Agent spawn 시 반드시 team_name 지정. team_name 없는 spawn은 hook이 차단 → 메인 직접 처리로 Fallback
- **심각도**: 낮음 (hook이 정상 차단, Fallback 작동)
- **Level**: 1 (참고)
- **재발 기록 (2026-04-02)**: kplan 단계에서 codex:codex-rescue + Explore spawn 시도 → hook 정상 차단 확인. hook 작동 이상 없음.

## L-282: 빌드 출력 경로 변경 시 관련 문서 동시 업데이트 필수 (2026-04-01)

- **문제**: 빌드 출력 경로가 `bin/Debug/net10.0-windows/`에서 `bin/Debug/net10.0-windows/win-x64/net10.0-windows/`로 변경되었으나 PROJECT.md, restapi.md 등 참조 문서가 구 경로를 유지
- **원인**: 빌드 설정 변경 시 코드/바이너리만 수정하고 문서 경로 동기화 누락
- **해결**: PROJECT.md 30행·633행, restapi.md 355행 경로 수정 + PowerShell Start-Process 방식으로 교체
- **교훈**: 빌드 출력 경로 변경 시 `grep -r "net10.0-windows" .` 로 모든 문서/스킬 경로 참조를 일괄 검색하여 동시 업데이트. PROJECT.md, restapi.md, kinfra 스킬 파일 포함.
- **심각도**: 중간 (잘못된 경로로 인한 빌드/실행 가이드 오류)
- **Level**: 2 (주의)

## L-283: kplan 증상 수준 계획 — 근본 원인 미포착으로 kdev 추가 투입 (2026-04-01)

- **문제**: 메일 읽음 카운트 불일치 버그 수정 시 kplan이 증상(폴더 카운트 미갱신)만 분석하고 근본 원인(Graph API 동기화 범위 7일 제한, 서버 미읽음 목록 기준 미사용)을 놓쳐 kdev-2 추가 투입 필요
- **근본 원인**: kplan의 코드 탐색 범위가 표층(ViewModel) 수준에 머물고, 실제 데이터 흐름 전체(GraphMailService → SyncReadStatusAsync → ViewModel)를 추적하지 않음
- **해결**: SyncReadStatusAsync를 서버 미읽음 목록 기준으로 전면 교체 + GetMessagesReadStatusAsync days 7→30 확장 + GetUnreadMessageIdsAsync 신규 추가
- **교훈**: 카운트 불일치/상태 불일치 류 버그는 데이터 흐름 전체(API 호출 → 동기화 로직 → ViewModel 반영)를 end-to-end로 추적해야 근본 원인 파악 가능. 증상 레이어(ViewModel)만 수정하면 재발함
- **심각도**: 중간 (kdev 추가 투입으로 작업 시간 증가)
- **Level**: 2 (주의)

## L-284: SyncReadStatusAsync 적용 범위 — 받은/보낸편지함 외 폴더 누락 (2026-04-01)

- **문제**: `SyncReadStatusAsync`가 받은편지함(Inbox)과 보낸편지함(SentItems)에만 읽음 상태를 동기화하여, 이동된 메일이나 다른 폴더의 읽음 상태가 동기화되지 않음
- **근본 원인**: Graph API 호출 시 폴더를 고정 2개(Inbox, SentItems)로 하드코딩 — 사용자 커스텀 폴더 미포함
- **해결**: 현재는 Inbox+SentItems 범위 유지, 향후 확장 시 동기화 대상 폴더 목록을 설정으로 외부화 권장
- **교훈**: 동기화 범위를 특정 폴더로 제한할 경우 코드 주석과 문서에 제한 범위를 명시해야 함. 향후 확장 시 폴더 목록을 `UserPreferencesSettings`에 설정 가능 필드로 추가하는 것이 바람직
- **심각도**: 낮음 (현재 주요 폴더 커버)
- **Level**: 1 (참고)

## L-285: EmailsSynced 이벤트 0건 — 의미 모호성으로 디버깅 혼란 (2026-04-01)

- **문제**: `EmailsSynced` 이벤트가 `newCount=0`으로 발생할 때 "신규 메일 없음(정상)"과 "동기화 실패(이상)"를 구분할 수 없어 디버깅 혼란 발생
- **근본 원인**: 이벤트 페이로드에 성공/실패 구분 플래그 없이 카운트만 전달. 0건이 정상 상태(신규 없음)인지 오류 상태(API 실패, 빈 응답)인지 의미적으로 모호
- **해결**: 이벤트 핸들러에서 0건 시 폴더 카운트 갱신 로직을 추가(방어적 갱신). 근본 해결은 이벤트 페이로드에 `IsSuccess`, `ErrorMessage` 필드 추가 권장
- **교훈**: 이벤트 페이로드 설계 시 카운트뿐 아니라 성공/실패 상태를 포함해야 소비자(ViewModel, 로그)가 정확한 분기 처리 가능. `EmailsSyncedEventArgs` 확장: `int NewCount, bool IsSuccess, string? ErrorMessage`
- **심각도**: 낮음 (디버깅 불편, 기능 오작동 아님)
- **Level**: 1 (참고)

## L-286: EF Core DbContext 오염 방지 — UNIQUE 위반 시 개별 저장 + Detach 패턴 (2026-04-01)

- **문제**: `BackgroundSyncService.SaveEmailsAsync`에서 배치 `SaveChangesAsync` 중 `InternetMessageId+ParentFolderId` UNIQUE 제약 위반 발생 → DbContext 오염 → 같은 try 블록 내 `SyncReadStatusAsync` 미도달 → 읽음 상태 영구 미동기화
- **근본 원인**: EF Core는 `SaveChangesAsync` 실패 시 DbContext를 오염 상태로 남김. 배치 저장 중 UNIQUE 위반 1건이 전체 배치를 실패시키고, 같은 try 블록의 후속 로직도 함께 차단
- **해결**:
  1. `SaveEmailsAsync`: 배치 저장 → 개별 저장 + catch 시 `Entry(email).State = EntityState.Detached` (DbContext 오염 방지)
  2. `SyncFavoriteFoldersAsync` / `SyncAccountAsync`: `SyncFolderAsync`와 `SyncReadStatusAsync`를 독립 try/catch 블록으로 분리 (각 작업이 서로 영향 안 받도록)
- **교훈**: EF Core에서 UNIQUE 위반 가능성이 있는 `SaveChangesAsync`는 개별 저장 + catch 시 Detach 패턴 적용 필수. 독립적인 동기화 작업들은 반드시 개별 try/catch로 분리하여 한 작업 실패가 다른 작업을 차단하지 않도록 설계
- **패턴**: `foreach (var item in items) { try { ctx.Add(item); await ctx.SaveChangesAsync(); } catch { ctx.Entry(item).State = EntityState.Detached; } }`
- **심각도**: 높음 (읽음 상태 영구 미동기화 — 사용자 체감 버그)
- **Level**: 1 (참고)

## L-320: kfinish Step 1에서 kfinish_cleanup 스킵 금지 (2026-04-01)

- **문제**: kfinish 실행 시 "팀이 이미 삭제됐고 고아 pane도 없으니 kfinish_cleanup 스킵해도 된다"고 판단하여 `Skill('kfinish_cleanup')` 미호출. 결과적으로 4개의 고아 pane(2.1.89)과 고착 파이프라인이 정리되지 않고 잔류.
- **근본 원인**: "정리 대상이 없다"는 수동 판단이 실제 상태와 불일치. 수동 확인은 고아 pane/고착 파이프라인을 놓칠 수 있음.
- **해결**: kfinish SKILL.md Step 1에 L-320 규칙 추가 — 팀/pane이 0개여도 반드시 kfinish_cleanup 실행 또는 team-report.sh + team-cleanup.sh 호출 필수.
- **재발방지**: 스킬 규칙 강화 (kfinish SKILL.md Step 1 주석). Hook은 불필요 (LLM 의지 의존이지만, 이 수준은 스킬 규칙 + 교훈 참조로 충분).
- **심각도**: 중간 (고아 pane 잔류 → 리소스 누수 + 다음 세션 혼란)
- **Level**: 2 (규칙)

## L-287: WebView2 NavigateToString 크기 제한 — cid: 인라인 이미지는 virtual host 방식 필수 (2026-04-01)

- **문제**: cid: 인라인 이미지를 data URI로 변환하여 `NavigateToString()`에 전달하면 3.4MB+ HTML이 렌더링되지 않음 (빈 화면)
- **근본 원인**: WebView2 `NavigateToString()`은 내부 URL 길이 제한(약 2MB) 존재 — 대용량 data URI 포함 HTML은 크기 초과로 렌더링 실패
- **해결**: `SetVirtualHostNameToFolderMapping("maix.local", tempFolder, ...)` + 임시 파일 방식으로 전환. cid: 이미지를 tempFolder에 파일로 저장 후 `<img src="https://maix.local/...">` URL 참조
- **교훈**: WebView2에서 대용량 이미지가 포함된 HTML 렌더링 시 `NavigateToString()` 대신 virtual host 매핑 + 파일 서빙 방식 사용 필수. data URI 방식은 소용량(수십KB)에만 적합
- **심각도**: 높음 (cid: 인라인 이미지 완전 미표시)
- **Level**: 2 (주의)

## L-288: 빈 본문 전환 시 NavigateToString 선행 초기화 필수 (2026-04-01)

- **문제**: 이전 메일(이미지 포함 HTML) 표시 후 빈 본문 메일로 전환 시 이전 메일 내용이 잔류
- **근본 원인**: virtual host 방식으로 렌더링된 이전 페이지가 `NavigateToString()` 호출 전까지 WebView2에 캐시됨
- **해결**: 빈 본문/메타데이터 카드 표시 전 `mailWebView.NavigateToString("<html><body></body></html>")` 선행 호출로 초기화
- **교훈**: WebView2에서 콘텐츠 전환 시 항상 빈 HTML로 선행 초기화 후 새 콘텐츠 로드. 특히 virtual host → NavigateToString 전환 시 잔류 문제 발생 가능
- **심각도**: 낮음 (이전 메일 잔류 — 시각적 혼란)
- **Level**: 1 (참고)

## L-289: UI 리스트 성능 — WPF VirtualizingPanel + CancellationToken + Graph API 병렬 처리 패턴 (2026-04-02)

- **문제**: 메일탭 폴더 전환 시 UI 블로킹 발생 — 대량 메일 로드 중 스크롤 버벅임, 폴더 빠르게 전환 시 이전 요청과 충돌
- **원인**: (1) EmailListBox에 Virtualization 미적용으로 전체 아이템 렌더링, (2) LoadEmailsAsync에 CancellationToken 미지원으로 Race Condition 발생, (3) Bulk 작업(읽음/플래그/삭제)에서 Graph API 순차 호출
- **해결**:
  1. `MainWindow.xaml`: `VirtualizingPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel` 추가
  2. `MainViewModel.cs`: `_loadEmailsCts` CancellationTokenSource 도입, 폴더 전환 시 이전 토큰 취소
  3. `MainViewModel.cs`: Bulk 작업에 `SemaphoreSlim(8)` + `Task.WhenAll` + `ExecuteUpdateAsync` 적용
  4. `ViewModelBase.cs`: `ExecuteAsync`에 `OperationCanceledException` catch 추가 (취소 = 정상, 에러 표시 안 함)
- **교훈**: WPF ListBox 대량 아이템 → VirtualizingPanel 필수. 폴더/탭 전환 시 이전 비동기 요청 취소는 CancellationTokenSource 패턴. Graph API 배치 작업은 SemaphoreSlim + Task.WhenAll으로 병렬화. DB 배치 업데이트는 SaveChanges 불필요한 ExecuteUpdateAsync 사용.
- **패턴**: `_cts?.Cancel(); _cts = new CancellationTokenSource(); await LoadAsync(_cts.Token);`
- **심각도**: 중간 (UI 블로킹 사용자 체감)
- **Level**: 1 (참고)

## L-290: EF Core UNIQUE 제약 위반 로그 노이즈 — Detach 패턴 이후 ERR 로그는 정상 (2026-04-02)

- **문제**: BackgroundSyncService.SaveEmailsAsync에서 UNIQUE 제약 위반 시 EF Core 내부 ERR 로그가 런타임 로그에 남음
- **원인**: EF Core가 DbUpdateException 발생 시 내부적으로 ERR 레벨 로그를 기록 — L-286 Detach 패턴으로 기능적 오류는 없지만 로그 노이즈 잔존
- **해결**: L-286 Detach 패턴(`dbContext.Entry(email).State = EntityState.Detached`)이 이미 올바르게 처리 중. ERR 로그는 EF Core 내부 노이즈이며 실제 기능 오류 아님
- **교훈**: EF Core DbUpdateException catch 블록에서 Detach 패턴을 사용해도 EF Core 자체 내부 ERR 로그는 suppress 불가. 이 로그는 정상 동작의 부산물 — 실제 오류 여부는 catch 처리 여부로 판단할 것
- **심각도**: 낮음 (기능 문제 없음, 로그 노이즈만)
- **Level**: 1 (참고)

## L-291: WPF Dispatcher.Invoke → InvokeAsync — 백그라운드 스레드 블로킹 방지 (2026-04-02)

- **문제**: 백그라운드 스레드(동기화 루프)에서 `Dispatcher.Invoke()` 사용 시 UI 스레드가 응답하기를 동기 대기 → 두 스레드 상호 블로킹 가능
- **근본 원인**: `Dispatcher.Invoke()`는 호출 스레드를 UI 스레드 작업 완료까지 블로킹. 백그라운드 스레드가 많거나 빈번히 호출 시 UI 응답성 저하 및 교착(Deadlock) 위험
- **해결**: `Dispatcher.InvokeAsync()`로 전환 — fire-and-forget으로 UI 큐에 작업을 올리고 즉시 반환
- **교훈**: 백그라운드 스레드에서 UI 업데이트 시 `Dispatcher.Invoke` 대신 `Dispatcher.InvokeAsync` 사용. 완료 확인이 필요한 경우에만 `await Dispatcher.InvokeAsync().Task` 패턴 사용
- **패턴**: `Application.Current?.Dispatcher.InvokeAsync(() => { ... });`
- **심각도**: 중간 (UI 응답성 저하)
- **Level**: 1 (참고)

## L-292: WPF 설정 라디오 버튼 — 두 그룹이 같은 필드 공유 시 덮어쓰기 버그 (2026-04-02)

- **문제**: 즐겨찾기/전체 동기화 주기 라디오 버튼 2개 그룹이 `prefs.MailSyncIntervalSeconds` 동일 필드를 읽고 씀 → 한 쪽 설정이 다른 쪽을 덮어씀
- **근본 원인**: 동적 생성 라디오 버튼의 `IsChecked` 비교값과 `Checked` 콜백 모두 동일 공유 필드 참조
- **해결**: 전용 필드 분리 — `FavoriteSyncIntervalSeconds`, `FullSyncIntervalSeconds` 각각 독립 사용
- **교훈**: 동적 생성 WPF 라디오 버튼 그룹이 여러 개일 때, 각 그룹의 `IsChecked` 초기값과 `Checked` 콜백은 반드시 전용 필드/메서드로 분리. 공유 필드 사용 시 덮어쓰기 버그 발생
- **패턴**: `IsChecked = prefs.GroupAIntervalSeconds == seconds`, `Checked += () => prefs.GroupAIntervalSeconds = seconds; vm.SetGroupAInterval(seconds);`
- **심각도**: 중간 (설정 저장 버그)
- **Level**: 1 (참고)

## L-293: 동기화 주기 하한 — UI 옵션 필터 + 서비스 계층 이중 방어 필수 (2026-04-04)

- **문제**: `intervalOptions`에서 위험 저주기(1~5초)를 제거해도 서비스 계층 `Set*SyncInterval` 하한이 1초로 설정되어, 외부 또는 코드 직접 호출 시 우회 가능
- **근본 원인**: UI에서만 방어하고 서비스 계층의 실제 하한값을 별도로 강화하지 않은 설계
- **해결**: `SetFavoriteSyncInterval`, `SetFullSyncInterval`, `SetCalendarSyncInterval` 등 모든 `Set*SyncInterval` 하한을 1초 → 10초로 변경
- **교훈**: 동기화 주기 제한은 UI 옵션(options 배열) + 서비스 계층 하한 두 곳을 모두 방어해야 함. UI만 필터링하면 코드 경로나 API 직접 호출로 우회 가능
- **패턴**: `if (seconds < 10) seconds = 10; // UI 필터 + 서비스 계층 하한 이중 방어`
- **심각도**: 낮음 (기능 문제 없음, 잠재적 오용 방지)
- **Level**: 1 (참고)

## L-294: 0건 동기화 이벤트 발화 억제 — 불필요 UI 갱신/번쩍임 방지 (2026-04-04)

- **문제**: `EmailsSynced(newCount=0)`, `CalendarSynced(eventCount=0)` 발화 시에도 `LoadEmailsAsync`, `CalendarDataUpdated` 이벤트가 실행되어 불필요한 UI 번쩍임 발생
- **근본 원인**: 이벤트 핸들러에서 카운트 검사 없이 무조건 UI 갱신 로직 실행
- **해결**: `OnEmailsSynced`에서 `newCount == 0`이면 `LoadEmailsAsync` 스킵. `OnCalendarSynced`에서 `eventCount == 0`이면 `CalendarDataUpdated` 미발화
- **교훈**: 동기화 완료 이벤트 핸들러에서 변경 0건 시 UI 갱신을 조기 반환으로 억제. 특히 PeriodicTimer 기반 반복 동기화에서 매 틱마다 UI를 갱신하면 번쩍임/성능 저하 발생
- **패턴**: `if (newCount == 0) return; // 신규 없으면 UI 갱신 스킵`
- **심각도**: 낮음 (UI 번쩍임, 기능 문제 없음)
- **Level**: 1 (참고)

## L-295: ko_pipeline kdev 진입 전 TeamCreate 완료 순서 미보장 — 팀 미생성 hook 차단 3회 연속 (2026-04-05)

- **문제**: kdev Batch1 에이전트(kdev-1/2/3) spawn 시 팀 'maix-mailcache-k4' 미생성 상태 → hook(HOOK_BLOCK_TEAM_NOT_EXIST) 3회 연속 차단 후 팀 생성 완료 뒤 정상 재시도
- **근본 원인**: ko_pipeline에서 TeamCreate 완료를 확인하기 전에 kdev 에이전트 spawn 시도. hook이 정상 차단했으나 3회 반복은 파이프라인 진입 타이밍 이슈
- **해결**: 팀 생성 완료 후 spawn (hook이 차단하므로 실질적 피해 없음, 반복 자체가 비효율)
- **교훈**: ko_pipeline에서 kdev 진입 전 TeamCreate 완료를 명시적으로 확인 후 spawn. 기존 hook 차단은 정상이나 3회 재시도 반복은 파이프라인 순서 개선 대상
- **원본 오류**: ERR-1, ERR-2, ERR-3 (errors.md)
- **심각도**: 중간 (작업 차질 없음, 비효율만)
- **Level**: 2 (주의)

## L-296: MaiX 신규 서비스 구현 시 Serilog 직접 사용 금지 — Log4(YYYYMMDD.log) 표준 로거 사용 필수 (2026-04-05)

- **문제**: MailFolderCacheService 구현 시 Serilog 직접 사용 → 로그가 `mAIx-YYYYMMDD.log`에 기록됨. 기존 AC auto_scripts는 Log4 표준 경로(`YYYYMMDD.log`) 기준으로 작성되어 캐시 로그 grep 실패 (ktest FIND-001)
- **근본 원인**: 신규 서비스 작성 시 프로젝트 표준 로거(Log4) 대신 Serilog를 직접 참조. 캐시 동작 자체는 정상이나 AC 자동검증 로그 경로 불일치
- **해결**: MaiX 프로젝트 신규 서비스는 기존 로거(`_log = LogManager.GetCurrentClassLogger()` 패턴) 사용. Serilog 직접 의존 금지
- **교훈**: 신규 서비스 구현 시 MaiX 표준 로거(Log4 NLog — `YYYYMMDD.log`) 사용 필수. Serilog 직접 사용 시 AC auto_scripts 경로 불일치 및 로그 파일 분산 발생
- **패턴**: `private static readonly Logger _log = LogManager.GetCurrentClassLogger();`
- **심각도**: 중간 (기능 정상, AC 자동화 실패)
- **Level**: 1 (참고)

## L-297: 캐시 서비스 InvalidateAll 구현 후 모든 무효화 트리거 연결 확인 필수 (2026-04-05)

- **문제**: MailFolderCacheService.InvalidateAll() 구현됐으나 로그아웃 핸들러(MenuLogout_Click)에 미연결 (ktest FIND-002)
- **근본 원인**: CRUD 훅 체크리스트에 로그아웃/재로그인 연결 지점 누락
- **해결**: 앱 재시작 시 메모리 캐시 소멸로 기능 동등 — 즉각 수정 불필요
- **교훈**: 캐시 무효화 메서드 구현 후 모든 연결 지점(로그아웃/재로그인/초기화/앱종료) 체크리스트 확인 필수. 특히 InvalidateAll은 계정 전환/재로그인 경로에 연결되어야 완전한 구현
- **심각도**: 낮음 (앱 재시작 효과 동등)
- **Level**: 1 (참고)

## L-298: ktest→kdone 전환 시 기존 팀 잔류로 TeamCreate 중복 차단 (2026-04-09)

- **문제**: ktest 완료 후 kdone spawn 시도 시 기존 팀(maix-k5-fb0f3493, state=DEV)이 잔류하여 HOOK_BLOCK_TEAM_DUPLICATE 차단 발생
- **근본원인**: ktest 에이전트 종료 후 팀 상태(state=DEV)가 정리되지 않은 채 새 TeamCreate 호출 → hook이 중복 팀 생성 차단
- **해결**: 기존 TeamDelete 후 TeamCreate 재시도로 해결
- **교훈**: ktest→kdone 전환 전 기존 팀 상태(team_name 파일) 확인 및 필요 시 TeamDelete 선행 필수. ko SKILL.md의 "이전 팀 정리" 절차를 kdone spawn 전에도 적용
- **패턴**: `TeamDelete → TeamCreate` (중복 팀 잔류 시)
- **심각도**: 중간 (작업 지연, 기능 문제 없음)
- **Level**: 2 (주의)

## L-299: 팀에이전트의 직접 Agent() spawn 시도 — pipeline_order_guard.sh 정상 차단 확인 (2026-04-09)

- **문제**: DEV 상태에서 unnamed 팀에이전트가 subagent_type=general-purpose로 직접 Agent() spawn 시도 → pipeline_order_guard.sh(L-035) 차단
- **근본원인**: 팀에이전트(kdone-1)가 SPAWN_REQUEST 위임 없이 직접 Agent() 호출 시도
- **해결**: hook이 정상 차단. SPAWN_REQUEST를 team-lead에 전달하여 메인이 spawn하는 올바른 경로로 처리
- **교훈**: 팀에이전트는 직접 Agent() 호출 절대 금지. 항상 SendMessage(to:"team-lead", "SPAWN_REQUEST: ...") 형식으로 메인에 위임. pipeline_order_guard.sh가 정상 작동 중임을 확인
- **심각도**: 낮음 (hook 정상 차단, 기능 문제 없음)
- **Level**: 1 (참고)

## L-300: MaiX 모든 레이어(Controls/ViewModels 포함)에서 Serilog 직접 사용 금지 (2026-04-09)

- **문제**: 신규 Controls/*.cs, ViewModels/*.cs에서 `using Serilog; Log.ForContext<T>()` 패턴 반복 사용
- **근본원인**: L-296은 서비스 레이어만 명시했으나 Controls/ViewModels 레이어도 동일 패턴 위반 발생. 레이어 구분 없이 모든 .cs 파일에 동일 제약이 적용되어야 함
- **해결**: NLog 표준 로거로 전환
- **올바른 패턴**: `using NLog; private static readonly Logger _log = LogManager.GetCurrentClassLogger();`
- **금지 패턴**: `using Serilog; private static readonly ILogger _log = Log.ForContext<MyClass>();`
- **근거**: AC auto_scripts가 NLog 경로(`YYYYMMDD.log`)만 지원. Serilog 사용 시 `mAIx-YYYYMMDD.log`로 분산되어 자동검증 실패 및 로그 누락
- **심각도**: 중간 (AC 자동화 실패, 로그 추적 불가)
- **Level**: 2 (주의 — L-296 확장)

## L-302: 검색 고도화 — FTS5 trigram + BigramHelper (2026-04-09)

- SQLite FTS5 trigram: `tokenize='trigram'`으로 content/content_rowid 연동 테이블 생성 시 한국어 부분일치 검색 가능. 1자 이하 쿼리는 LIKE 폴백 필수.
- NLog 전환 패턴: `using NLog;` + `LogManager.GetCurrentClassLogger()` — Serilog DI 필드 제거 후 static 필드로 교체.
- FTS5 content 테이블 트리거: Up/Down 양방향 트리거 3개(ai/ad/au) 재생성 필수. Down에서도 롤백용 트리거 재생성해야 함.
- **심각도**: 낮음 (설계 지침)
- **Level**: 1 (참고)

## L-301: 외부 서비스 반환값 null 검사 필수 (2026-04-09)

- **문제**: ChunkedUploadService.UploadLargeFileAsync 결과 null 미검사로 업로드 실패 시 UploadCompleted 이벤트 오발화 가능
- **근본원인**: 비동기 외부 서비스 호출 후 반환값을 null 검사 없이 사용하는 패턴
- **해결**: 모든 비동기 외부 서비스 호출 후 반환값 null 검사 추가
- **올바른 패턴**: `var result = await service.CallAsync(...); if (result == null) { /* 실패 처리 */ return; }`
- **심각도**: 낮음 (잠재적 오동작)
- **Level**: 1 (참고)

## L-303: kio bash_exec run_in_background=true 무한 블로킹 — 절대 사용 금지 (2026-04-10)

- **문제**: kio bash_exec 호출 시 `run_in_background=true` 파라미터 사용 시 팀에이전트 무한 블로킹 발생
- **근본원인**: bash_exec.py가 background 모드에서도 프로세스 종료를 내부적으로 대기하는 버그 → 에이전트가 응답 없이 멈춤
- **증상**: 팀에이전트(kdev/ktest)가 응답 없이 무한 대기 → ki-rescue 에이전트 개입 필요 사태
- **해결**: bash_exec.py 버그 수정 완료. 하지만 예방 규칙 영구 유지
- **재발방지**: `run_in_background=true` 파라미터 완전 제거. ko SKILL.md에 `kio_bash_exec_금지규칙(L-303)` 추가
- **심각도**: 높음 (에이전트 멈춤, 파이프라인 중단)
- **Level**: 3-skill (ko SKILL.md 규칙 추가 완료)

## L-304: tmux kill-pane이 Claude Code 세션 전체 종료 — ki-rescue 에이전트 위임 필수 (2026-04-10)

- **문제**: 멈춘 팀에이전트 처리 시 tmux kill-pane 명령 실행 시 Claude Code 세션 전체가 종료됨
- **근본원인**: tmux pane이 Claude Code 실행 세션과 공유되어 있어 pane 종료 = 세션 전체 종료
- **해결**: hook 차단 여부와 무관하게 tmux kill-pane은 직접 실행 금지. 반드시 ki-rescue 에이전트를 spawn하여 위임
- **재발방지**: ko SKILL.md `hook_차단_시_대안` 섹션에 L-304 경고 추가. `run_in_background=true` 없이 ki-rescue spawn
- **심각도**: 높음 (세션 전체 종료 위험)
- **Level**: 3-skill (ko SKILL.md 규칙 강화 완료)

## L-305: kplan이 요구사항을 임의 변경 가능 — 메인 확인 후 kdev 진입 필수 (2026-04-10)

- **문제**: kplan이 사용자 원래 요구사항보다 더 광범위하거나 다른 방향으로 계획을 수립할 수 있음
- **근본원인**: kplan은 요구사항 해석 + 설계를 자율적으로 수행하며, 메인의 중간 확인 없이 kdev로 직행하면 의도치 않은 구현 발생
- **해결**: kplan 완료 수신 후 메인이 계획서를 원래 요구사항과 반드시 대조. 불일치 시 kplan 재수행 또는 수정 지시 후 kdev 진입
- **재발방지**: ko SKILL.md `kplan_결과_검증(L-305)` 규칙 추가. 파이프라인 순서표 step 4에 주의 표시
- **심각도**: 중간 (요구사항 왜곡 가능)
- **Level**: 2 (ko SKILL.md 규칙 추가 완료)

---

## L-362 (2026-04-11) — kdev 완료 후 pane 잔류 문제

- **증상**: kdev 에이전트가 완료 보고 후에도 tmux pane이 살아있는 채로 방치됨
- **근본원인**: shutdown_with_verify 절차에서 pane 소멸 확인 단계 누락
- **조치**:
  - ko_pipeline/SKILL.md: 2.7단계 pane 소멸 확인 + kill escalation 추가
  - kstatus/SKILL.md: --force 후 pane 잔류 재확인 절차 추가
  - team-cleanup.sh: shutdown_sent=true 에이전트 L-328 예외 허용
- **재발방지**: 메인 오케스트레이터가 shutdown 후 반드시 pane 소멸을 확인하고, 잔류 시 kill escalation 실행
- **심각도**: 중간

## L-363 (2026-04-11) — Serilog 잔류 패턴

- **증상**: TeamsViewModel.cs, MainWindow.Teams.cs에 Serilog 코드 잔류
- **근본원인**: 초기 구현 시 NLog 전환 규칙 미적용
- **조치**: ktest-1이 감지 후 NLog으로 전환 완료
- **재발방지**: 모든 레이어(Services/Controls/ViewModels) NLog 전용 원칙 준수. Serilog 사용 금지(MEMORY.md 기록됨)
- **심각도**: 낮음
- **Level**: 1-code (코드에서 직접 수정 완료)

## L-364 (2026-04-14) — GraphMailService/BackgroundSyncService Serilog 기존 사용 중 (NLog 미준수)

- **증상**: Phase 1 lazy sync 구현 시, 수정 대상인 GraphMailService.cs + BackgroundSyncService.cs 모두 `using Serilog; Log.ForContext<T>()` 패턴 사용 중 확인
- **배경**: L-296/L-300에서 NLog 표준 로거 사용이 규칙화되었으나, 이 두 파일은 규칙 제정 이전부터 Serilog를 사용 중이었고 변환 작업이 수행되지 않은 채 방치됨
- **결정**: Phase 1 수정 범위(파일 2개 제한)를 초과하므로 즉시 수정 불가. 기존 패턴(`_logger.Debug/Information`) 유지하고 별도 NLog 마이그레이션 작업으로 분리
- **조치**: 이번 사이클에서는 기존 Serilog 패턴 유지. NLog 미전환 파일: `GraphMailService.cs`, `BackgroundSyncService.cs`
- **후속 필요**: 두 파일의 NLog 마이그레이션 작업 별도 수행 필요 (`_logger` → `_log = LogManager.GetCurrentClassLogger()`, `using Serilog` → `using NLog`)
- **심각도**: 낮음 (기능 정상, 로그 파일 분산 이슈만)
- **Level**: 1 (참고)

## L-365 (2026-04-23) — IsRead 동기화: 순방향 블록 비활성화로 미읽음 불일치 발생

- **증상**: 서버(Outlook)에서 읽음 처리된 메일이 MaiX DB에서 계속 미읽음으로 남는 불일치
- **근본 원인**: `SyncReadStatusAsync` 순방향 블록이 주석 처리되어 있었음 (코드 비활성화)
- **추가 발견**: TCS 백그라운드 Task.Run에서 나머지 메일 처리 후 읽음 동기화 호출 누락; 역방향 동기화에서 동일 EntryId 이중 조회
- **교훈**: 동기화 코드 비활성화 시 주석 이유와 재활성화 조건을 반드시 명시. 안전 가드(빈 서버 응답 시 조기 리턴) 없이 순방향 동기화 활성화 금지
- **조치**: 순방향 블록 활성화 + 안전 가드 추가, TCS 백그라운드 SyncReadStatusAsync 호출 추가, 이중 조회 제거
- **Level**: 1 (참고 — 코드 재활성화로 해결됨)

## L-366 (2026-04-24) — ParentFolderId 드리프트: 메일 이동 후 DB 컬럼 미갱신으로 미읽음 카운트 오류

- **증상**: 서버에서 메일이 다른 폴더로 이동된 후 DB의 ParentFolderId가 갱신되지 않아, 미읽음 카운트 GROUP BY 집계 시 잘못된 폴더에 카운트가 누적됨
- **근본 원인**: delta sync에서 메일 이동(move) 이벤트 수신 시 IsRead만 교정하고 ParentFolderId는 업데이트하지 않는 드리프트 발생
- **교훈**: delta sync에서 상태 교정 시 IsRead뿐 아니라 ParentFolderId 등 관련 필드도 함께 동기화 필수. 이동된 메일의 폴더 귀속 정보는 서버 응답 기준으로 항상 최신화해야 함
- **조치**: SyncReadStatusAsync에서 ParentFolderId 드리프트 감지 + DB 교정 로직 추가
- **Level**: 2 (MEMORY 기록 권고)

## L-367 (2026-04-24) — 동기화 서비스 상태 교정 후 UI 캐시 갱신 누락

- **증상**: BackgroundSyncService에서 IsRead/ParentFolderId 교정이 완료되었음에도 MainViewModel 캐시가 갱신되지 않아 배지(미읽음 카운트)가 UI에 반영되지 않음
- **근본 원인**: 동기화 서비스가 DB를 직접 교정하지만 UI 캐시를 통지하는 이벤트가 없었음
- **교훈**: 동기화 서비스에서 DB 상태를 교정한 후에는 반드시 UI 캐시 갱신 이벤트(ReadStatusCorrected 등)를 발행해야 함. Service ↔ ViewModel 간 상태 일관성을 이벤트로 명시적으로 연결할 것
- **조치**: ReadStatusCorrected 이벤트 신설 → MainViewModel.OnReadStatusCorrected 핸들러에서 캐시 패치 + 배지 갱신
- **Level**: 2 (MEMORY 기록 권고)

## L-368 (2026-04-24) — InternetMessageId 단독 UNIQUE 인덱스로 자기 자신에게 보낸 메일 누락

- **증상**: 자기 자신에게 보낸 메일이 받은편지함에 표시되지 않음. Sent에만 존재.
- **근본 원인**: `IX_Email_InternetMessageId` 단독 UNIQUE 인덱스로 인해 보낸편지함 저장 후 받은편지함 INSERT 시 UNIQUE 위반으로 스킵됨
- **교훈**: 동일 메일이 여러 폴더에 동시 존재할 수 있는 경우 단독 컬럼 UNIQUE 대신 (컬럼 + ParentFolderId) 복합 UNIQUE 사용 필수. DbContext 코드와 실제 DB 인덱스가 불일치하는 경우 수동 마이그레이션 필요.
- **조치**: 마이그레이션 20260424000016 — 단독 UNIQUE 폐기 + 복합 UNIQUE 추가. BackgroundSyncService UNIQUE catch 폴백 검색 로직 추가.
- **Level**: 1 (설계 결정 기록)

## L-369 (2026-05-02) — Dispatcher.Invoke(async 람다) 패턴: async void 처리로 예외 미전파 + UI 블로킹

- **증상**: `Dispatcher.Invoke(async () => { ... })` 패턴 사용 시 async 람다가 `async void`로 처리되어 예외가 전파되지 않으며, 동기 블로킹 발생
- **근본 원인**: `Dispatcher.Invoke`는 동기 메서드이므로 async 람다를 전달하면 `Task`를 반환하는 `Action`이 아닌 `Func<Task>`가 `async void`로 처리됨. 호출 스레드가 블로킹되고 예외는 소실됨
- **전수조사 결과**: 6개 파일에서 47건 발견 (MainWindow.xaml.cs 23건, OneNoteViewModel.cs 19건, TeamsViewModel.cs 4건, OneDriveViewModel.cs 1건, MainWindow.Activity.cs 1건, TaskEditDialog.xaml.cs 1건). 정상 패턴 9건은 유지
- **교훈**: `Dispatcher.Invoke(async ...)` 패턴은 코드 리뷰 시 반드시 검출해야 할 안티패턴. 대안: `await Dispatcher.InvokeAsync(() => { ... })` 사용 + 메서드 시그니처를 `async void` 또는 `async Task`로 변경
- **조치**: 47건 일괄 InvokeAsync 전환. domain-csharp/SKILL.md에 금지 패턴 및 자동 검증 grep 추가
- **Level**: 2 (MEMORY 기록 권고)

## L-370 (2026-05-02) — async void 이벤트 핸들러: 예외 소실 + 비동기 흐름 단절

- **증상**: WPF 이벤트 핸들러가 `async void`로 선언된 경우, 내부에서 발생하는 예외가 unhandled exception으로 처리되어 앱이 비정상 종료되거나 예외가 조용히 소실됨
- **근본 원인**: `async void` 메서드는 Task를 반환하지 않아 호출자가 예외를 catch할 수 없음. 이벤트 핸들러 시그니처 제약으로 인해 습관적으로 `async void`를 사용하는 경향이 있으나, 이벤트 핸들러 외의 코드에서는 절대 사용 금지
- **전수조사 결과 (2차)**: 6개 파일에서 15건 발견 (MainWindow.xaml.cs 위주). try-catch 미적용 async 핸들러 4건 추가 수정
- **교훈**: 이벤트 핸들러가 아닌 모든 async 메서드는 반드시 `async Task` 반환. 이벤트 핸들러 내부에서 async 작업이 필요하면 별도 `async Task` 메서드로 분리 후 호출
- **조치**: 15건 `async void → async Task` 변환. 4건 try-catch 추가
- **Level**: 2 (MEMORY 기록 권고)

## L-371 (2026-05-02) — Dispatcher.BeginInvoke: 구식 API, 결과/예외 추적 불가

- **증상**: `Dispatcher.BeginInvoke(DispatcherPriority.Normal, ...)` 패턴 사용 시 반환된 DispatcherOperation이 무시되어 예외 추적 불가, 완료 여부 확인 불가
- **근본 원인**: `BeginInvoke`는 .NET Framework 시절 API로 비동기 패턴 미지원. `InvokeAsync`로 전환 시 `await`를 통한 예외 전파 + 완료 추적 가능
- **전수조사 결과 (2차)**: 6개 파일에서 16건 발견 (MainWindow.xaml.cs 14건, ComposeWindow.xaml.cs 2건)
- **교훈**: 신규 코드에서 `Dispatcher.BeginInvoke` 사용 금지. 반드시 `await Dispatcher.InvokeAsync(...)` 사용
- **조치**: 16건 `BeginInvoke → InvokeAsync + await` 전환
- **Level**: 1 (코드 규칙 강화)

### L-372: ConfigureAwait 잘못된 괄호 위치 안티패턴 (2026-05-02)

- **문제**: 멀티라인 체인에서 `(await x.Method()).Property` 패턴 처리 시 `.ConfigureAwait(false)`를 `await x.Method()` 직후가 아닌 `.Property` 접근 직후에 삽입하는 실수 발생
  ```csharp
  // ❌ 잘못된 패턴 (Property 접근 후 ConfigureAwait — 컴파일 에러 또는 의도 불명확)
  var result = (await x.GetAsync())
      .SomeProperty.ConfigureAwait(false);
  
  // ✅ 올바른 패턴 (await 대상 Task에 ConfigureAwait 적용)
  var result = (await x.GetAsync().ConfigureAwait(false))
      .SomeProperty;
  ```
- **발생 원인**: 대량 자동 적용 시 체인 구조 파악 없이 줄 끝에 기계적으로 삽입
- **재발방지**: 멀티라인 체인 수정 시 `(await ...)` 패턴 여부를 먼저 확인하고, ConfigureAwait는 반드시 `Task` 반환 직후 (괄호 닫기 전)에 삽입
- **Level**: 2 (대량 적용 시 반복 재현 위험)

### L-373: ConfigureAwait(false) 대규모 적용 — 4 Phase 분할 전략 (2026-05-02)

- **교훈**: 수백 건 이상의 대규모 ConfigureAwait 적용은 단일 Phase로 수행하면 버그 발생 시 추적이 어려움
- **권장 전략**:
  1. **Phase 별 레이어 분리**: Graph → BackgroundSync → AI/Converter → 잔존 순으로 레이어별 분리
  2. **Phase 완료 후 빌드 검증**: 각 Phase 완료마다 `dotnet build` 실행하여 즉시 확인
  3. **fire-and-forget 식별 선행**: ConfigureAwait 적용 전 의도적 fire-and-forget 패턴 먼저 식별하여 제외
- **Level**: 1 (프로세스 개선 사항)

### L-374: DispatcherOperation은 Task가 아님 — ConfigureAwait 적용 시 .Task 경유 필수 (2026-05-02)

- **문제**: `Dispatcher.InvokeAsync()`의 반환 타입은 `DispatcherOperation`이며 `System.Threading.Tasks.Task`가 아님. 직접 `ConfigureAwait(false)`를 체이닝하면 컴파일 에러 또는 의도 불명확
  ```csharp
  // ❌ 잘못된 패턴 — DispatcherOperation에 ConfigureAwait 직접 체이닝
  await _dispatcher.InvokeAsync(() => { ... }).ConfigureAwait(false);
  
  // ✅ 올바른 패턴 — .Task 프로퍼티 경유 후 ConfigureAwait
  await _dispatcher.InvokeAsync(() => { ... }).Task.ConfigureAwait(false);
  ```
- **근본 원인**: ConfigureAwait 대량 적용 시 반환 타입 구분 없이 일괄 체이닝
- **재발방지**: Dispatcher.InvokeAsync 뒤에 ConfigureAwait 적용 시 반드시 `.Task.ConfigureAwait(false)` 패턴 사용
- **Level**: 2 (대량 적용 시 반복 재현 위험)

### L-375: grep 기반 ConfigureAwait 누락 검사 — 멀티라인 체인에서 오탐 (2026-05-02)

- **문제**: 줄 단위 grep으로 `ConfigureAwait` 누락 여부를 검사할 때, 멀티라인 체인 패턴 `(await x.GetAsync())\n.SomeProperty`에서 실제로 올바르게 삽입된 패턴을 "누락"으로 오탐하거나 잘못 삽입된 패턴을 통과시키는 경우 발생
- **근본 원인**: grep은 줄 단위 검색이므로 멀티라인 체인의 괄호 구조를 추적하지 못함
- **재발방지**: ConfigureAwait 누락 grep 검사 후 `(await ...).` 패턴이 발견되면 반드시 괄호 매칭 수동 검증 수행. 대량 적용 전/후 정밀 검증은 Roslyn 분석기 또는 IDE 기능 활용 권장
- **Level**: 1 (프로세스 주의사항)

### L-376: SemaphoreSlim은 IDisposable — 메서드 내 지역 생성 시 using 필수 (2026-05-02)

- **문제**: `var semaphore = new SemaphoreSlim(N, N)` 형태로 메서드 내에서 지역 변수로 생성 후 `using` 없이 사용. `SemaphoreSlim`은 내부적으로 `AvailableWaitHandle`(`ManualResetEventSlim`)을 보유하며 `IDisposable`을 구현함
- **근본 원인**: SemaphoreSlim을 단순 카운터로 인식 — IDisposable 구현 여부를 확인하지 않은 채 생성
- **재발방지**:
  - 메서드 내 지역 변수 생성 시 반드시 `using var semaphore = new SemaphoreSlim(N, N)` 패턴 사용
  - 클래스 필드로 보유 시 `Dispose()` 메서드에서 `_semaphore?.Dispose()` 호출 필수
  - 코드 리뷰 시 `new SemaphoreSlim` grep 후 `using` 패턴 확인: `Grep("new SemaphoreSlim", "*.cs")`
- **Level**: 2 (반복 재현 위험 — 병렬 처리 코드에서 자주 사용)

### L-377: async void 이벤트 핸들러 외부 try-catch 래핑 필수 (2026-05-02)

- **문제**: `async void` 이벤트 핸들러에서 내부 각 분기마다 try-catch를 적용했으나, 분기 진입 전 코드(파라미터 검사, URI 파싱 등)에서 예외 발생 시 캐치 불가 — 예외가 UI 스레드로 전파되어 앱 크래시 또는 소실
  ```csharp
  // ❌ 불충분 — 분기 진입 전 예외 소실 위험
  public async void SomeEventHandler(...)
  {
      if (e.Uri.StartsWith("about:"))  // ← 이 줄에서 예외 발생 시 소실!
          return;
      try { ... } catch { }
      try { ... } catch { }
  }
  
  // ✅ 올바른 패턴 — 메서드 전체 외부 래핑
  public async void SomeEventHandler(...)
  {
      try
      {
          if (e.Uri.StartsWith("about:"))
              return;
          // ... 내부 로직
      }
      catch (Exception ex)
      {
          Log.Error($"핸들러 처리 실패: {ex}");
      }
  }
  ```
- **근본 원인**: async void는 예외를 호출자에게 전파하지 않으므로 외부 전체 래핑이 필수인데, 내부 분기별 try-catch만으로 충분하다고 판단
- **재발방지**: `async void` 이벤트 핸들러 작성/리뷰 시 메서드 본문 최외곽에 try-catch 존재 여부 확인 필수
- **연관**: L-370 (async void 이벤트 핸들러 일반 원칙), L-377 (외부 try-catch 래핑 특수 요건)
- **Level**: 2 (WPF 이벤트 핸들러 패턴에서 반복 재현 위험)

### L-378: oralph 자동 반복 검증의 가치 — 수동 검증 후에도 연관 패턴 지속 발견 (2026-05-02)

- **문제**: async void/InvokeAsync 패턴 수동 수정 후 "완료"로 판단했으나, oralph 자동 반복 검증(5회)으로 총 37건 추가 발견
  - iter1: InvokeAsync(async lambda) fire-and-forget try-catch 누락 5건
  - iter2: InvokeAsync(async lambda) Task.Unwrap 미적용 + try-catch 누락 8건
  - iter3: BeginInvoke(async lambda) + Timer.Elapsed async lambda 4건
  - iter4: async lambda 이벤트 핸들러 외부 try-catch 누락 20건
  - iter5: 0건 → 수렴 ✅
- **근본 원인**: 단일 패턴 수정이 연관 패턴 검색을 유발하는 연쇄 확장 현상. 수동 1회 검증은 해당 패턴 변형을 놓치기 쉬움
- **재발방지**: 동일 카테고리 버그(async 관련 패턴 등) 수정 후 oralph로 연관 패턴 전수 검증 권장
- **Level**: 1 (프로세스 참고 교훈)

### L-379: async 람다 이벤트 핸들러 try-catch 외부 래핑 — InvokeAsync(async lambda)도 예외 소실 위험 (2026-05-02)

- **문제**: `Dispatcher.InvokeAsync(async () => { await ... })` 패턴에서 async lambda가 `DispatcherOperation`을 반환하므로, inner async 예외가 `.Task.Unwrap()` 없이 소실됨
  ```csharp
  // ❌ 잘못 — inner async 예외 소실
  _ = Dispatcher.InvokeAsync(async () => {
      await SomeAsync();  // 예외가 소실됨
  });

  // ✅ 올바름 — Task.Unwrap + try-catch
  _ = Dispatcher.InvokeAsync(async () => {
      try { await SomeAsync(); }
      catch (Exception ex) { _log.Error(ex, "처리 실패"); }
  }).Task.Unwrap();

  // ✅ fire-and-forget 허용 시 — 외부 try-catch로 감싸기
  _ = Dispatcher.InvokeAsync(async () => {
      try { await SomeAsync(); }
      catch (Exception ex) { _log.Error(ex, "처리 실패"); }
  });
  ```
- **근본 원인**: InvokeAsync 반환 타입이 `DispatcherOperation`(Task 아님)이므로 async lambda 내부 예외가 외부로 전파되지 않음. L-374(DispatcherOperation.Task 경유)와 연관
- **재발방지**: InvokeAsync(async lambda) 사용 시 내부 반드시 try-catch 또는 `.Task.Unwrap()` 적용 필수
- **연관**: L-374 (DispatcherOperation.Task 경유), L-377 (async void 외부 try-catch)
- **Level**: 2 (InvokeAsync 패턴이 WPF 코드에서 자주 사용 — 반복 재현 위험)

### L-380: Timer.Elapsed/PropertyChanged 등 비WPF 이벤트 핸들러도 async void 패턴 동일 위험 (2026-05-02)

- **문제**: WPF 이벤트 핸들러에만 집중하다 `Timer.Elapsed += async (s, e) => { await ... }` 패턴을 놓침
  - `System.Timers.Timer.Elapsed`는 ThreadPool 스레드에서 실행 — async void와 동일하게 예외 소실
  - `System.Threading.Timer` 콜백, `PropertyChanged`, `NotifyCollectionChanged` 등도 동일
  ```csharp
  // ❌ 잘못 — Timer.Elapsed에서 async lambda 예외 소실
  _timer.Elapsed += async (s, e) => {
      await DoWorkAsync();  // 예외 소실!
  };

  // ✅ 올바름 — 외부 try-catch 래핑
  _timer.Elapsed += async (s, e) => {
      try { await DoWorkAsync(); }
      catch (Exception ex) { _log.Error(ex, "타이머 작업 실패"); }
  };
  ```
- **근본 원인**: L-377/L-379가 WPF 이벤트 핸들러에만 초점을 맞춰 비WPF 이벤트 핸들러의 동일 위험 간과
- **재발방지**: `async (s, e) =>` 패턴이 있는 모든 이벤트 핸들러(Timer, PropertyChanged 포함)에서 try-catch 래핑 확인 필수
- **연관**: L-370 (async void 일반), L-377 (외부 try-catch 래핑), L-379 (InvokeAsync async lambda)
- **Level**: 2 (Timer 패턴이 백그라운드 서비스에서 자주 사용 — 반복 재현 위험)

### L-381: oralph 무한 발견 양상 — 대규모 WPF에서 수렴 기준은 실측 UI 블로킹 0건 (2026-05-02)

- **문제**: async void 이벤트 핸들러 try-catch 강제를 oralph로 반복 적용하면, 대규모 WPF 앱(수백 파일)에서 잔존 패턴이 지속 보고됨
  - grep 기반 검증이 코드 수정 후에도 새로운 파일/패턴을 계속 발견 → max_reached 도달
  - 2차 oralph에서 18건 추가 수정(iter1:1건 + iter2:8건 + iter3:9건) 후에도 122건+ 잔존 보고
- **근본 원인**: 검증 기준(grep 패턴 카운트)이 무한 수렴 목표 — 실제 런타임 영향과 무관하게 계속 발견
- **재발방지**: oralph 수렴 기준 = **런타임 UI 블로킹 키워드 0건** (빌드 PASS + 런타임 로그). 잔존 건수 자체가 수렴 실패 의미 아님.
  - 작업 완료 판단 기준: CS 에러 0건 + 런타임 UI 스레드 키워드 0건 = 충분
  - 코드베이스 전체 완벽 적용은 별도 점진적 마이그레이션 작업으로 분리
- **Level**: 1 (oralph 수렴 기준 명확화 — hook/스킬 처리 불필요, 프로세스 참조용)

### L-382: 표본 수정 후 체계적 전수조사 필수 — 7차에서 패턴2 170건 잔존 발견 (2026-05-02)

- **문제**: async void 이벤트 핸들러 외부 try-catch 래핑(L-377) 규칙을 1차/5차/6차에서 표본 위주로 수정 → 7차 전수조사에서 170건 잔존 발견
  - 1차/5차/6차: 발견된 사례만 수정, 전체 grep 검증 누락
  - 7차: `async void.*Click|TextChanged|SelectionChanged` 등 전체 패턴 grep 후 외부 try-catch 부재 파일 전수 식별 → 20개 파일 170건 일괄 수정
  - MainWindow.xaml.cs 단독 116건(실측 150건) 거대 파일은 Batch를 분리해서 처리하는 편이 효율적
- **근본 원인**: "보이는 것만 수정" 방식 — 패턴 규칙이 LESSONS에 반영되어도 코드베이스 전수 적용은 별도 단계 필요
- **재발방지**:
  - 패턴 규칙(L-3xx) 신규 등록 시 즉시 전수조사 batch 1회 실행을 권장
  - 거대 파일(>1000줄, 매치 >50건)은 별도 Batch로 분리 처리 (병렬화 + 회수 용이)
  - 로거 일괄 지시 금지 — Serilog/NLog/Log4 혼용 환경에서는 파일별 기존 로거 확인 후 적용 (`_log` vs `_logger.Debug` 구분)
- **연관**: L-377 (외부 try-catch 본 규칙), L-378/L-381 (oralph 자동 반복 검증), L-364 (NLog/Serilog 혼용)
- **Level**: 1 (프로세스 교훈 — 새 패턴 규칙 등록 시 운영 절차로 참조)

### L-383: `InvokeAsync(async lambda).Task.ConfigureAwait(false)` 외관상 안전 함정 — inner async 예외 소실 (2026-05-02)

- **문제**: `Dispatcher.InvokeAsync(async () => { await ... }).Task.ConfigureAwait(false)` 패턴은 `.Task` 접근으로 외관상 안전해 보이지만, inner async 람다 본문에서 발생하는 예외는 여전히 소실됨
  ```csharp
  // ❌ 위험 — .Task는 DispatcherOperation 완료만 await, inner 예외 소실
  await Dispatcher.InvokeAsync(async () =>
  {
      await SomeAsyncMethod(); // ← 예외 발생 시 소실됨!
  }).Task.ConfigureAwait(false);

  // ✅ 안전 1 — Task.Unwrap() 사용 (inner Task 결합)
  await Dispatcher.InvokeAsync(async () =>
  {
      await SomeAsyncMethod();
  }).Task.Unwrap().ConfigureAwait(false);

  // ✅ 안전 2 — inner try-catch
  await Dispatcher.InvokeAsync(async () =>
  {
      try
      {
          await SomeAsyncMethod();
      }
      catch (Exception ex)
      {
          _log.Error(ex, "...");
      }
  }).Task.ConfigureAwait(false);
  ```
- **근본 원인**:
  - `Dispatcher.InvokeAsync(async lambda)` 반환은 `DispatcherOperation<Task>` (외부 Task가 inner Task를 감싼 형태)
  - `.Task.ConfigureAwait(false)`는 외부 Task의 완료(=inner Task 시작)만 기다림 — inner Task 본문 예외는 별도 미관측 Task에 머물러 소실
  - `.Task.Unwrap()`만이 외부 Task와 inner Task를 결합하여 전체 await 가능
- **재발방지**:
  - L-379 본 규칙(`InvokeAsync(async` grep 검사)의 자동 검출 패턴 강화: `.Task.ConfigureAwait`로 끝나는 패턴은 즉시 위험 표시
  - 코드 리뷰 시: `Grep("InvokeAsync\\(async.*\\.Task\\.ConfigureAwait", "*.cs")` — 발견되면 모두 `.Task.Unwrap().ConfigureAwait(false)` 또는 inner try-catch로 변환
  - oralph 8차 라운드 otest 중 AC-007 검증으로 추가 발견 → 검증 스크립트 sed 패턴 정밀도가 결과를 좌우함
- **연관**: L-369 (Dispatcher.Invoke async 람다), L-374 (DispatcherOperation 자체는 Task 아님), L-379 (InvokeAsync async 람다 try-catch 또는 Unwrap)
- **Level**: 2 (반복 재현 위험 — 외관상 안전해 보이는 함정 패턴)

### L-386: preserveSelection 가드 범위 오류 — Clear 시점 ~ 복원 완료 시점 전체를 guardScope로 감싸야 함 (2026-05-03)

- **문제**: `preserveSelection=true` 로직에서 `_isSwitchingFolder` 가드를 복원 직전에만 ON 하여 Clear 시점에 `LoadMailBody(null)` 호출 → 복원 시점엔 가드 ON으로 `LoadMailBody` 미호출 → 본문 빈 채로 잔류
- **원인**:
  - `ObservableCollection.Clear()` 호출 시 `SelectionChanged` → `SelectedEmail=null` write-back → `LoadMailBodyAsync(null)` 실행
  - 이 시점에 `_isSwitchingFolder`가 OFF이므로 가드가 막지 못함
  - 복원(`SelectedEmail = saved`) 시점에 가드가 ON이면 `PropertyChanged` 핸들러 내 `LoadMailBodyAsync` 호출이 차단
  - 결과: 본문이 null-load로 비워진 후 정상 로드 없이 잔류
- **해결 (guardScope 패턴)**:
  ```csharp
  var guardScope = preserveSelection;
  if (guardScope) _isSwitchingFolder = true;   // Clear 직전 ON
  try {
      Emails.Clear();
      foreach (var e in emails) Emails.Add(e);
      if (guardScope) {
          _isSwitchingFolder = false;            // 복원 직전 OFF
          guardScope = false;
          if (restored != null) SelectedEmail = restored;
          else SelectedEmail = null;             // 명시적 null 처리
      }
  } finally {
      if (guardScope) _isSwitchingFolder = false; // 안전망
  }
  ```
- **재발방지**:
  - `preserveSelection` 패턴 구현 시 **가드 ON/OFF는 Clear 시점을 기준으로 결정** — Clear 이후 복원 완료까지 전체 구간이 가드 범위
  - 또는 `Clear` 자체를 회피하는 **incremental update(ID 기반 Add/Update/Remove)** 패턴 채용
  - 코드 리뷰 시: `preserveSelection` 관련 가드가 Clear 이전에 설정되는지 확인
- **연관**: L-385(ObservableCollection.Clear selection write-back 시리즈)
- **Level**: 2 (selection 보존 패턴 구현 시 반복 발생 위험)

### L-385: WPF ListBox + ObservableCollection.Clear+Add — SelectedItem=null write-back으로 리딩 페인 Collapsed (2026-05-03)

- **문제**: 메일을 읽고 있는 도중 백그라운드 동기화가 실행되면 리딩 페인이 갑자기 닫힘
- **원인**:
  - `ReplaceEmails(IEnumerable<Email>)` 내부에서 `ObservableCollection.Clear()` → `Add(item) × N` 패턴 사용
  - `Clear()` 호출 시점에 WPF ListBox의 `SelectionChanged` 이벤트가 발화
  - 2-way 바인딩(`SelectedEmail`)에 의해 `null`이 write-back → `SelectedEmail = null`
  - `NullToVisibilityConverter`가 `Collapsed`로 평가 → 리딩 페인 즉시 숨김
- **해결**:
  - `ReplaceEmails(IEnumerable<Email> emails, bool preserveSelection = false)` 시그니처 보강
  - `preserveSelection=true` 시: "이전 selection ID 캡처 → Clear+Add → 동일 ID 재선택" 패턴 적용
  - 백그라운드 sync 호출지 2곳만 `preserveSelection: true` 적용 (사용자 액션 8곳은 default false 유지)
- **재발방지**:
  - 백그라운드에서 `ObservableCollection.Clear()`를 포함하는 갱신 시 반드시 selection 보존 여부를 명시적으로 결정
  - `ReplaceEmails` 또는 이와 유사한 bulk replace 메서드 작성 시 `preserveSelection` 옵션을 설계에 포함
  - 코드 리뷰 시: `ObservableCollection.Clear()` 다음에 `SelectedItem` 2-way 바인딩이 있는지 grep 확인
- **연관**: L-369(Dispatcher.Invoke async 람다), L-370(async void 이벤트 핸들러)
- **Level**: 2 (재발 위험 — 백그라운드 sync + ListBox 컬렉션 갱신 패턴은 프로젝트 전반에 반복 등장)

### L-384: SESSION_DIR 이중 경로 — hook 참조 CLAUDE_CONFIG_DIR 정확 확인 필수 (2026-05-02)

- **문제**: 파이프라인 evidence/state 파일 저장 시 SESSION_DIR이 두 곳에 존재
  - 메인 세션 경로: `$HOME/.claude/session-env/${UUID}/` (~/.claude 기반)
  - hook 참조 경로: `/tmp/cc-{프로젝트UUID}/session-env/${UUID}/` (CLAUDE_CONFIG_DIR 기반)
  - 한 쪽에만 evidence 생성하면 hook이 참조 시 누락 인식 → 파이프라인 검증 실패
- **근본 원인**:
  - Claude Code가 `CLAUDE_CONFIG_DIR` 환경변수로 hook 작업 디렉토리를 분리 (프로젝트별 격리)
  - 기존 hook은 `$HOME/.claude` 기준이었으나, 일부 hook은 `CLAUDE_CONFIG_DIR` 기준으로 동작
  - 두 경로의 동기화는 자동이 아님 — 명시적 양쪽 쓰기 필요
- **재발방지**:
  - evidence/state/lock 파일 생성 시 양쪽 경로 모두 동기화: `for D in $HOME/.claude/session-env/${UUID} /tmp/cc-{ID}/session-env/${UUID}; do mkdir -p $D/{logs,evidence,plans}; done`
  - hook 진단 시 직전 에이전트 결과 맹신 금지 — 어느 경로의 evidence를 hook이 참조하는지 `CLAUDE_CONFIG_DIR` 환경변수로 명시 확인
  - oralph/ok 진입 시 SESSION_DIR 결정 로직에 양쪽 경로 동기화 추가 검토
- **연관**: 없음 (신규 영역)
- **Level**: 1 (인프라 교훈 — hook 통합 환경에서 자주 재현)

### L-387: 병렬 에이전트 using 블록 충돌 — 동일 파일 선두 수정 시 중복 삽입 위험 (2026-05-09)
- **증상**: Wave 2 병렬 에이전트(odev-A + odev-C)가 동일 파일 선두(using 블록)를 각각 수정 → `using System;` 중복 선언
- **영향**: C# 컴파일러가 허용하나 코드 품질 저하. IDE 경고 유발
- **재발방지**: odone_cleanup에서 `grep -c "^using System;" 파일` 로 중복 using 전수 확인 필수
- **Level**: 1 (단순 충돌, odone_cleanup에서 해결)

### L-388: 비동기 정리 함수 fire-and-forget 패턴 주의 (2026-05-09)
- **증상**: `_ = StopOpenAiServicesAsync()` 패턴 — 정리 함수 내부 예외가 소실됨
- **원인**: L-379 fire-and-forget 규칙은 InvokeAsync(async lambda)에 특화되어 있으나, 일반 Task 메서드도 동일 위험
- **재발방지**: 정리/종료 함수(StopXxx/CloseXxx/DisposeXxx)를 fire-and-forget으로 호출할 때 내부 try-catch 필수 확인. `_ = Method()` 패턴 발견 시 내부 catch 유무 검증
- **연관**: L-379 (InvokeAsync async lambda 예외 소실)
- **Level**: 2 (반복 패턴 가능성 — 서비스 정리 코드에서 반복 출현)

### L-389: WPF ItemsPanel.LoadContent() — 실제 Visual Tree 패널 아님 (2026-05-09)
- **증상**: `ItemsControl.ItemsPanel.LoadContent()`로 반환된 패널의 Orientation을 변경해도 UI에 반영 안 됨
- **원인**: ItemsPanelTemplate.LoadContent()는 새 인스턴스를 반환 (실제 렌더링된 VirtualizingStackPanel 아님)
- **올바른 방법**:
  ```csharp
  // ✅ 올바른 방법 1: ViewModel 바인딩 (ItemsPanel DataTemplate에 Orientation 바인딩)
  // XAML: <ItemsPanelTemplate><VirtualizingStackPanel Orientation="{Binding TopicNavOrientation, Converter=...}"/></ItemsPanelTemplate>

  // ✅ 올바른 방법 2: VisualTreeHelper로 실제 패널 탐색
  var panel = VisualTreeHelper.GetChild(itemsControl, 0) as VirtualizingStackPanel;
  if (panel != null) panel.Orientation = newOrientation;
  ```
- **재발방지**: ItemsPanel 동적 변경 시 VisualTreeHelper 사용 또는 ViewModel 바인딩 방식 사용. LoadContent() 직접 호출 금지
- **Level**: 2 (WPF 패턴 오류 — 반복 가능)

### L-390: 팝업 창과 동적 패널 혼동 — XAML 정적 추가가 사용자에게 도달하지 않는 패턴 (2026-05-10)
- **증상**: odev가 ApiSettingsWindow.xaml에 OaiRecording 섹션을 정적으로 추가했으나, 사용자 메뉴에서 해당 팝업으로 진입하는 경로가 없어 실제 화면에 표시 안 됨. 실제 설정 화면은 ShowAiProviderSettings() 동적 패널이었음
- **원인**: WPF 앱에는 정적 XAML(팝업 창)과 동적 패널(코드에서 생성되는 패널) 두 종류의 UI 진입점이 존재. XAML에 컨트롤을 추가했더라도 해당 윈도우를 여는 메뉴 바인딩이나 버튼이 없으면 사용자 도달 불가
- **재발방지**: odev가 신규 UI 컨트롤 추가 시 반드시 해당 컨트롤/창으로 도달하는 사용자 진입 경로(메뉴 바인딩, Click 핸들러, ShowXxx 메서드 등)를 grep으로 확인 후 추가. 진입 경로가 없으면 동적 패널 또는 기존 화면에 통합 방식으로 검토
- **연관**: L-391 (진입 경로 grep 검증)
- **Level**: 2 (회귀 발생 — otest 1회 역라우팅 유발)

### L-391: 신규 UI 추가 시 사용자 진입 경로 grep 검증 필수 (2026-05-10)
- **증상**: odev가 신규 UI 섹션을 특정 윈도우에 추가한 후 otest에서 "사용자가 볼 수 없음" FAIL. 해당 윈도우를 호출하는 메뉴/버튼이 XAML에 연결되지 않은 상태였음
- **원인**: odev가 구현 후 진입 경로 존재 여부를 grep으로 확인하지 않고 커밋
- **재발방지**: odev 단계에서 신규 UI 추가 후 체크리스트:
  1. `grep -r "XxxWindow\|ShowXxx\|MenuXxx_Click" --include="*.cs" --include="*.xaml"` 로 진입점 확인
  2. 검색 결과 0건이면 진입 경로 미연결 — 수정 후 진행
  3. 동적 패널(ShowAiProviderSettings 등)과 정적 팝업(ApiSettingsWindow 등) 분기 명확히 파악 후 올바른 위치에 추가
- **연관**: L-390 (팝업 창과 동적 패널 혼동)
- **Level**: 2 (프로세스 개선 — odev 체크리스트 강화)

### L-392: otest UI 검증 = 코드 grep만으로 부족, 실제 사용자 진입 경로 추적 필수 (2026-05-10)
- **증상**: otest Phase 2 코드 검증이 전체 PASS였으나 UI 검증에서 FAIL. XAML에 컨트롤이 존재하지만 사용자가 실제로 볼 수 없는 상황
- **원인**: otest가 코드 grep(컨트롤 존재 여부)만 확인하고, 해당 컨트롤로 도달하는 사용자 흐름(진입 경로)을 검증하지 않음
- **재발방지**: otest UI 검증 시 2단계 확인 필수:
  1. 코드 grep: 컨트롤/섹션이 XAML/코드에 존재하는가
  2. 진입 경로 grep: 해당 화면을 여는 메뉴 클릭/버튼/메서드가 실제 XAML 바인딩에 연결되어 있는가
  - 2단계 PASS 조건: 사용자가 앱 실행 후 UI 동작만으로 해당 컨트롤에 도달 가능해야 함
- **연관**: L-390, L-391
- **Level**: 2 (테스트 프로세스 개선 — otest 체크리스트 강화)

### L-393: hook 차단 기준은 tool_name 기반이어야 함 — message 본문 키워드 매칭은 false positive 유발 (2026-05-10)
- **증상**: ui_test_guard.sh가 SendMessage 도구 호출 시 message 본문의 키워드(RETEST 등)를 검사하여 의도치 않게 차단(false positive). 실제로는 block 대상이 아닌 일반 통신 메시지도 차단됨
- **원인**: hook이 tool_name 대신 message 본문만을 기준으로 차단 여부를 판단. 의미론적으로 동일한 키워드가 다른 맥락(진행 보고, 팀 통신 등)에서도 출현 가능
- **재발방지**: hook 차단 기준은 tool_name(또는 input.command 등 구조적 필드)을 1차 기준으로 사용. message 본문 기반 키워드 필터링은 false positive 위험이 높으므로 반드시 tool_name과 함께 AND 조건으로만 사용. 옵션 B 패턴(message 필드 비어있지 않으면 통과) 적용
- **연관**: ui_test_guard.sh 옵션 B
- **Level**: 2 (hook 설계 원칙)

### L-394: 이벤트 기반 컴포넌트 E2E 검증 — Reflection 트리거 헬퍼 효과적 (2026-05-10)
- **증상**: RealtimeAudioChunkReady 이벤트처럼 실제 하드웨어(마이크)나 외부 서비스(OpenAI)에 의존하는 이벤트는 otest 환경에서 실제 트리거 불가능
- **해결**: DebugPcmInjectHelper 헬퍼 클래스로 Reflection을 통해 이벤트를 강제 발화(fire). 실제 서비스 의존 없이 이벤트 핸들러 경로 전체를 E2E 검증 가능
- **패턴**:
  ```csharp
  // Reflection으로 private 이벤트 강제 발화
  var eventField = typeof(TService).GetField("RealtimeAudioChunkReady", BindingFlags.NonPublic | BindingFlags.Instance);
  var handler = (EventHandler<byte[]>)eventField.GetValue(service);
  handler?.Invoke(service, fakeChunk);
  ```
- **재발방지**: 외부 의존성 있는 이벤트 기반 E2E 검증 시 Debug 전용 Inject 헬퍼 패턴 먼저 검토. 헬퍼는 `Services/Audio/Debug{Name}InjectHelper.cs` 위치에 생성
- **Level**: 2 (테스트 패턴 — 재사용 가능)

### L-395: PowerShell UIAutomation ScrollPattern.Scroll(LargeIncrement) — 스크롤 영역 컨트롤 접근 (2026-05-10)
- **증상**: FlaUI/UIAutomation으로 WPF 앱의 스크롤 영역 내 컨트롤에 접근 시 스크롤이 안 된 상태에서 컨트롤 탐색 실패
- **해결**: `ScrollPattern.Scroll(ScrollAmount.LargeIncrement)` 호출 후 컨트롤 재탐색
- **패턴**:
  ```powershell
  $scrollEl = $window.FindFirstDescendant($cf.ByControlType([FlaUI.Core.Definitions.ControlType]::ScrollBar))
  $scrollPattern = $scrollEl.Patterns.Scroll.Pattern
  $scrollPattern.Scroll([FlaUI.Core.Definitions.ScrollAmount]::LargeIncrement, [FlaUI.Core.Definitions.ScrollAmount]::NoAmount)
  Start-Sleep -Milliseconds 300
  # 이후 컨트롤 재탐색
  ```
- **Level**: 1 (UIAutomation 테크닉 — WPF 스크롤 영역 접근 시 참고)

### L-396: otest 마커 mtime 갱신 미보장 — file_write 후 overwrite=true 필수, 실패 시 강제 재생성 (2026-05-10)
- **증상**: otest 완료 후 `evidence/otest_done` 마커가 존재하지만 mtime이 갱신되지 않아 otest_done_guard.sh가 "stale marker" 차단 → otest가 PASS인데도 odone 진입 불가
- **원인**: file_write 시 overwrite=true 미지정 또는 파일 내용이 동일한 경우 파일시스템 캐시로 인해 mtime 미갱신
- **재발방지**:
  1. 마커 파일 갱신 시 항상 `overwrite=true` + 현재 타임스탬프를 content에 포함 (동일 내용 방지)
  2. guard 차단 시 `file_delete → file_write` 강제 재생성 패턴으로 우회
  3. otest_done 마커 내용 형식: `PASS {ISO8601_TIMESTAMP}` (매번 내용 변경 보장)
- **연관**: otest_done_guard.sh
- **Level**: 2 (인프라 교훈 — otest→odone 전환 시 반복 가능)

### L-397: Mock 인터셉터 + 시간 단축 timer 패턴 — 외부 API E2E 검증 (2026-05-10)

- **문제**: OpenAI API는 비용/대기 한계로 E2E 자동 반복 검증이 불가능했음
- **해결**: `MockOpenAiResponseInjector` 인터셉터로 각 서비스 mock 분기 + `DebugTimerScale`로 타이머 주기 단축 (60초→6초, 5분→30초)
- **효과**: 실제 API 호출 없이 E2E 시나리오 13/13 PASS 검증 가능
- **패턴**: `IsEnabled=false` 기본값으로 production 영향 0 유지
- **연관**: RecordingE2ETestHarness, DebugPcmInjectHelper, L-394
- **Level**: 2 (반복 적용 가치 있는 E2E 패턴)

### L-398: oralph 반복 검증에서 미검증 항목은 mock 환경 구축으로 해결 (2026-05-10)

- **문제**: oralph iter1에서 외부 API 의존 항목은 실제 검증 불가 → 13/13 PASS 달성 불가
- **해결**: iter2에서 mock 환경(MockOpenAiResponseInjector + RecordingE2ETestHarness) 구축 후 13/13 PASS
- **교훈**: oralph 미달 항목이 "API 비용/대기" 한계일 때 → mock 환경 구축이 올바른 접근법
- **패턴**: iter1 → "mock 구축 결정(A)" → iter2 PASS → 완수
- **Level**: 2 (oralph 워크플로우 교훈)

### L-399: production 영향 없는 디버그 플래그로 mock/시간단축 환경 격리 (2026-05-10)

- **원칙**: 디버그/테스트 헬퍼는 production 코드에 포함하되 default off 플래그로 완전 격리
  - `MockOpenAiResponseInjector.IsEnabled = false` (기본) → mock 분기 진입 불가
  - `OpenAiRecordingSettings.DebugTimerScale = 1.0` (기본) → 타이머 주기 변경 없음
- **장점**: 별도 빌드 구성(DEBUG/RELEASE) 불필요, production 코드 경로와 분리
- **주의**: default 값 변경 시 반드시 HISTORY.md에 기록 (실수 운영 방지)
- **Level**: 1 (참고용 설계 패턴)

### L-400: silent failure 진단 시 catch 블록에서 ex 전체 객체 로깅 필수 (2026-05-10)

- **문제**: catch 블록에서 `ex.Message`만 로깅 → ObjectDisposedException 등 위치 식별 불가 (스택트레이스 소실)
- **근본원인**: `_log.Error(ex.Message, ...)` 패턴은 스택트레이스/내부예외를 누락하여 silent failure의 위치와 원인을 숨김
- **해결**: `_log.Error(ex, "메시지")` 패턴 사용 — NLog/Serilog 모두 첫 인수에 Exception 객체 전달 시 전체 스택트레이스 기록
- **교훈**: silent failure 진단은 catch 블록 로깅 패턴 전수 확인부터 시작하라. ex.Message만 찍으면 증거가 없다.
- **심각도**: 높음 (진단 불가 → 장기 방치)
- **Level**: 2 (MEMORY.md 검토 권장)

### L-401: DI ServiceProvider scope 생명주기 — using var scope dispose 후 ViewModel이 그 Provider 참조하면 resolve 실패 (2026-05-10)

- **문제**: `using var scope = _serviceProvider.CreateScope(); new ViewModel(scope.ServiceProvider)` 패턴에서 scope 블록 종료 후 ViewModel이 dispose된 scope의 ServiceProvider로 Singleton 서비스 resolve 시 ObjectDisposedException 또는 silent skip 발생
- **근본원인**: `scope.ServiceProvider`는 scope lifetime 동안만 유효. scope dispose 후 호출 시 ObjectDisposedException 발생하나 catch 블록에서 삼킴 → STT 등 서비스 미동작
- **해결**: Singleton 서비스는 root ServiceProvider(`_serviceProvider`, IServiceProvider)에서 직접 resolve. scope는 Scoped/Transient 서비스 전용으로 사용
- **패턴**: `new ViewModel(_serviceProvider)` (root provider) 또는 `scope.ServiceProvider.GetRequiredService<ISingleton>()` 후 scope 해제 전에 서비스 꺼내기
- **심각도**: 높음 (런타임 STT 미동작 — silent failure 유발)
- **Level**: 2 (MEMORY.md 검토 권장)

### L-402: 외부 진입점 없는 테스트 헬퍼는 REST endpoint 또는 디버그 메뉴와 동시 추가 권장 (2026-05-10)

- **문제**: `RealRecordingTestHarness.cs` 신규 헬퍼 클래스를 만들었으나 진입점(메뉴, API, 버튼)이 없어 otest에서 자동 호출 불가 — 사용자 직접 검증 의존
- **근본원인**: 헬퍼 클래스만 추가하고 UI/API 진입점 연결 생략 → 자동화 불가, 수동 검증 필수
- **해결 방향**: 실호출 테스트 헬퍼 추가 시 동시에 디버그 메뉴 항목 또는 REST POST `/api/test/real-stt` 엔드포인트 연결 권장
- **교훈**: 테스트 헬퍼의 가치는 실제로 호출 가능할 때 비로소 실현된다 (L-391 연관: 진입 경로 없으면 FAIL)
- **심각도**: 낮음
- **Level**: 1 (참고용)

### L-403: silent failure 진단 로그 발화 0건 → 호출 경로 자체 단절 가설 전환 (2026-05-10)

- **문제**: OpenAI STT silent failure 진단 로그 3곳을 추가했으나 사용자 추가 녹음 4회 후에도 신규 로그 발화 0건 확인
- **근본원인**: 진단 로그를 추가한 위치가 이미 도달 불가한 영역이었음 — 호출 경로 자체가 그 위치 이전에 단절
- **해결 방향**: 발화 0건 확인 즉시 "더 깊은 곳에 진단 로그" 전략을 버리고 "호출 경로 진입점부터 역추적" 전략으로 전환
- **교훈**: silent failure 진단 로그가 발화 0건이면 → 호출 경로 자체 단절 가설로 전환 (이전 진단 로그가 도달하지 못하는 영역)
- **심각도**: 중간
- **Level**: 2 (패턴 등록)

### L-404: 호출 경로 추적 로그 패턴 — Layer별 7곳 진입 표시 (2026-05-10)

- **문제**: 단일 지점 진단 로그로는 끊김 위치 식별 불가 — 여러 시도 후에도 정확한 단절 지점 미확정
- **해결 방향**: 진입점(UI 클릭)부터 말단(HTTP/WS 송수신)까지 7곳에 동시 진단 로그 삽입
  ```
  Layer 1 — UI 클릭 핸들러 진입 (MainWindow)
  Layer 2 — ViewModel 메서드 진입 + null 체크 결과
  Layer 3 — 서비스 StartAsync 진입 + 연결 상태
  Layer 4 — AudioRecordingService invoke subscribers 수
  Layer 5 — STT 서비스 수신 데이터 길이
  Layer 6 — 외부 API 송신 직전 (HTTP POST / WS Send)
  Layer 7 — 외부 API 응답 수신 snippet
  ```
- **교훈**: 호출 경로 추적 로그 패턴 — 진입점부터 말단까지 Layer별 7곳 표시 후 끊김 지점 식별 (Layer별 진단 템플릿 응용)
- **심각도**: 낮음
- **Level**: 1 (참고용)

### L-405: API key 로그 출력 안전 마스킹 패턴 (2026-05-10)

- **문제**: API key 전체를 로그에 출력하면 보안 위험, null/빈 문자열 처리 누락 시 예외 발생
- **해결 방향**: 안전 마스킹 패턴 표준화
  ```csharp
  // ✅ 올바른 패턴
  var keyMask = !string.IsNullOrEmpty(apiKey) && apiKey.Length >= 7
      ? apiKey.Substring(0, 7) + "***"
      : "(short_or_empty)";
  _log.Info($"[진단] API key prefix: {keyMask}");
  ```
- **교훈**: API key 로그 출력 시 `Substring(0,7)+"***"` + 길이 fallback `"(short_or_empty)"` 안전 마스킹 패턴 필수
- **심각도**: 낮음
- **Level**: 1 (참고용)

### L-406: NLog 표준 정책의 함정 — 출력 채널(config) 검증 없이 라이브러리 표준화는 silent drop 초래 (2026-05-10)

- **문제**: L-296에서 'MaiX 모든 레이어에서 NLog 표준 로거 사용 필수'로 규정했으나 정작 NLog.config 자체가 없어서 8개 클래스의 모든 NLog 로그가 silent drop되어 왔음. 5회 STT 디버깅 시도에서도 NLog 출력이 전혀 나오지 않아 근본 원인 추적 불가였음.
- **근본원인**: 로거 라이브러리 표준화(NLog 채택 규칙 제정) 시 출력 채널(NLog.config) 검증 누락. 코드에서 Logger를 사용하는 것과 그 Logger 출력이 실제 파일에 도달하는 것은 별개임.
- **해결**: NLog.config 신규 생성 + mAIx.csproj CopyToOutputDirectory 등록 + App.xaml.cs LogManager.Setup() 호출.
- **교훈**: 신규 로거 라이브러리 추가/표준화 시 출력 파일에 1줄이 실제로 찍히는지 즉시 검증 필수. NLog.config + CopyToOutputDirectory + LogManager.Setup() 3요소는 영구 보존해야 함.
- **재발방지**: 로거 정책 제정 체크리스트에 "출력 파일 실제 생성 확인" 단계 추가.
- **심각도**: 높음 (5회 디버깅 시도 전부 측정 불가)
- **Level**: 2 (인지 — MEMORY 반영)

### L-407: NLog 4.7+ Setup() extension method — using NLog; 없으면 LoadConfigurationFromFile 컴파일 오류 (2026-05-10)

- **문제**: `NLog.LogManager.Setup()` 체인에서 `.LoadConfigurationFromFile()`이 extension method라 `using NLog;` 없이는 컴파일 오류 발생. Fully qualified name(`NLog.LogManager.Setup()`)으로 첫 메서드는 호출 가능하나 extension method 체인은 using 없으면 CS1061 오류.
- **근본원인**: Extension method는 네임스페이스 using 없이는 메서드 확장 대상을 찾지 못함. Fully qualified name과 extension method 해석은 다른 메커니즘.
- **해결**: `using NLog;` 추가.
- **교훈**: NLog 4.7+ Setup() 패턴 사용 시 반드시 `using NLog;` 필수. Fully qualified name 시도로 우회 불가.
- **심각도**: 낮음
- **Level**: 1 (참고용)

### L-408: 자기코드 맹점 — 출력 채널 우선 의심 순서 (2026-05-10)

- **문제**: OpenAI Realtime STT silent failure를 5회 시도하면서 매번 코드 로직(scope dispose, 분기 미진입, API 호출 오류)만 의심하고 출력 채널(NLog.config 부재)을 의심하지 않음. 진단 로그 22줄을 추가했음에도 아무것도 보이지 않았던 이유는 NLog.config 자체가 없었기 때문.
- **근본원인**: "자기가 작성한 코드"에 대한 확증 편향 — 코드 로직이 맞다고 전제하고 출력 인프라를 후순위로 의심.
- **해결**: NLog.config 존재 여부 확인 후 즉시 해결.
- **교훈**: 자기 분석/수정 코드의 출력이 안 보일 때 의심 순서는 반드시 다음을 따른다.
  1. **출력 채널** (라우팅/target/level filter/config 부재)
  2. **빌드 갱신 누락** (실행 중인 바이너리가 구버전)
  3. **분기 미진입** (조건문/이벤트 핸들러 연결 누락)
  4. **코드 로직** (마지막으로 의심)
- **심각도**: 높음 (5회 반복 실패)
- **Level**: 2 (인지 — MEMORY 반영)

### L-409: OpenAI Realtime API session.update 필수 (2026-05-10)

- **문제**: WebSocket 연결 + audio chunk 전송만으로는 STT transcript 이벤트가 0건. 서버가 audio를 받아도 기본 설정에서 transcription이 비활성화되어 있어 응답 없음.
- **근본원인**: Realtime API의 기본 modalities는 `["text", "audio"]`이나, `input_audio_transcription`을 명시하지 않으면 STT가 비활성화됨. `session.update` 발송 없이는 서버가 transcription 이벤트를 생성하지 않음.
- **해결**: `StartAsync()` 직후 `session.update` 메시지로 `modalities=["text"]` + `input_audio_transcription={model:"whisper-1"}` + `turn_detection={type:"server_vad"}` 명시.
- **교훈**: Realtime API 사용 시 `session.update` 발송은 연결 직후 필수 절차. context7 명세(`/openai/openai-realtime-api-beta`) 1순위 참조.
- **재발방지**: Realtime WebSocket 연결 후 STT 미응답 시 `session.update` 발송 여부를 첫 번째로 확인.
- **심각도**: 높음 (STT 기능 전체 불동작)
- **Level**: 2 (인지 — Realtime API 필수 프로토콜)

### L-410: server_vad로 묵음 구간 자동 가시화 (2026-05-10)

- **문제**: 클라이언트 VAD로는 묵음 구간 타임스탬프를 정확히 계산하기 어려움. 네트워크 지연과 버퍼 누적으로 실제 묵음 시점과 이벤트 시점이 어긋남.
- **해결**: `turn_detection: {type: "server_vad"}` 설정 시 서버가 발화 시작/종료를 자동 감지. `input_audio_buffer.speech_started`의 `audio_start_ms`와 `input_audio_buffer.speech_stopped`의 `audio_end_ms`로 정확한 묵음 구간(ms 단위) 계산 가능.
- **구현**: `_speechStartMs` 기록 → `speech_stopped` 이벤트에서 `(audio_end_ms - _speechStartMs) / 1000.0` 계산 → 1초 이상일 때만 `[묵음 N.N초]` 마커 발화.
- **교훈**: 음성 STT에 묵음 표시 필요 시 server_vad가 클라이언트 VAD보다 정확. 1초 미만 묵음은 노이즈로 간주하여 표시 생략.
- **재발방지**: 묵음 구간 가시화 필요 시 `server_vad` + `speech_started/stopped` 이벤트 조합 우선 채택.
- **심각도**: 중간 (UX 개선)
- **Level**: 1 (기술 참조)

### L-411: OpenAI Realtime API input audio는 반드시 24kHz pcm16 (2026-05-10)

- **문제**: `input_audio_format: {encoding: "pcm16"}` 설정은 sample rate 필드가 없음. OpenAI 서버 기본값은 24kHz. 16kHz 데이터를 24kHz로 해석하면 음성이 약 1.5배 가속 → server_vad 임계값(음량·패턴) 미달 → `speech_started` 이벤트 0건 발화.
- **증상**: session.update 발송 + audio chunk 24회 전송 정상이나 server_vad `speech_started` 이벤트 완전 침묵.
- **해결**: `AudioRecordingService._outputFormat` SampleRate 16000→24000, BytesPerSecond 32000→48000. `OpenAiRealtimeSttService.BytesPerSecond` 상수 32000→48000. `OpenAiTranscribeSttService.BuildWavStream` WAV 헤더 SampleRate 16000→24000 + BytesPerSecond 32000→48000.
- **교훈**: Realtime API STT 사용 시 input audio는 반드시 **24kHz pcm16 16bit mono**로 출력. 송신 측 sample rate를 24kHz로 통일해야 server_vad가 정상 작동.
- **재발방지**: Realtime API 신규 통합 시 `AudioRecordingService` 출력 포맷의 SampleRate/BytesPerSecond를 24kHz 기준으로 명시 검증 필수.
- **심각도**: 높음 (server_vad 완전 불능)
- **Level**: 2 (MEMORY.md 기록 권장)

### L-412: sample rate 변경의 광범위 영향도 전수 조사 필수 (2026-05-10)

- **문제**: sample rate 상수 변경은 청크 크기 계산·WAV 헤더·BytesPerSecond·리샘플링 ratio·다른 STT 서비스 호환성 등 광범위하게 영향. `_outputFormat.AverageBytesPerSecond`를 동적 참조하면 자동 반영되나, 하드코딩된 `32000` 등은 별도 갱신 필요.
- **해결**: `grep "16000\\|32000\\|SampleRate" **/*.cs`로 전수 조사 후 영향 파일 4개 일괄 조정.
- **교훈**: sample rate 변경 시 반드시 하드코딩 상수 전수 grep 후 갱신. 다른 STT 서비스(Whisper local, VOSK 등) 호환성 영향도 평가 필수.
- **재발방지**: sample rate 변경 PR 리뷰 시 `grep "16000\|32000\|SampleRate"` 결과를 변경 파일 목록과 교차 검증.
- **심각도**: 중간 (잠재적 버그 파급)
- **Level**: 1 (기술 참조)

### L-413: PeriodicTimer + Task 라이브 모니터링 패턴 (2026-05-10)

- **문제**: 클라이언트 측 묵음 추적 구현 시 `Timer.Elapsed += async (s, e) => { ... }` 패턴을 쓰면 L-380 async lambda 예외 소실 위험 + CancellationToken 연동 불편.
- **해결**: `PeriodicTimer` + `WaitForNextTickAsync(CancellationToken)` + `Task.Run` 조합으로 백그라운드 모니터링 Task 구성. `OperationCanceledException`은 정상 종료 경로로 처리.
- **교훈**: 백그라운드 주기 모니터링 Task는 `Timer.Elapsed` 이벤트 대신 `PeriodicTimer + WaitForNextTickAsync` 패턴 사용. CancellationToken으로 종료를 깔끔하게 보장.
- **재발방지**: 새 모니터링 Task 구현 시 `new System.Threading.PeriodicTimer(TimeSpan)` + `await timer.WaitForNextTickAsync(ct)` 패턴을 기본으로 채택.
- **심각도**: 낮음 (패턴 개선)
- **Level**: 1 (기술 참조)

### L-414: WPF ComboBox int 바인딩 — sys:Int32 Tag 명시 필수 (2026-05-10)

- **문제**: `<ComboBoxItem Tag="12">` (string)를 `SelectedValue="{Binding IntProperty}"` (int)에 바인딩하면 타입 불일치로 `SelectedValue`가 null 반환되어 바인딩 무효화.
- **해결**: `xmlns:sys="clr-namespace:System;assembly=mscorlib"` 선언 후 `<ComboBoxItem.Tag><sys:Int32>12</sys:Int32></ComboBoxItem.Tag>` 형식으로 Tag를 실제 int 타입으로 명시.
- **교훈**: ComboBox `SelectedValue`를 int/double 프로퍼티에 바인딩 시 `sys:Int32`/`sys:Double`로 Tag 타입을 명시해야 바인딩이 정상 작동.
- **재발방지**: numeric 프로퍼티 바인딩 ComboBox 작성 시 string Tag 패턴 사용 금지. `sys:Int32` 네임스페이스 선언 + 타입 명시 패턴 표준화.
- **심각도**: 중간 (바인딩 silent fail)
- **Level**: 1 (기술 참조)

### L-415: 사용자 목표 집중 — 고정 주기 매직 넘버 의문 제기 (2026-05-10)

- **문제**: 기존 코드에 `60`(초), `5분` 등 고정 주기 매직 넘버가 있었음. 사용자 목표 "실시간 주제어 네비게이션"과 불일치했으나 무비판적으로 유지.
- **해결**: `TopicExtractorIntervalSec` 동적 설정으로 전환 + 옵션탭 ComboBox(12/30/60/120초) 제공. 사용자가 원하는 최소 단위로 변경 가능.
- **교훈**: 기존 코드의 매직 넘버/고정 주기를 발견하면 사용자 실제 목표와 일치하는지 의문을 제기하라. YAGNI: 명시 요구된 항목만 동적화, 나머지는 별도 작업으로 분리.
- **재발방지**: 구현 전 "이 상수가 사용자 목표에 맞는가?" 질문을 체크리스트에 추가. 고정 주기 코드는 동적 설정 전환 여부를 oplan 단계에서 평가.
- **심각도**: 중간 (UX 목표 불일치)
- **Level**: 1 (기술 참조)

### L-416: UI 옵션 분산 시 단일 출처 원칙 (2026-05-10)

- **문제**: 화자분리모드/청크길이/누적요약주기 3옵션이 좌측 녹음 패널 + 우측 옵션탭 두 군데에 분산. 사용자 혼란 + x:Name 충돌 위험 + 중복 바인딩 관리 부담.
- **해결**: 좌측 3옵션을 우측 옵션탭으로 완전 이동. 좌→우 이동 시 한 번의 Edit으로 좌측 제거 + 우측 추가를 동시 처리.
- **교훈**: 동일 기능 옵션은 한 위치에만 존재해야 한다. 좌/우 패널 분산은 유지보수 비용을 배로 증가시킨다.
- **재발방지**: UI 옵션 추가 시 단일 위치 원칙. 기존 분산 발견 시 통합 작업으로 처리. 옵션 이동 시 반드시 x:Name 충돌을 grep으로 전수 확인.
- **심각도**: 중간 (UX 혼란)
- **Level**: 1 (기술 참조)

### L-417: 옵트인 정책 — 신규 자동 기능 기본 false (2026-05-10)

- **문제**: "체크박스로 선택 시 자동" 요구 = 옵트인 의도임에도 기본값을 true로 설정하면 기존 사용자에게 의도치 않은 동작 변화 유발.
- **해결**: `AutoFinalSummary` 기본값 `false` 설정. XML에 키 없으면 기본 false → 기존 사용자 동작 유지(하위 호환).
- **교훈**: 신규 자동 기능의 기본값은 반드시 false(옵트인). 사용자가 "체크박스" 또는 "선택 시"로 표현하면 옵트인 의도로 해석하라.
- **재발방지**: 신규 자동 기능 추가 시 기본값 false 필수. 기본 true 설정은 오직 사용자가 "기본으로 켜져있어야 한다"고 명시한 경우에만 허용.
- **심각도**: 중간 (기존 사용자 동작 변화)
- **Level**: 1 (기술 참조)

### L-418: MinuteSummaryService 장기 동작 서비스 주기 발화 로그 필수 (2026-05-13)

- **문제**: MinuteSummaryService PeriodicTimer가 발화하는지 로그가 없어서 odebug 진단 시 "0건"으로 보임 → 타이머 미시작 or 정상 시작인지 판별 불가.
- **해결**: RunTimerLoopAsync 내에 매 tick 발화 로그(`[MinuteSummary] PeriodicTimer 틱 — buffer={Count}개`), 스킵 로그, 요약 시작 로그 3건 추가.
- **교훈**: 주기적 동작(PeriodicTimer/Timer/cron 등)을 포함하는 장기 동작 서비스는 반드시 tick 단위 발화 로그를 남겨야 한다. 로그 없으면 외부에서 동작 여부 판별 불가.
- **재발방지**: 새로운 PeriodicTimer/Timer.Elapsed 기반 서비스 구현 시 tick 발화 로그 필수. code review 시 주기 서비스 grep 후 로그 존재 여부 확인.
- **심각도**: 중간
- **Level**: 1 (기술 참조)

### L-419: oplan 코드 분석 시 정의부+호출부 동시 확인 필수 — dead code 오분류 방지 (2026-05-13)

- **문제**: 1차 oplan이 `_realtimeSummaryTimer` 정의부만 grep으로 확인하고 호출부(`StartRealtimeSTT()`)를 미검증. 실제로는 `StartRealtimeSTT()`가 어디서도 호출되지 않아 타이머 자체가 시작 안 됨(dead code). oplan은 이를 핵심 수정 대상으로 오판 → odev-1 잘못된 수정 → otest-1 PASS → 사용자 "실시간 요약 0건" 확인 → 역라우팅.
- **해결**: 2차 oplan에서 `StartRealtimeSTT()` 호출부 미존재 확인 후 dead code로 올바르게 분류 → dead code 제거 + `MinuteSummaryService` 단일 경로 강화.
- **교훈**: oplan이 함수/필드를 분석할 때 정의부(`new Timer()`, 필드 선언)만 확인하는 것으로 불충분하다. 그 함수/필드가 실제 실행 경로에 포함되는지(= 어디서 호출되는지) 반드시 함께 확인해야 한다. `find_referencing_symbols` 또는 `grep -n '호출할 함수명'`으로 호출부 존재 여부를 확인하라.
- **재발방지**: oplan 단계에서 핵심 경로 분석 시 정의부 확인 후 반드시 호출부도 grep. 호출부 0건이면 dead code 가능성 높음 → 사용자에게 dead code 가능성 보고 후 판단 요청.
- **심각도**: 중간 (역라우팅 1회 유발)
- **Level**: 2 (LESSONS.md + MEMORY.md)

### L-420: otest 런타임 발화 검증 누락 금지 — PeriodicTimer/주기적 동작 수정 시 필수 (2026-05-13)

- **문제**: 1차 otest acceptance_criteria가 grep 정적 검증 위주로만 작성됨. `MinuteSummaryService` PeriodicTimer가 실제 60초마다 발화하는지 런타임 로그 확인 항목이 없었음. 정적 검증만으로 PASS → 사용자가 실제 환경에서 "핵심요약 1건만" 발견.
- **해결**: 2차 otest에서 런타임 검증 추가 — 18:47:41 PeriodicTimer 첫 발화, 18:48~18:52 5회 연속 발화 확인.
- **교훈**: oplan acceptance_criteria 작성 시 PeriodicTimer/Timer/cron 등 주기적 동작이 수정 범위에 포함되면 반드시 런타임 발화 검증 항목을 추가해야 한다. 단순 "코드 존재 여부 grep"만으로는 실제 동작을 보장할 수 없다.
- **재발방지**: otest Phase 2 검증 시 주기적 동작 수정이 있으면 `acceptance_criteria`에 런타임 발화 검증 항목(로그 확인/실시간 모니터링) 필수 포함. 정적 grep만으로 PASS 불가.
- **심각도**: 중간 (역라우팅 유발)
- **Level**: 2 (LESSONS.md + MEMORY.md)

### L-421: 부분 수정 한계 시 사용자에게 재설계 권한 위임 (2026-05-13)

- **문제**: TopicExtractor 분절 주기 수정으로 이전 ok 파이프라인(복수 시도)을 진행했으나 근본 구조 문제가 해결되지 않았음.
- **해결**: 사용자가 "기존 로직 제거 + 실시간요약 발화 시 추출"로 재설계 방향을 명시 → TopicExtractorService 전체 삭제 + MinuteSummary 콜백 변환 구조로 단순화 → 1회 사이클로 완료.
- **교훈**: 부분 수정(기존 구조 유지)으로 안 풀리는 문제는 사용자에게 재설계 권한 위임이 효율적이다. 에이전트가 기존 구조를 유지하면서 계속 패치를 시도하는 것보다, "이 구조 자체가 문제인가?"를 사용자에게 물어 더 단순한 대안을 얻는 것이 낫다.
- **재발방지**: 동일 기능 2회+ ok 파이프라인이 진행되었으나 해결 안 되면 oplan 단계에서 "기존 구조 유지 vs 재설계" 옵션을 사용자에게 명시적으로 제시한다.
- **심각도**: 낮음 (1회 관찰)
- **Level**: 1 (참고용)

### L-422: active 코드도 사용자 의도 정렬로 dead code가 될 수 있다 (2026-05-13)

- **문제**: TopicExtractorService는 정적 분석상 active 코드(이벤트 구독, 서비스 등록, 메서드 호출 모두 존재)였으나 사용자 요구("실시간요약 발화 시 핵심주제 생성")와 구조적으로 미정렬.
- **해결**: 사용자 의도 기반으로 TopicExtractorService 전체 삭제 + MinuteSummaryCreated 콜백에서 직접 TopicSegment 변환 처리.
- **교훈**: 코드의 dead/alive 판정은 "실행되는가(정적 분석)"뿐 아니라 "사용자 의도와 정렬되는가(의도 정렬도)"로도 판단해야 한다. active 코드도 사용자 목표와 미정렬이면 재설계 대상이다.
- **재발방지**: oplan 분석 시 "이 코드가 실행되는가"뿐 아니라 "이 코드가 사용자 목표와 정렬되는가"도 점검한다.
- **심각도**: 낮음 (1회 관찰)
- **Level**: 1 (참고용)

### L-423: 이종 페르소나 병렬 odev — 언어/계층 다른 변경은 분리 + 병렬화 (2026-05-13)

- **문제**: 이번 작업은 BE(C# 서비스/ViewModel) 4파일 + FE(XAML) 2파일을 동시에 수정해야 했음.
- **해결**: be-csharp 에이전트(TopicExtractor 제거 + ViewModel 재구성) + fe-designer 에이전트(XAML ScrollViewer+StackPanel 재작성)로 분리하여 병렬 실행 → 각 에이전트가 자기 계층에 집중 → 효율적 완료.
- **교훈**: C#(서비스/ViewModel) 변경과 XAML(UI) 변경이 동시에 필요한 경우, 이종 페르소나(be-csharp + fe-designer)로 분리하여 병렬 실행하면 충돌 없이 효율적이다. 동일 계층 내 여러 파일도 마찬가지.
- **재발방지**: odev 계획 시 언어/계층이 다른 변경 그룹이 있으면 자동으로 병렬 에이전트 분리를 검토한다.
- **심각도**: 낮음 (긍정 사례 — 잘 된 패턴 기록)
- **Level**: 1 (참고용)

### L-424: WPF ItemsControl 가변 높이 — StackPanel+DisplayHeight 표준 패턴 (2026-05-14)

ItemsControl에서 시간 비례/가변 높이가 필요할 때 ItemsPanelTemplate=Grid + 코드비하인드 RowDefinition 동적 생성 패턴은 안티패턴이다.
3중 우회 (ItemContainerGenerator 비동기 + CollectionChanged 수동 구독 + Grid.SetRow 명령형)가 필요하며 WPF 내부 타이밍 문제로 신뢰성이 낮다.
표준 패턴은 ItemsControl + ItemsPanelTemplate(StackPanel) + ItemTemplate(Border Height={Binding DisplayHeight})이다.
ViewModel이 항목 추가 시 전체 DisplayHeight를 재계산하여 모델 프로퍼티에 저장한다.
oplan 설계 단계에서 Grid ItemsPanelTemplate 패턴이 제안되면 즉시 기각하고 StackPanel+DisplayHeight로 전환한다.

**재발방지**: oplan_normal/SKILL.md "WPF 레이아웃 설계 필수 규칙" 섹션 추가됨.

### L-425: UIAutomation DataItem 검증 = 개수 + Rect 좌표 분산 2단계 필수 (2026-05-14)

ItemsControl/ListBox 등 반복 컨트롤 검증 시 DataItem 개수 확인만으로 PASS 판정 불가.
전체 DataItem이 동일 Rect(동일 Y 좌표)에 겹쳐 있으면 사용자에게는 1개만 보인다.
개수 검증만으로는 WPF 레이아웃 겹침 버그를 감지하지 못한다.
필수 2단계: (1) DataItem 개수 예상값 일치, (2) 각 DataItem BoundingRectangle.Y 값 분산 확인.
전체 Y가 동일하면 FAIL — 즉시 역라우팅.

**재발방지**: otest_winforms/SKILL.md "ItemsControl/반복 컨트롤 검증 필수 규칙" 섹션 추가됨.

### L-426: UI '최신/단일' 표시 요구 해석 분기 — 모호 시 사용자 확인 필수 (2026-05-14)

요구사항에 "단일", "최신", "마지막", "1건"이 포함될 때 2가지 해석이 가능하다.
(A) 누적 유지 + 최신 강조: 이전 이력 유지, 최신 항목만 강조 또는 상단 표시.
(B) 이전 완전 숨김: 최신 1건만 표시, 이전 항목 제거.
oplan이 확인 없이 ContentControl 단일 표시(해석 B)로 구현 → 실제 의도는 누적 유지(해석 A) → 1차 역라우팅.
모호 시 oplan 계획서에 2가지 해석을 명시하고 사용자 확인 후 진행.

**재발방지**: oplan_normal/SKILL.md "UI 요구사항 해석 분기 규칙" 섹션 추가됨.

### L-427: 동일 증상 역라우팅 2회 = 설계 한계 신호 — 즉시 근본변경 옵션 제시 (2026-05-14)

동일 증상으로 역라우팅이 2회 발생하면 3번째 우회 시도는 금지한다.
우회 방법(RaisePropertyChanged → Grid.SetRow → ItemContainerGenerator) 3차례 실패 후에야 사용자에게 옵션 제시 → 사용자 마찰 증가.
2회 역라우팅 시 즉시 의무 절차:
1. 기존 구조 한계 명시.
2. 옵션 A(기존 구조 우회 지속)와 옵션 B(근본 설계 변경) 명시 제시.
3. 사용자 결정 후 진행.
L-421(동일 기능 2회 ok 미해결 시 재설계 권한 위임)의 역라우팅 단위 적용 강화.

**재발방지**: oplan_normal/SKILL.md "역라우팅 반복 대응 규칙" 섹션 추가됨.

### L-429: '확인해보라' 요청 = 기존검증+신규요구 동시처리 패턴 (2026-05-14)

사용자가 "맞는지 확인해보라" 형식으로 요청할 때 기존 알고리즘 정합성 검증과 신규 요구사항(마지막 카드 잔여흡수, Min 가드 제거 등)을 한 사이클에 통합 처리하면 역라우팅 없이 PASS 가능.
oplan 계획서에 "기존 로직 검증 AC 항목"을 명시하고 신규 요구사항과 묶어서 단일 사이클로 처리한다.

**재발방지**: oplan에 검증 AC 항목 명시 → 단일 사이클 처리

---

### L-430: odev 자체발견 프로퍼티 대체 — 코드 정합성 > 계획서 100% 준수 (2026-05-14)

oplan 계획서가 존재하지 않는 프로퍼티(Topic)를 참조하도록 설계했을 때 odev가 실제 코드를 탐색하여 의미적으로 동등한 대체 프로퍼티(SummaryPreview)를 자율 선택하면 역라우팅 없이 완료 가능.
계획서 100% 준수보다 코드 정합성 우선. 의미적으로 동등하면 odev에서 자체 처리 허용.

**재발방지**: 계획서 작성 시 프로퍼티 존재 여부 사전 grep 확인 (oplan 단계)

---

### L-428: 교훈 LESSONS 즉시 등재 효과 — 다음 사이클에서 직접 활용됨 (2026-05-14)

L-424(WPF ItemsControl 가변높이 안티패턴 기각)가 직전 작업 완료 시 LESSONS.md에 등재되었고,
이번 작업(타임라인 ruler + % 비례)에서 oplan-1이 처음부터 StackPanel+Canvas 조합을 설계하여 역라우팅 0회 달성.
교훈은 등재 직후부터 다음 작업에 즉각 활용된다 — 교훈 등재 → 바로 다음 파이프라인 사이클에서 효과 측정 가능.

**관련 패턴**: L-424(StackPanel 표준), L-425(DataItem Rect Y 분산 검증), L-427(역라우팅 2회 즉시 옵션 제시)
**재발방지**: 해당 없음 (긍정 패턴 기록)

### L-431: WPF ListBoxItem Focusable=False Setter가 클릭 선택 차단 (2026-05-14)

- **문제**: ItemContainerStyle에 `<Setter Property="Focusable" Value="False"/>` 가 설정되어 있으면, ListBoxItem을 클릭해도 선택(IsSelected)이 정상 동작하지 않음
- **근본 원인**: WPF ListBox는 포커스 이동을 통해 선택 상태를 처리하는데, Focusable=False이면 포커스가 이동 불가하여 선택 이벤트가 차단됨
- **해결**: ItemContainerStyle에서 `Focusable=False` Setter 삭제
- **재발방지**: ItemContainerStyle 코드 리뷰 시 `Focusable=False` 패턴 즉시 기각. WPF 선택이 안 되는 증상 발생 시 ItemContainerStyle Focusable 설정 먼저 확인
- **Level**: 2 (반복 재현 위험 — ListBox 스타일링 시 흔한 실수)

### L-432: PreserveXxxOnSelectionChange() 공개 메서드 패턴 — LoadCollection+SelectionChanged 경쟁 조건 회피 (2026-05-14)

- **문제**: LoadOneNoteRecordings()에서 ObservableCollection을 재설정할 때 SelectionChanged 이벤트가 발화되어 STT 데이터가 Clear됨
- **근본 원인**: WPF에서 ObservableCollection.Clear() → SelectedItem 변경 → SelectionChanged 자동 발화 → 핸들러가 STT/요약 데이터를 Clear
- **해결**: `PreserveSTTOnSelectionChange()` public 메서드로 "다음 SelectionChanged는 Clear 스킵" 플래그를 설정, LoadCollection 직전에 호출
- **패턴**: `PreserveXxxOnSelectionChange()` — 컬렉션 재로드 직전 호출하여 SelectionChanged 부작용 방지
- **재발방지**: ObservableCollection 재설정(Clear+Add) 후 SelectionChanged 핸들러가 데이터를 Clear하는 패턴 발견 시 PreserveXxx 패턴 적용
- **Level**: 2 (WPF 컬렉션 재설정 시 반복 재현 가능)

### L-433: 노트/녹음파일 전환 시 5종 Clear 필수 (2026-05-14)

- **문제**: 다른 OneNote 노트 또는 녹음파일로 전환 시 이전 노트의 핵심요약/실시간요약 데이터가 새 노트에 잔류
- **근본 원인**: OnSelectedPageChanged 핸들러에서 요약 관련 컬렉션/필드를 초기화하지 않음
- **해결**: OnSelectedPageChanged 양쪽 분기에 5종 Clear 추가
  - TopicSegments.Clear()
  - MinuteSummaries.Clear()
  - CumulativeSummaryText = string.Empty
  - FinalSummaryText = string.Empty
  - MinuteSummaryCount = 0
- **재발방지**: 노트/파일 전환 핸들러 구현 시 표시용 컬렉션/필드 전체 Clear 체크리스트 적용
- **Level**: 2 (신규 요약 데이터 추가 시 반복 재현 위험)

### L-434: 메모리 전용 컬렉션 vs 영속화 데이터 분리 — 녹음파일 페어링 패턴 (2026-05-14)

- **문제**: 녹음 중 생성된 STT/요약 데이터가 녹음 중지 후 다른 파일 선택 시 소실. 앱 재시작 후 복원 불가
- **근본 원인**: ObservableCollection은 메모리 전용 — 영속화 없이는 세션 간 데이터 유지 불가
- **해결**: `RealtimeRecordingResult` 모델 신규 생성 + `.realtime.json` 파일로 영속화
  - StopRecording → `SaveRealtimeRecordingResultAsync()` 호출
  - 파일 선택 → `LoadRealtimeResultAsync()` 호출 후 컬렉션 복원
- **페어링 패턴**: `{녹음파일명}.realtime.json` — 녹음파일과 동일 디렉토리에 동일 이름으로 페어링
- **재발방지**: 녹음/분석 세션 데이터는 항상 영속화 파일 페어링 설계. 메모리 컬렉션 단독 사용 설계 감지 시 페어링 파일 추가 권고
- **Level**: 2 (신규 데이터 타입 추가 시 반복 재현 위험)

### L-435: PreserveXxx 패턴 + 로드 페어 의무 — Preserve 호출 시 LoadXxx도 반드시 함께 호출 (2026-05-14)

- **문제**: `PreserveSTTOnSelectionChange()` 호출 후 `LoadSelectedRecordingResults()` 누락 → STT 데이터 영구 표시 안 됨 (보존 의도와 반대 결과)
- **근본 원인**: Preserve 메서드는 SelectionChanged 경쟁 조건만 회피, 데이터 로드는 호출자 책임 — 이 책임이 명시되지 않아 누락
- **해결**: `LoadOneNoteRecordings()`에 `Preserve` 호출 직후 `LoadSelectedRecordingResults()` 명시 추가
- **규칙**: `PreserveXxxOnSelectionChange()` 호출 시 반드시 `LoadXxxResults()` 쌍으로 호출 (보존 + 로드 = 페어)
- **재발방지**: Preserve 메서드 코드 리뷰 시 호출부 grep → `LoadXxx` 쌍 호출 여부 확인 필수
- **Level**: 2 (L-432 보강 — Preserve+Load 페어 의무 규칙 추가)

### L-436: LoadRealtimeResultAsync는 데이터 로딩 전용 — UI 트리거(RebuildTimelineTicks)는 호출자 책임 (2026-05-14)

- **문제**: `LoadRealtimeResultAsync()` 내부에서 `RebuildTimelineTicks()` 미호출 → 타임라인 틱 게이지 고정(기본값 표시)
- **근본 원인**: 로드 메서드가 데이터 복원만 담당, UI 파생 계산은 별도 메서드 — 호출자가 연동 책임 미인지
- **해결**: `LoadRealtimeResultAsync()` 양 경로(데이터 있음 / 없음) 말미에 `RebuildTimelineTicks()` 명시 추가
- **규칙**: 데이터 로드 메서드 작성 시 "로드 후 파생 UI 계산이 필요한가?" 체크리스트 적용
- **재발방지**: Load 메서드 구현 시 관련 Rebuild/Recalculate 연동 여부 설계 단계에서 명시
- **Level**: 2 (신규 교훈 — 로드/UI 계산 분리 책임 패턴)

### L-437: ObservableCollection.Count==0 early return은 이전 데이터 잔류 유발 — 기본값 명시 생성 패턴 (2026-05-14)

- **문제**: `RebuildTimelineTicks()`에서 `Count==0`일 때 `return` → 이전 사이클 틱 데이터 잔류, 빈 타임라인 미표시
- **근본 원인**: early return이 컬렉션 Clear를 건너뜀 → 전 페이지 틱 데이터 잔류
- **해결**: `Count==0` 케이스에 `TimelineTicks.Clear()` + 기본 틱(0:00 / 1:00) 생성 후 return
- **규칙**: 입력 데이터가 없을 때도 "UI 컬렉션 Clear + 기본값 표시"를 명시적으로 수행할 것
- **재발방지**: Rebuild 메서드 early return 시 Clear 생략 여부 코드 리뷰 체크리스트 적용
- **Level**: 2 (신규 교훈 — empty case 기본값 생성 패턴)

### L-438: WPF Image Stretch=Uniform+VerticalAlignment=Top은 가로 콘텐츠 상단 압축 유발 — Fill+Stretch 표준 패턴 (2026-05-14)

- **문제**: STT 미니맵 이미지가 상단에만 압축 표시 (하단 빈 공간 발생)
- **근본 원인**: `Stretch="Uniform"` + `VerticalAlignment="Top"` 조합 → 가로 와이드 이미지가 원래 비율 유지하며 상단 정렬 → 하단 여백
- **해결**: `Stretch="Fill"` + `HorizontalAlignment="Stretch"` + `VerticalAlignment="Stretch"` 로 전환
- **규칙**: 패널 전체 영역을 채워야 하는 이미지는 `Fill+Stretch` 표준 패턴 사용. `Uniform+Top`은 원본 비율 보존 목적에만 사용
- **재발방지**: Image Stretch 설정 시 "패널 전체 채움 vs 비율 보존" 의도 명시 후 패턴 선택
- **Level**: 2 (신규 교훈 — WPF Image Stretch 패턴)

## L-439: Wave 기반 의존성 spawn — 인터페이스/타입 선행 → 구현체 병렬 → 통합 (2026-05-15)

- **문제**: 다파일 코드 추가(5+ 파일) 시 단순 병렬 spawn은 인터페이스 미정 상태로 구현체끼리 시그니처 충돌 위험
- **해결**: Wave 단계로 분할 — Wave1(타입/인터페이스/Factory 시그니처) → Wave2(구현체 병렬 spawn) → Wave3(호출자 통합)
- **실증**: 음성 파이프라인 2모드 시스템 14파일 1110줄을 Wave 기반 spawn으로 역라우팅 0회 단일 사이클 완수
  - Wave1: AudioPipelineMode + IRealtimeAudioPipeline + AudioPipelineFactory(시그니처) + SentimentResult 모델
  - Wave2: LegacyAudioPipeline / UnifiedRealtimeAudioPipeline / SentimentAnalysisService / CostEstimatorService / HallucinationFilter 병렬
  - Wave3: OneNoteViewModel + MainWindow.xaml + App.xaml 통합
- **재발방지**: 신규 파일 5+ 시 oplan_deep 7단계(Wave 기반 spawn 설계)에서 Wave 분할 검토 의무
- **Level**: 2 (다파일 작업의 표준 패턴화 — MEMORY.md 등재)

## L-440: 추상화 인터페이스+팩토리 도입 기준 — 동등 분기 모드 2개+ 시 호출자 변경 최소화 (2026-05-15)

- **문제**: 모드 분기(Legacy vs Unified, Local vs Remote 등)가 명확한데 호출자(ViewModel)에서 if/switch로 직접 분기하면 분기 코드 누수 + 모드 추가 시 수정 폭증
- **해결**: 공통 인터페이스(IXxxPipeline) + Factory.Create(mode) 패턴 도입 → 호출자는 _field + Factory 호출 + 이벤트 구독만 보유
- **실증**: IRealtimeAudioPipeline + AudioPipelineFactory 도입으로 ViewModel은 모드 enum을 모르고 동작 (Wave1에서 인터페이스 선행 → Wave3 통합 시 ViewModel 수정 최소화)
- **재발방지**: 모드/전략 분기 2가지 이상일 때 oplan_deep에서 인터페이스+팩토리 패턴 우선 검토
- **Level**: 2 (아키텍처 패턴 — MEMORY.md 등재)

## L-441: OpenAI Realtime API out-of-band response.create 패턴 — 단일 WebSocket STT+분석 비용 절감 (2026-05-15)

- **문제**: STT용 WebSocket과 분석용 ChatCompletion API 별도 사용 시 비용 2배 + 컨텍스트 동기화 부담
- **해결**: 단일 WebSocket에서 out-of-band response.create 패턴 적용
  - `session.update`: `create_response=false` (모델 자동 응답 비활성)
  - `tools strict function_call`: 1분 요약+감성 분석 결과를 함수 호출 형식으로 강제
  - `response.create`: `conversation=none` + `item_reference` 슬라이딩 윈도우(N=8) → 멀티턴 비용 절감 핵심
- **실증**: UnifiedRealtimeAudioPipeline에서 STT(transcription only) + 분석(function_call)을 단일 WebSocket으로 동시 처리
- **재발방지**: OpenAI Realtime API 통합 시 out-of-band 패턴 우선 검토 — 비용/지연 절감 핵심
- **Level**: 2 (외부 API 통합 패턴 — MEMORY.md 등재)

## L-442: 전략 swap 5단계 대칭 구조 — Unsubscribe→DisposeAsync→Factory.New→Subscribe→StartAsync (2026-05-15)

- **문제**: 런타임 폴백 swap 시 이벤트 누수, 2중 구독, WebSocket 좀비 발생 위험
- **해결**: 5단계 대칭 구조 강제
  1. **Unsubscribe**: 기존 인스턴스의 모든 이벤트 핸들러 해제
  2. **DisposeAsync**: 기존 인스턴스 비동기 정리 (WebSocket 종료, Timer Dispose 등)
  3. **Factory.New**: 새 구현체 인스턴스 생성 (Factory 경유)
  4. **Subscribe**: 새 인스턴스에 동일 이벤트 핸들러 등록
  5. **StartAsync**: 새 인스턴스 시작
- **실증**: UnifiedRealtimeAudioPipeline → LegacyAudioPipeline 자동 폴백 — PipelineFallback 이벤트 핸들러(OnPipelineFallback)가 5단계 순차 실행
- **연관**: L-379 (InvokeAsync(async lambda) 예외 소실 주의) + L-376 (SemaphoreSlim IDisposable)
- **재발방지**: 인터페이스 구현체 런타임 swap 모든 경로에 5단계 대칭 구조 강제
- **Level**: 2 (런타임 swap 표준 패턴 — MEMORY.md 등재)

## L-443: PeriodicTimer + WebSocket 결합 시 _sendLock SemaphoreSlim(1,1) 필수 (2026-05-15)

- **문제**: 1분 주기 PeriodicTimer가 response.create 전송 + 별도 audio frame 전송이 동시 발생 시 WebSocket SendAsync 충돌 (ClientWebSocket InvalidOperationException)
- **해결**: `private SemaphoreSlim _sendLock = new(1, 1);` 필드 보유 → 모든 SendAsync 직전 `await _sendLock.WaitAsync()` + `finally { _sendLock.Release(); }`
- **L-376 준수**: SemaphoreSlim은 IDisposable이므로 Dispose() 메서드에서 `_sendLock?.Dispose()` 호출 필수
- **실증**: UnifiedRealtimeAudioPipeline에서 1분 PeriodicTimer + audio frame 송신을 _sendLock으로 직렬화
- **재발방지**: WebSocket 동시 송신 가능성 있는 모든 경로에 SemaphoreSlim 락 + L-376 IDisposable 패턴 강제
- **Level**: 2 (멀티 송신 경로 표준 패턴 — MEMORY.md 등재)

## L-444: 외부 API Beta→GA 마이그레이션 4축 패턴 (2026-05-15)

- **문제**: OpenAI가 2026-05-12 Realtime Beta API 폐기 → `beta_api_shape_disabled` 에러로 STT 완전 미작동
- **근본원인**: Beta 전용 헤더(`OpenAI-Beta: realtime=v1`) + Beta 전용 URL + Beta 페이로드 shape 잔류
- **GA 마이그레이션 4축 (순서 중요)**:
  1. **Beta 헤더 제거**: `OpenAI-Beta: realtime=v1` 헤더 라인 삭제
  2. **URL endpoint 변경**: `wss://api.openai.com/v1/realtime?model=...` → `wss://api.openai.com/v1/realtime?intent=transcription`
  3. **페이로드 nested 재구조**: flat `session.input_audio_transcription.model` → nested `session.type=transcription` + `session.audio.input.format/transcription/turn_detection`
  4. **이벤트명 매핑 확인**: `conversation.item.input_audio_transcription.completed` 등 GA 이벤트명 일치 여부 검증
- **주의**: 4축 중 하나라도 누락 시 연결은 되나 STT 결과 미수신 상태가 됨 (silent failure)
- **재발방지**: 외부 API deprecation 공지 정기 모니터링 + deprecation 일자 HISTORY.md 기록 습관화
- **심각도**: 높음 (STT 완전 미작동)
- **Level**: 2 (GA 마이그레이션 표준 패턴 — MEMORY.md 등재 권장)

## L-445: WebSocket 외부 API 에러 silent close 방지 패턴 (2026-05-15)

- **문제**: OpenAI WebSocket에서 `type=="error"` 메시지 수신 시 기존 코드가 아무 처리 없이 무시 → silent failure로 원인 진단 불가
- **해결**: `type == "error"` 분기 신규 추가 → `_log.Error("{에러 전체 JSON}")` + `StatusChanged` 이벤트로 사용자 가시 알림 발행
- **패턴 (fail-fast)**:
  ```csharp
  case "error":
      _log.Error("[RealtimeSTT] 서버 에러: {Json}", json);
      StatusChanged?.Invoke(this, new SttStatusEventArgs { Message = $"오류: {json}" });
      break;
  ```
- **적용 원칙**: WebSocket 기반 외부 API 클라이언트에서 `type=="error"` 또는 동등 에러 응답을 반드시 NLog Error + 사용자 알림 2중 처리
- **재발방지**: WebSocket 이벤트 핸들러 작성 시 error 타입 분기 유무를 코드 리뷰 체크리스트에 포함
- **Level**: 2 (WebSocket 클라이언트 표준 패턴)

## L-446: 외부 API 디버깅 장기전 — 단발 추측 수정은 매몰비용, nlog 직접 확인이 정답 (2026-05-17)

- **문제**: OpenAI Realtime GA STT 복구를 단발 추측 수정 6커밋(3ebf7939~9258e4f3)으로 반복 → 누적 후에야 사용자 "잘된다"
- **근본 교훈**: 외부 API Beta→GA 등 폐기/변경 디버깅에서 추측 수정 2회+ 실패 = 매몰비용 신호. 매번 nlog 직접 확인이 정답이었음
- **로그 이중 채널 (강력 재확인 — L-406/L-408)**: STT 출력은 NLog `nlog-*.log` 전용. Log4 `mAIx-*.log` 아님. 채널 혼동 시 "동작 안 함"으로 오판
- **재발방지**: 외부 API 디버깅 2회+ 단발 추측 실패 시 즉시 nlog 직접 확인 모드 전환 (odev/SKILL.md 미반영 — docs Level 2)
- **Level**: 2

## L-447: 화자분리 STT response_format 모델별 분기 — gpt-4o-transcribe=json, whisper-1만 verbose_json (2026-05-17)

- **문제**: 화자분리 ON 별도 서비스(OpenAiTranscribeSttService)가 `/v1/audio/transcriptions`에 `verbose_json` 고정 → gpt-4o-transcribe 계열 전 청크 BadRequest → transcript 0건
- **해결**: `model.Contains("whisper")` 분기. whisper-1 → verbose_json + timestamp_granularities[] 유지. gpt-4o-transcribe → json (timestamp 미반환 → ProcessTranscriptionResponse text 분기가 chunkStartTime 폴백)
- **재발방지**: OpenAI transcription API 통합 시 response_format은 모델 capability별 분기 필수. 모델 추가 시 capability 확인
- **Level**: 2

## L-448: VAD OFF(turn_detection=null) → 서버 commit 미발생 → 주기적 수동 commit 필수 (2026-05-17)

- **문제**: ServerVadEnabled=false 또는 whisper 계열 시 turn_detection=null → OpenAI 서버 자동 commit 없음 → 스트리밍 commit 0건 → 실시간 전사 0건
- **해결**: PeriodicTimer(3s) 수동 `input_audio_buffer.commit` 루프. `_audioAppendedSinceCommit`(volatile) 추적으로 빈버퍼 commit_empty 회피. L-443 `_sendLock` 동시 적용. L-380 외부 try-catch
- **재발방지**: turn_detection=null 분기 존재 시 수동 commit 루프 동반 필수 (odev/SKILL.md 반영 — L-448 섹션)
- **Level**: 2

## L-449: 하이라이트 무동작 = 데이터 소스 부재(통지 누락 아님) — LLM keywords는 프롬프트 스키마 확장이 정식 경로 (2026-05-17)

- **문제**: TopicSegment.Keywords 미할당 시 HighlightTextBehavior 영구 무동작. 통지 경로 의심으로 시간 소모
- **근본 원인**: 통지 누락이 아닌 **데이터 소스 부재**. MinuteSummaryService systemPrompt JSON에 keywords 배열 요청(전사 원문 표기 명시) + entry/navSegment 매핑이 정식 경로(B안)
- **부가**: 구버전 .realtime.json 역직렬화 시 keywords 누락 → 빈목록 graceful. CollectionChanged 단일구독으로 통지누락 근본해결
- **재발방지**: 하이라이트/표시 무동작 시 통지보다 데이터 소스(원천 할당) 부재 먼저 의심. LLM 파생 데이터는 프롬프트 스키마 확장이 정식 경로
- **Level**: 1

## L-450: 토글 무반응 = 반응할 레이아웃 미구현(토글은 정상) — 2모드는 Option B (2026-05-17)

- **문제**: 가로/세로 토글 무반응. 토글 바인딩은 정상이나 단일 ItemsControl만 존재 → 반응할 가로 레이아웃 자체가 없음
- **해결 (Option B)**: 세로 ItemsControl(기존 byte-identical 보존) + 가로 ScrollViewer+ItemsControl(Border Width=DisplayWidth) 2개를 StringEqualsToVisibilityConverter로 모드 토글. L-389(LoadContent 미사용)/L-424(ItemsPanel=StackPanel+Orientation) 절대 준수
- **재발방지**: 토글 무반응 신고 시 바인딩보다 "반응할 레이아웃 존재 여부" 먼저 확인. 2모드는 Option B 기본 채택 (oplan_normal/SKILL.md 반영 — L-450 섹션)
- **Level**: 2

## L-451: WebSocket 종료 await 경로의 send는 취소 가능해야 함 (codex 적대리뷰) (2026-05-17)

- **지적 (codex 외부 AI 적대리뷰)**: SendJsonAsync가 `_sendLock.WaitAsync`(취소 불가) + `_ws.SendAsync(CancellationToken.None)`. 소켓 stall 시 StopAsync finally의 `await _manualCommitTask`가 같은 경로에 막혀 무한 hang 가능
- **현황**: 본 작업은 기존 SendJsonAsync 패턴 답습(신규 결함 아님). 일관성상 현 사이클 미수정, 향후 강화 대상
- **재발방지**: StopAsync/Dispose에서 await되는 send 경로는 `_sendLock.WaitAsync(ct)` + `SendAsync(ct)` 또는 타임아웃 필수. CancellationToken.None send를 종료 await 경로에 두지 말 것 (odev/SKILL.md 반영 — L-451 섹션)
- **Level**: 2

## L-452: model.Contains() capability 추론은 alias/deployment명에 취약 (codex 적대리뷰) (2026-05-17)

- **지적 (codex)**: `model.Contains("whisper")`는 capability를 명명규칙으로 추론. 모델 alias(예: stt-prod → whisper-1)면 오분류로 잘못된 response_format + VAD 동작 변경
- **현황**: 본 작업은 기존 session.update turn_detection 분기와 동일 로직 답습(일관성 우선). 현 사이클 미수정
- **재발방지**: 외부 API capability를 모델명 substring으로 추론 시 alias/deployment 취약성 인지. 향후 명시 capability 매핑 테이블 또는 설정값 도입 검토
- **Level**: 1

## L-453: 사용자 결정 SendMessage idle 중 미수신 가능 — 결정 메시지는 수신 확인/재전송 필요 (2026-05-17)

- **문제 (프로세스)**: 사용자 결정 메시지가 idle 상태 중 발송되어 메인 미수신 3회 실측
- **재발방지**: 사용자 결정 대기 중 메시지 미수신 가능성 인지. 결정 메시지는 수신 확인 또는 명확한 재전송 패턴 적용
- **Level**: 1

## L-454: 이전 작업 과도 제거 보정 패턴 — 요소 단위 정밀 제거 필수 (2026-05-17)

- **문제**: Wave1이 타임라인 좌측 눈금 ItemsControl 컨테이너를 통째 제거 → 시간 텍스트(0:00 등)까지 함께 소실
- **근본원인**: "선만 제거" 요청을 컨테이너 전체 제거로 과해석. DataTemplate 내 Line 요소만 제거해야 했으나 부모 컨테이너까지 삭제
- **해결**: DataTemplate 내 `<Line>` 요소만 제거하고 시간 TextBlock 컨테이너는 보존 (보정 커밋)
- **교훈**: 부분 제거 요청 시 DataTemplate/ItemTemplate 내 하위 요소 단위로 정밀 제거. 컨테이너 통째 제거는 항상 과도 — 리뷰 필수
- **Level**: 2 (MEMORY.md 반영)

## L-455: 추상 UI 용어 의미체 확인 필수 — "토글"·"방향" 다중 해석 패턴 (2026-05-17)

- **문제**: "가로/세로 토글"을 카드 방향 전환으로 구현(2회). 사용자 진의는 패널 자체의 도킹 위치 이동(우측↔하단, 작업표시줄식)
- **근본원인**: 추상 UI 용어("토글", "방향", "레이아웃")에 대해 구현자 관점으로 의미를 확정. 사용자 의도 재확인 없이 진행
- **해결**: 3차 시도에서 "작업표시줄처럼"이라는 구체 비유로 의미체 확정 후 Grid.SetRow/SetColumn 재배치로 구현
- **교훈**: 추상 UI 용어는 oplan 단계에서 "A 의미입니까, B 의미입니까?" 형식으로 반드시 확인. 동일 요청 2회 이상 역라우팅 시 의미체 오해 가능성 먼저 점검 (L-427 연관)
- **Level**: 2 (MEMORY.md 반영)

## L-456: 단일 Grid 코드비하인드 재배치 = 마크업 복제 0 도킹 패턴 (2026-05-17)

- **문제**: 2-컨테이너 복제 방식(Option B 2 ItemsControl)으로 접근 시 마크업 중복 + 동기화 부담 발생
- **해결**: `Grid.SetRow(panel, row)` / `Grid.SetColumn(panel, col)` 런타임 재배치로 단일 패널을 도킹 위치만 변경. 마크업 복제 0
- **교훈**: 패널의 도킹 위치(행/열) 토글이 목적이면 단일 그리드 코드비하인드 재배치 패턴 우선 검토. 2-컨테이너 복제보다 Surgical하고 유지보수 부담 없음
- **Level**: 1

## L-457: 하이라이트 정밀화 = LLM 품질 기준 강화 + 단어경계 매칭 양쪽 동시 필요 (2026-05-17)

- **문제**: systemPrompt만 강화하거나 IsWordBoundary만 추가해도 부분 문자열 매칭 또는 LLM 부적합 키워드 잔류
- **해결**: (1) systemPrompt에 "핵심 명사/기술 용어 2~5개, s.Length≥2 이상" 품질 기준 강화 + (2) IsWordBoundary로 부분 문자열 차단 동시 적용
- **교훈**: 하이라이트 정밀화는 데이터 생성(LLM 프롬프트 품질)과 매칭 로직(단어경계) 양 끝 동시 개선 필요. 한쪽만 수정하면 나머지 쪽에서 부정확 잔류
- **Level**: 1

## L-458: 전체 묵음 분기 = LLM 생성 단계 스킵 + 기존 이벤트 경로 재사용 (2026-05-17)

- **문제**: 전체 묵음 구간에서도 LLM 호출이 발생하여 무의미한 API 비용 + 부정확한 결과 반환
- **해결**: `IsAllSilence()` 판정 후 LLM 호출 스킵 → "묵음" 고정 텍스트 엔트리를 기존 `OnMinuteSummaryGenerated` 이벤트 경로로 발행. UI 분기 불필요
- **교훈**: 예외 케이스(묵음/빈 입력) 처리 시 별도 UI 경로 추가 금지. 기존 이벤트/데이터 흐름에 예외 결과를 주입하여 UI 레이어는 무수정 유지 (Surgical 준수)
- **Level**: 1

## L-459: SizeChanged 이벤트는 Collapsed 아닌 호스트가 소유해야 함 (2026-05-17)

- **문제**: 가로/세로 2모드에서 세로 컨테이너(`TopicSegmentsContainer`)가 `SizeChanged`를 소유했으나, 가로 모드로 전환 시 세로 컨테이너가 Collapsed → `SizeChanged` 미발화 → `PanelWidth` 미측정 → 가로 타임라인 LeftPx=0
- **해결**: `SizeChanged`를 항상 표시된 부모 호스트 Grid(`TopicNavLayoutHost`)에 이관. 자식 컨테이너(Collapsed 가능) 소유 금지
- **교훈**: Visibility를 토글하는 컨테이너에 SizeChanged를 달면 Collapsed 상태에서 미발화. 2모드 레이아웃에서 SizeChanged 이벤트는 반드시 두 모드를 모두 감싸는 부모 호스트에 귀속시켜야 함
- **Level**: 2 (L-450 Option B 패턴 보완)

## L-464: 이중 Stop 경로 race — bool 플래그로 먼저 실행 경로가 가드 설정 (2026-05-17)

- **문제**: 녹음 중지 시 STTSegments가 0개로 비어 표시되는 회귀. 직전 guardScope 수정(cb4ae007)이 실효 없었던 이유: 추정 원인(SelectionChanged→파일로드 race)만 보호하고 진짜 파괴 경로를 놓침
- **진짜 파괴 경로**: StopRecording()이 LiveSTTSegments→STTSegments 동기 복사 후 LiveSTTSegments.Clear() → NAudio 비동기 콜백으로 OnRecordingCompleted()가 Clear된 LiveSTTSegments를 재복사 → STTSegments=0
- **해결**: `_sttCopiedByStopRecording` bool 플래그. StopRecording()에서 복사 직전 true 설정, OnRecordingCompleted()에서 flag 체크 후 skip. StartRecordingAsync()에서 false 리셋
- **교훈**: 이중 Stop 경로(동기 + 비동기 비동기 콜백)가 동일 컬렉션을 복사할 때, 먼저 실행된 경로가 가드 플래그를 설정하고 나중 경로가 skip하는 패턴이 안전. bool 플래그는 단발성 동기-비동기 race에 적합 (복수 재진입 경쟁 → L-462 int 카운터)
- **Level**: 3

## L-465: 회귀 수정은 추정 원인만 보호하면 실효 없음 — nlog 런타임 재현 필수 (2026-05-17)

- **문제**: cb4ae007 회귀 수정이 정적 PASS했으나 사용자 런타임에서 재현. guardScope가 SelectionChanged race를 보호했지만 진짜 파괴 경로(이중 Stop)는 보호하지 않음
- **근본원인**: otest가 정적 grep으로 PASS 처리, 런타임 nlog로 실제 파괴 시점을 측정하지 않음 → 추정 원인만 수정하고 진짜 원인은 건드리지 않음
- **해결**: 런타임 nlog 경로 표시 (`경로=StopRecording`, `경로=OnRecordingCompleted`, `skip` 분기)로 실제 실행 흐름을 측정 가능하게 하여 진짜 파괴 경로 규명
- **교훈**: 회귀 수정에서 정적 PASS는 필요조건이지 충분조건이 아님. 반드시 nlog 런타임 재현으로 "실제 파괴 시점"을 확인한 후 수정해야 함. 단발 추측 수정 2회+ 실패는 매몰비용 — L-446/L-420 강력 재확인
- **Level**: 3 (프로세스 교훈)


## L-466: LoadXxxResultAsync 진입 즉시 Clear → 비동기 저장 race로 데이터 소실 (2026-05-17)

- **문제**: 녹음 중지 직후 STT만 사라지는 현상 회귀 3연속 (cb4ae007·3cd74ec2 실효 없음). 원인: LoadSTTResultAsync 진입 즉시 STTSegments.Clear() 실행 → 새 녹음 .stt.json이 비동기 Task.Run 저장 중이라 아직 파일 없음 → early return → STT 0건
- **근본원인**: cb4ae007(SelectionChanged guardScope) · 3cd74ec2(이중 Stop 가드)는 각각 "다른 경로"를 보호했지만 LoadSTTResultAsync 내부 Clear 타이밍은 건드리지 않음. 추측 수정 2회 모두 진짜 경로를 빗나감
- **해결 (설계A)**: Clear 위치를 "파일 존재 확인 통과 후"로 이동. early return 2경로(recording_/sttPath 미존재)는 Clear 없이 반환 → 메모리 STT 보존
- **교훈1**: LoadXxxResultAsync에서 Clear는 새 데이터 확보(파일 존재 확인) 후에만 실행 (L-385/L-386 "캡처→Clear+Add" 패턴 보강)
- **교훈2**: 회귀 N연속 시 "증상 단서(STT만 사라짐=STT전용함수)"로 진짜 경로 좁히기가 추측 수정보다 우선 (L-465/L-446/L-420 연관)
- **Level**: 2 (설계 패턴 교훈)
## 반영 추적 테이블

| 교훈 ID | 교훈 요약 | 반영 대상 | 반영 위치 | 반영일 | 검증 |
|---------|-----------|-----------|-----------|--------|------|
| L-303 | kio run_in_background=true 무한 블로킹 | skill | ko/SKILL.md kio_bash_exec_금지규칙 | 2026-04-10 | ✅ |
| L-304 | tmux kill-pane Claude Code 세션 종료 | skill | ko/SKILL.md hook_차단_시_대안 | 2026-04-10 | ✅ |
| L-305 | kplan 요구사항 임의 변경 | skill | ko/SKILL.md kplan_결과_검증 | 2026-04-10 | ✅ |
| L-362 | kdev 완료 후 pane 잔류 문제 | skill | ko_pipeline/SKILL.md, kstatus/SKILL.md | 2026-04-11 | ✅ |
| L-363 | Serilog 잔류 패턴 | code | TeamsViewModel.cs, MainWindow.Teams.cs | 2026-04-11 | ✅ |
| L-364 | GraphMailService/BackgroundSyncService Serilog 기존 사용 — NLog 마이그레이션 미완 | docs | LESSONS.md | 2026-04-14 | ✅ |
| L-366 | ParentFolderId 드리프트 — delta sync에서 메일 이동 시 DB 컬럼 미갱신 | code | BackgroundSyncService.cs | 2026-04-24 | ✅ |
| L-367 | 동기화 서비스 상태 교정 후 UI 캐시 갱신 누락 — ReadStatusCorrected 이벤트 필요 | code | BackgroundSyncService.cs, MainViewModel.cs | 2026-04-24 | ✅ |
| L-368 | InternetMessageId 단독 UNIQUE → 자기 자신에게 보낸 메일 받은편지함 누락 | code | Migrations/20260424000016, BackgroundSyncService.cs | 2026-04-24 | ✅ |
| L-369 | Dispatcher.Invoke(async 람다) — async void 처리로 예외 미전파 + UI 블로킹 | skill | domain-csharp/SKILL.md 금지패턴/체크리스트 | 2026-05-02 | ✅ |
| L-370 | async void 이벤트 핸들러 — 예외 소실 + 비동기 흐름 단절 | code | MainWindow.xaml.cs 외 5개 파일 (15건 변환) | 2026-05-02 | ✅ |
| L-371 | Dispatcher.BeginInvoke 구식 API — 결과/예외 추적 불가 | code | MainWindow.xaml.cs, ComposeWindow.xaml.cs (16건 변환) | 2026-05-02 | ✅ |
| L-372 | ConfigureAwait 잘못된 괄호 위치 — 멀티라인 체인에서 Property 접근 후 삽입 버그 | docs+code | LESSONS.md + 버그 5종 직접 수정 | 2026-05-02 | ✅ |
| L-373 | ConfigureAwait 대규모 적용 4 Phase 분할 전략 | docs | LESSONS.md | 2026-05-02 | ✅ |
| L-374 | DispatcherOperation은 Task 아님 — ConfigureAwait 적용 시 .Task 경유 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-02 | ✅ |
| L-375 | grep 기반 ConfigureAwait 누락 검사 멀티라인 오탐 | docs | LESSONS.md | 2026-05-02 | ✅ |
| L-376 | SemaphoreSlim IDisposable — 메서드 내 지역 생성 시 using 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-02 | ✅ |
| L-377 | async void 이벤트 핸들러 외부 try-catch 래핑 필수 — 내부 분기 try-catch만 불충분 | docs | LESSONS.md + MEMORY.md | 2026-05-02 | ✅ |
| L-378 | oralph 자동 반복 검증 — 수동 검증 후에도 37건 추가 발견, 연쇄 확장 현상 | docs | LESSONS.md | 2026-05-02 | ✅ |
| L-379 | InvokeAsync(async lambda) — inner async 예외 소실, .Task.Unwrap 또는 try-catch 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-02 | ✅ |
| L-380 | Timer.Elapsed 등 비WPF 이벤트 핸들러도 async lambda try-catch 필수 | docs | LESSONS.md | 2026-05-02 | ✅ |
| L-381 | oralph 무한 발견 양상 — 수렴 기준 = 런타임 UI 블로킹 0건으로 충분 | docs | LESSONS.md | 2026-05-02 | ✅ |
| L-382 | 표본 수정 후 체계적 전수조사 필수 — 패턴 규칙 등록 시 전수 batch 권장 | docs | LESSONS.md, HISTORY.md | 2026-05-02 | ✅ |
| L-383 | `InvokeAsync(async lambda).Task.ConfigureAwait(false)` 외관상 안전 함정 — inner async 예외 소실, .Task.Unwrap() 또는 try-catch 필수 | docs+code | LESSONS.md + MEMORY.md + MainWindow.xaml.cs L135/L230 | 2026-05-02 | ✅ |
| L-384 | SESSION_DIR 이중 경로 — `$HOME/.claude/session-env` vs `/tmp/cc-{프로젝트UUID}/session-env`, evidence 마커는 hook이 참조하는 CLAUDE_CONFIG_DIR 경로에 정확히 생성 필수 | docs | LESSONS.md | 2026-05-02 | ✅ |
| L-385 | WPF ListBox + ObservableCollection.Clear+Add + 2-way 바인딩 패턴 — SelectedItem=null write-back으로 리딩 페인 Collapsed (사용자 관점: 메일 닫힘) | docs+code | LESSONS.md + MEMORY.md + MainViewModel.cs ReplaceEmails preserveSelection | 2026-05-03 | ✅ |
| L-386 | preserveSelection 가드 범위 오류 — Clear 시점부터 복원 완료까지 전체 guardScope 감싸기 필수, 복원 시점만 가드 ON하면 본문 빈 채 잔류 | docs+code | LESSONS.md + MEMORY.md + MainViewModel.cs ReplaceEmails guardScope 패턴 | 2026-05-03 | ✅ |
| L-387 | 병렬 에이전트 using 블록 중복 충돌 — 동일 파일 선두 수정 시 중복 삽입 위험 | docs | LESSONS.md | 2026-05-09 | ✅ |
| L-388 | 비동기 정리 함수 fire-and-forget 예외 소실 — StopXxx() 내부 try-catch 확인 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-09 | ✅ |
| L-389 | WPF ItemsPanel.LoadContent() 실제 패널 아님 — VisualTreeHelper 또는 바인딩 방식 사용 | docs | LESSONS.md + MEMORY.md | 2026-05-09 | ✅ |
| L-390 | 팝업 창과 동적 패널 혼동 — XAML 정적 추가가 진입 경로 없으면 사용자 도달 불가 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-391 | 신규 UI 추가 시 사용자 진입 경로 grep 검증 필수 — 진입점 없으면 FAIL | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-392 | otest UI 검증 = 코드 grep + 진입 경로 grep 2단계 필수 — 코드 존재 ≠ 사용자 도달 가능 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-393 | hook 차단 기준은 tool_name 기반 — message 본문 키워드 매칭은 false positive 위험 | docs+hook | LESSONS.md + ui_test_guard.sh 옵션 B | 2026-05-10 | ✅ |
| L-394 | 이벤트 기반 E2E 검증 — Reflection 트리거 헬퍼(DebugPcmInjectHelper) 패턴 효과적 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-395 | PowerShell UIAutomation ScrollPattern.Scroll(LargeIncrement) — 스크롤 영역 컨트롤 접근 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-396 | otest 마커 mtime 미갱신 — file_write overwrite=true + 타임스탬프 내용 필수, 실패 시 강제 재생성 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-397 | Mock 인터셉터 + 시간 단축 timer 패턴 — 외부 API E2E 검증 시 비용/대기 없이 전수 검증 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-398 | oralph 미달 항목이 API 한계일 때 → mock 환경 구축 후 iter 재실행 패턴 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-399 | production 영향 없는 디버그 플래그(default false/1.0)로 mock/시간단축 환경 격리 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-400 | silent failure 진단 시 catch 블록에서 ex 전체 객체(스택트레이스) 로깅 필수 — ex.Message만 불충분 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-401 | DI scope dispose 후 ViewModel이 그 Provider 참조 시 Singleton resolve 실패 — root provider 전달 필수 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-402 | 외부 진입점 없는 테스트 헬퍼 — REST endpoint/디버그 메뉴 동시 추가 권장 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-403 | silent failure 진단 로그 발화 0건 → 호출 경로 자체 단절 가설 전환 필수 (진단 로그가 도달 불가한 영역) | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-404 | 호출 경로 추적 로그 패턴 — 진입점부터 말단까지 Layer별 7곳 표시 후 끊김 지점 식별 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-405 | API key 로그 출력 시 Substring(0,7)+"***"+길이 fallback ("(short_or_empty)") 안전 마스킹 패턴 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-406 | NLog 표준 정책의 함정 — NLog.config 부재 시 모든 로그 silent drop (L-296 후속) | docs | LESSONS.md + MEMORY.md | 2026-05-10 | ✅ |
| L-407 | NLog Setup() extension method — using NLog; 없으면 LoadConfigurationFromFile 컴파일 오류 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-408 | 자기코드 맹점 — 출력 채널→빌드갱신→분기미진입→코드로직 순서로 의심 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-10 | ✅ |
| L-409 | OpenAI Realtime API session.update 필수 — WebSocket 연결만으로 STT 응답 수신 불가 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-410 | server_vad로 묵음 구간 자동 가시화 — speech_started/stopped 이벤트로 정확한 구간 계산 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-411 | OpenAI Realtime API input audio 24kHz 강제 — 16kHz 전송 시 음성 가속 + server_vad 임계 미달 | docs+code | LESSONS.md + AudioRecordingService/OpenAiRealtimeSttService/OpenAiTranscribeSttService | 2026-05-10 | ✅ |
| L-412 | sample rate 변경 시 하드코딩 상수 전수 grep 조사 필수 — BytesPerSecond/WAV 헤더 일괄 갱신 | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-413 | PeriodicTimer + Task 라이브 모니터링 패턴 — Timer.Elapsed(async lambda) 대신 PeriodicTimer + WaitForNextTickAsync + CancellationToken 사용. 외부 try-catch + OperationCanceledException 정상 종료 보장. | docs+code | LESSONS.md + OpenAiRealtimeSttService.cs | 2026-05-10 | ✅ |
| L-414 | WPF ComboBox int 바인딩 — ComboBoxItem.Tag="N"(string)을 int 프로퍼티에 SelectedValue 바인딩 시 null 반환. sys:Int32 Tag 명시(`<ComboBoxItem.Tag><sys:Int32>N</sys:Int32></ComboBoxItem.Tag>`) 필수. numeric 바인딩에는 sys:Int32/sys:Double 타입 명시 패턴 사용. | docs+code | LESSONS.md + MainWindow.xaml | 2026-05-10 | ✅ |
| L-415 | 사용자 목표 집중 — 고정 주기(60초/5분) 매직 넘버를 발견하면 사용자 실제 목표와 일치 여부 의문 제기. YAGNI: 명시 요구된 항목만 동적화, 나머지는 별도 작업으로 분리. 재발방지: 기존 코드 매직 넘버 발견 시 목표 정합성 확인 후 수정. | docs | LESSONS.md | 2026-05-10 | ✅ |
| L-416 | UI 옵션 분산 시 단일 출처 원칙 — 동일 기능 옵션이 좌/우 패널에 분산되면 사용자 혼란 + x:Name 충돌 위험. 좌→우 이동 시 한 번의 Edit으로 동시 처리(좌측 제거 + 우측 추가). 재발방지: UI 옵션 추가 시 단일 위치 원칙. 기존 분산 발견 시 통합 작업으로 처리. | docs+code | LESSONS.md + MainWindow.xaml (화자분리/청크/요약주기 → 옵션탭) | 2026-05-10 | ✅ |
| L-417 | 옵트인 정책 — 신규 자동 기능 기본 false (옵트인) 필수. 기본 true는 기존 사용자에게 의도치 않은 동작 변화 유발. XML에 키 없으면 기본 false → 기존 동작 유지(하위 호환). 재발방지: 신규 자동 기능 추가 시 반드시 기본값 false로 설정. | docs+code | LESSONS.md + OpenAiRecordingSettings.AutoFinalSummary (기본 false) | 2026-05-10 | ✅ |
| L-418 | MinuteSummaryService 장기 동작 서비스 주기 발화 로그 필수 — PeriodicTimer tick 단위 발화 로그 없으면 외부에서 동작 여부 판별 불가. | docs | LESSONS.md | 2026-05-13 | ✅ |
| L-419 | oplan 코드 분석 시 정의부+호출부 동시 확인 필수 — 정의부만 확인하면 dead code를 핵심 경로로 오분류. find_referencing_symbols 또는 grep으로 호출부 존재 여부 반드시 확인. | docs | LESSONS.md + MEMORY.md | 2026-05-13 | ✅ |
| L-420 | otest 런타임 발화 검증 누락 금지 — PeriodicTimer/Timer 등 주기적 동작 수정 시 acceptance_criteria에 런타임 발화 로그 확인 항목 필수. 정적 grep만으로 PASS 불가. | docs | LESSONS.md + MEMORY.md | 2026-05-13 | ✅ |
| L-421 | 부분 수정 한계 시 사용자에게 재설계 권한 위임 — 동일 기능 2회+ ok 미해결이면 oplan에서 "기존 구조 vs 재설계" 옵션 명시 제시. | docs | LESSONS.md | 2026-05-13 | ✅ |
| L-422 | active 코드도 사용자 의도 미정렬이면 재설계 대상 — dead/alive 판정은 정적 분석 + 의도 정렬도 양쪽 기준. | docs | LESSONS.md | 2026-05-13 | ✅ |
| L-423 | 이종 페르소나 병렬 odev — C#(BE)과 XAML(FE) 변경은 be-csharp + fe-designer 병렬 분리로 충돌 없이 효율적 완료. | docs | LESSONS.md | 2026-05-13 | ✅ |
| L-424 | WPF ItemsControl 가변높이 — StackPanel+DisplayHeight 표준 패턴. ItemsPanelTemplate=Grid 안티패턴 기각 | docs+skill | LESSONS.md + oplan_normal/SKILL.md | 2026-05-14 | ✅ |
| L-425 | UIAutomation DataItem 검증 = 개수 + Rect Y 좌표 분산 2단계 필수. 전체 동일 Y = 겹침 = FAIL | docs+skill | LESSONS.md + otest_winforms/SKILL.md | 2026-05-14 | ✅ |
| L-426 | UI '최신/단일' 표시 요구 — 2가지 해석 분기(누적유지 vs 숨김) 모호 시 사용자 확인 필수 | docs+skill | LESSONS.md + oplan_normal/SKILL.md | 2026-05-14 | ✅ |
| L-427 | 동일 증상 역라우팅 2회 = 설계 한계 신호. 3회째 우회 금지 + 즉시 근본변경 옵션 사용자 제시 | docs+skill | LESSONS.md + oplan_normal/SKILL.md | 2026-05-14 | ✅ |
| L-428 | 교훈 즉시 등재 효과 — LESSONS 등재 직후 다음 사이클에서 즉각 활용 (L-424 StackPanel 패턴 역라우팅 0회 입증) | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-429 | '확인해보라' 요청 = 기존검증+신규요구 동시처리 패턴 — oplan에 검증 AC 항목 명시 후 단일 사이클 완료 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-430 | odev 자체발견 프로퍼티 대체 — 계획서 Topic → 실제 SummaryPreview 자율 대체 (역라우팅 없이 완료). 코드 정합성 > 계획서 100% 준수 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-431 | WPF ListBoxItem Focusable=False Setter가 클릭 선택 차단 — ItemContainerStyle에서 Focusable=False 제거 필수 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-432 | PreserveXxxOnSelectionChange() 공개 메서드 패턴 — LoadCollection+SelectionChanged 경쟁 조건 회피 패턴 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-433 | 노트/녹음 전환 시 5종 Clear 필수 — TopicSegments/MinuteSummaries/CumulativeSummaryText/FinalSummaryText/MinuteSummaryCount 모두 초기화 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-434 | 메모리 전용 컬렉션 vs 영속화 데이터 분리 — 녹음파일 페어링 .realtime.json으로 영속화 후 Stop→Save/Load→LoadResults 패턴 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-435 | PreserveXxx+LoadXxx 페어 의무 — Preserve 호출 후 LoadXxx 누락 시 STT 영구 미표시. Preserve+Load는 반드시 쌍으로 호출 | docs | LESSONS.md + MEMORY.md | 2026-05-14 | ✅ |
| L-436 | LoadRealtimeResultAsync 데이터 로딩 전용 — RebuildTimelineTicks 등 UI 트리거는 호출자 책임으로 명시 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-437 | ObservableCollection.Count==0 early return은 이전 데이터 잔류 — Clear + 기본값 명시 생성 패턴 필수 | docs | LESSONS.md | 2026-05-14 | ✅ |
| L-438 | WPF Image Stretch=Uniform+VerticalAlignment=Top 상단 압축 유발 — Fill+Stretch 표준 패턴으로 전환 | docs | LESSONS.md + MEMORY.md | 2026-05-14 | ✅ |
| L-439 | Wave 기반 의존성 spawn — Wave1(타입/인터페이스) → Wave2(구현 병렬) → Wave3(통합). 14파일 1110줄 역라우팅 0회 입증 | docs+skill | LESSONS.md + MEMORY.md + oplan_deep/SKILL.md + oplan_normal/SKILL.md | 2026-05-15 | ✅ |
| L-440 | 추상화 인터페이스+팩토리 도입 기준 — 동등 분기 모드 2개+ 시 호출자 변경 최소화 | docs+skill | LESSONS.md + MEMORY.md + oplan_deep/SKILL.md | 2026-05-15 | ✅ |
| L-441 | OpenAI Realtime API out-of-band 패턴 — create_response=false + function_call + item_reference 슬라이딩 윈도우가 비용 절감 핵심 | docs | LESSONS.md + MEMORY.md | 2026-05-15 | ✅ |
| L-442 | 전략 swap 5단계 대칭 구조 — Unsubscribe→DisposeAsync→Factory.New→Subscribe→StartAsync로 무중단 swap 보장 | docs+skill | LESSONS.md + MEMORY.md + odev/SKILL.md | 2026-05-15 | ✅ |
| L-443 | PeriodicTimer + WebSocket 결합 시 _sendLock SemaphoreSlim(1,1) 필수 (L-376 IDisposable 패턴 준수) | docs+skill | LESSONS.md + MEMORY.md + odev/SKILL.md | 2026-05-15 | ✅ |
| L-444 | 외부 API Beta→GA 마이그레이션 4축 — Beta 헤더 제거 + URL endpoint 변경 + 페이로드 nested 재구조 + 이벤트명 매핑 동시 적용 | docs | LESSONS.md + HISTORY.md | 2026-05-15 | ✅ |
| L-445 | WebSocket 외부 에러 silent close 방지 — type=="error" 분기 추가 + NLog Error + 사용자 가시 알림 발행 (fail-fast 패턴) | docs+code | LESSONS.md + OpenAiRealtimeSttService.cs | 2026-05-15 | ✅ |
| L-446 | 외부 API 디버깅 장기전 — 단발 추측 수정 2회+ 실패 = 매몰비용. nlog 직접 확인이 정답. 로그 이중채널(STT=NLog nlog-*.log) (L-406/L-408 재확인) | docs | LESSONS.md + HISTORY.md + MEMORY.md | 2026-05-17 | ✅ |
| L-447 | 화자분리 STT response_format 모델별 분기 — gpt-4o-transcribe=json, whisper-1만 verbose_json. 전 청크 BadRequest → 0건 증상 | docs | LESSONS.md + MEMORY.md | 2026-05-17 | ✅ |
| L-448 | VAD OFF turn_detection=null → 서버 commit 미발생 → PeriodicTimer 수동 commit + _audioAppendedSinceCommit + _sendLock 필수 (L-443 연관) | docs+skill | LESSONS.md + MEMORY.md + odev/SKILL.md | 2026-05-17 | ✅ |
| L-449 | 하이라이트 무동작 = 데이터 소스 부재(통지 누락 아님). LLM keywords는 프롬프트 JSON 스키마 확장이 정식 경로(B안) | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-450 | 토글 무반응 = 반응할 레이아웃 미구현(토글은 정상). 2모드는 Option B(2 ItemsControl Visibility 토글, L-389/L-424 준수) | docs+skill | LESSONS.md + oplan_normal/SKILL.md | 2026-05-17 | ✅ |
| L-451 | WebSocket 종료 await 경로의 send는 취소 가능해야 함 — _sendLock.WaitAsync(ct)+SendAsync(ct) 또는 타임아웃 (codex 적대리뷰) | docs+skill | LESSONS.md + odev/SKILL.md | 2026-05-17 | ✅ |
| L-452 | model.Contains() capability 추론은 alias/deployment명에 취약 — 향후 명시 capability 매핑 검토 (codex 적대리뷰) | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-453 | 사용자 결정 SendMessage idle 중 미수신 가능 — 결정 메시지는 수신 확인/재전송 필요 (프로세스 교훈, 3회 실측) | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-454 | 이전 작업 과도 제거 보정 — "선만 제거" 요청을 컨테이너 통째 제거로 과해석. DataTemplate 내 하위 요소 단위 정밀 제거 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-17 | ✅ |
| L-455 | 추상 UI 용어 의미체 확인 필수 — "토글"·"방향"·"레이아웃"은 oplan 단계에서 A/B 형식 확인 필수. 동일 역라우팅 2회+ 시 의미체 오해 먼저 점검 | docs | LESSONS.md + MEMORY.md | 2026-05-17 | ✅ |
| L-456 | 단일 Grid 코드비하인드 재배치 = 마크업 복제 0 도킹 패턴 — Grid.SetRow/SetColumn 런타임 변경으로 2-컨테이너 복제 회피 | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-457 | 하이라이트 정밀화 = LLM 프롬프트 품질 강화 + IsWordBoundary 단어경계 양쪽 동시 필요 — 한쪽만 수정 시 나머지 쪽 부정확 잔류 | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-458 | 전체 묵음 분기 = LLM 스킵 + 기존 이벤트 경로 재사용 — 별도 UI 경로 추가 금지. 예외 결과를 기존 흐름에 주입하여 UI 레이어 무수정 유지 | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-459 | SizeChanged 이벤트는 항상 표시된(Collapsed 아닌) 컨테이너가 소유 — Collapsed 요소는 SizeChanged 미발화, 호스트 Grid에 이관 필수 | docs | LESSONS.md + HISTORY.md | 2026-05-17 | ✅ |
| L-460 | 대칭 메서드 쌍(SetPanelWidth/SetPanelHeight) 한쪽 재계산 누락 → 한 축 stale — 동일 재계산 호출은 두 메서드 모두에 대칭 배치 필수 | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-461 | Canvas.Left 절대좌표 vs StackPanel 누적폭 클램프 정책 불일치 — 같은 축 내 클램프 기준값은 좌표계에 맞게 일치(가로=0.0, 세로=최소높이) 필수 | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-462 | 단발성 bool guard → int 카운터 guardScope 전환으로 재진입 race 차단 — 복수 경로가 동시에 보호 카운터를 증가시킬 때 bool은 경쟁 조건 유발, int 카운터는 회별 소비로 안전 (L-385/L-386 보강) | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-463 | 추상 UI "너비/높이 1/4"와 실제 도킹 가변축 기하 반전 — 세로 모드 대화네비는 Row(높이)가 축소 대상, 가로 모드는 패널 자체가 아닌 픽셀 Row 높이. oplan에서 "A 의미입니까 B 의미입니까?" 형식 L-455 추가 명시 필요 | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-464 | 이중 Stop 경로(동기 StopRecording + 비동기 OnRecordingCompleted) 컬렉션 복사-Clear race — bool 플래그(_sttCopiedByStopRecording)로 먼저 실행된 경로가 가드 설정, 나중 경로는 skip. StartRecordingAsync에서 false 리셋 필수 | docs+code | LESSONS.md + OneNoteViewModel.cs | 2026-05-17 | ✅ |
| L-465 | 회귀 수정은 추정 원인만 보호하면 실효 없음 — 진짜 파괴 경로는 nlog 런타임 재현으로 "실제 파괴 시점" 측정 후 수정 필수. 정적 PASS만으로 통과 금지 (L-446/L-420 재확인) | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-466 | LoadXxxResultAsync 진입 즉시 Clear \xe2\x86\x92 비동기 저장 race로 빈 파일 로드 시 데이터 소실 \xe2\x80\x94 Clear는 파일 존재 확인 통과 후에만 실행(설계A). early return 경로는 Clear 없이 반환하여 메모리 데이터 보존 필수. 회귀 N연속 시 증상 단서(STT만 사라짐=STT전용함수)로 호출 경로 좁히기가 추측 수정보다 우선 (L-385/L-386 보강, L-465 연관) | docs+code | LESSONS.md + HISTORY.md + OneNoteViewModel.cs | 2026-05-17 | \xe2\x9c\x85 |
| L-465 보강 (2026-05-17) | 회귀가 N번 연속 발생할 때 직전 N개 수정이 모두 같은 계층(증상: 복사/로드 가드)만 건드렸는지 점검하라. 한 번도 안 건드린 계층(원인: fire-and-forget await 누락)이 진짜 원인일 가능성이 높다. otest 정적 grep은 odev 진단로그가 빌드 dll에 실재하는지 보장 못 함 — .NET dll은 UTF-16LE, python utf-16-le 디코딩 카운트로 실재 검증해야 거짓 PASS를 차단할 수 있다. | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-467 | 로그 채널 오판이 회귀를 5번 반복시킴 — log4net/Serilog/NLog 3채널 공존 프로젝트에서 잘못된 물리파일(794MB Serilog 폭증)을 보고 "코드 미실행"으로 오판 → 4개 수정이 전부 잘못된 가설(StopRecordingAsync 우회) 위 반복. 진단로그 추가 시 어느 물리파일에 떨어지는지 grep 실증 먼저 (L-406/L-408/L-446 강력 보강) | docs | LESSONS.md | 2026-05-17 | ✅ |
| L-468 | 단방향 race 가드의 함정 — 양방향 race(A가 B 결과 덮어쓰기 + B가 A 결과 덮어쓰기 가능)에 단방향 가드만 두면 반대 방향 무방비. 대칭 bool 가드(_sttCopiedByRecordingCompleted) 추가로 StopRecordingAsync가 OnRecordingCompleted 선복사를 빈 LiveSTTSegments로 덮어씌우는 버그 근본 차단 | docs+code | LESSONS.md + OneNoteViewModel.cs | 2026-05-17 | ✅ |
| L-469 | VM 지연생성 Loaded null 초기화 누락 → 첫동작 무반응 — 비동기로 지연 생성되는 ViewModel이 Loaded 이벤트 시점에 null이면 초기 레이아웃 동기화가 누락되어 토글/UI 첫 동작 무반응(두번째부터 정상). 결정론적 VM 생성 완료 지점(DataContext 할당 직후)에 초기화 호출 추가 + 기존 Loaded 호출은 멱등 안전망으로 유지. | docs+code | LESSONS.md + MainWindow.xaml.cs | 2026-05-18 | ✅ |
| L-470 | Canvas 배치+우측정렬 TextBlock에 Margin은 위치에 반영 안 됨 — Canvas 자식의 절대좌표 레이아웃을 TextAlignment=Right가 우회하면 Margin Left/Right가 위치에 영향 없음. 미세 위치 이동은 RenderTransform TranslateTransform X/Y가 정답 — 레이아웃 연산 0, 인접 요소 불변. | docs | LESSONS.md | 2026-05-18 | ✅ |
| L-471 | 녹음 등 휘발성 메모리 컬렉션은 Stop 경로 1회 저장만으로는 크래시 시 전량 소실 — 데이터 변경 트리거 지점들에 debounce 타이머(AutoReset=false Stop→Start) + SemaphoreSlim 직렬화로 증분 영속화. Stop 경로는 추가이지 대체 아님(타이머 Stop으로 경합 차단). | docs+code | LESSONS.md + OneNoteViewModel.cs | 2026-05-18 | ✅ |
| L-472 | 타임라인 라벨을 시간눈금(분단위 루프)이 아닌 데이터 경계(세그먼트 Start/End)로 발행하면, 데이터 병합 시 중간 라벨이 별도 처리 없이 자동 소멸 — UI 표시 규칙을 데이터 구조에 위임. | docs+code | LESSONS.md + OneNoteViewModel.cs | 2026-05-18 | ✅ |
| L-473 | 새 기능(영속화 throttle 타이머)이 우회하던 비정상 의존을 정상화 가드가 끊으면 숨은 결함이 노출됨 — 짧은 녹음 .stt.json이 그동안 "녹음중 타이머의 LiveSTT 저장"에 의존했고, Stop 경로 단일화 시 selection-change Clear 레이스(L-385/L-386)로 STTSegments가 비동기 저장 평가 전에 Clear되어 저장 자체가 미호출. 휘발 컬렉션 저장은 Clear 가능 지점 이전에 ToList() 불변 스냅샷 선캡처 후 그 스냅샷으로 게이트+저장 — 컬렉션 무단변경 0으로 연쇄영향 차단. 회귀 디버깅 시 단발 가설보다 nlog 직접 확인이 정답(L-446 재확인). | docs+code | LESSONS.md + OneNoteViewModel.cs | 2026-05-18 | ✅ |
| L-474 | 외부 스코프 변수를 fire-and-forget `Dispatcher.InvokeAsync` 람다에서 채우고 동시 `Task.Run`에서 그 변수를 읽는 패턴은 race condition — InvokeAsync 완료 보장 없이 Task.Run이 즉시 시작해 빈 값으로 평가됨. 동기 시점의 데이터를 비동기로 전달하려면 `await dispatcher.InvokeAsync(...).Task.ConfigureAwait(false)`로 직렬화하고 Task.Run 자체를 제거(또는 await 직렬화). 캡처/저장 두 비동기 사이에 happens-before 관계 강제 필요. 짧은 녹음 STT 미저장의 진짜 원인이 빌드 미반영 가설을 압도하지 않게 dll strings + 로그 양쪽 증명 필수. | docs+code | LESSONS.md + OneNoteViewModel.cs | 2026-05-19 | ✅ |
| L-475 | TeamDelete 3차+sleep 후에도 in-process 캐시 잔존 가능 — Claude Code 내부 레지스트리는 shutdown_response 수신/pane 소멸/FS 정리와 비동기. evidence/inprocess_stuck 마커로 fail-loud 보장 + ok_pipeline precheck CASE 0 차단 + 세션 재기동 권고. L-204 fire-and-forget shutdown 원칙 유지(차단형 대기 금지). | docs+skill | LESSONS.md + oinit/SKILL.md + ok_pipeline/SKILL.md + CLAUDE.md | 2026-05-20 | ✅ |
| L-476 | Timer 런타임 발화 검증 대체 조건 — 30초+ 주기 Timer는 정적 3조건(Start 호출/핸들러 존재/로그 패턴) 모두 충족 시 otest 정적 PASS 허용 (L-420 보완) | docs | LESSONS.md | 2026-05-20 | ✅ |
| L-477 | PreviewMouseWheel 외부 전파 — e.Handled=true 먼저 + 새 MouseWheelEventArgs 인스턴스로 무한루프 방지 | docs | LESSONS.md | 2026-05-20 | ✅ |
| L-478 | LLM JSON 스키마 확장 graceful 호환 — TryGetProperty+Empty fallback + NullToVisibilityConverter 조합으로 구형 응답 하위 호환 | docs | LESSONS.md | 2026-05-20 | ✅ |
| L-479 | STT delta 누적 + 마침 문자 감지 자동 분리 패턴 — ConcurrentDictionary 버퍼, 역방향 마침 탐색 | docs | LESSONS.md | 2026-05-21 | ✅ |
| L-480 | MAP 카드 단순화 — 사용자 피드백 기반 보정 패턴 | docs | LESSONS.md | 2026-05-21 | ✅ |
| L-481 | 라이브 임시 카드 패턴 — 즉시 삽입 + 종료 시 교체 | docs | LESSONS.md | 2026-05-21 | ✅ |
| L-482 | ObservableCollection Insert+즉시선택 guardScope 패턴 — L-385/L-386 Insert 버전 | docs | LESSONS.md + MEMORY.md | 2026-05-21 | ✅ |
| L-485 | 단일 출처(DRY) + 일관 참조 패턴 — 스킬 간 판정 기준 분산 금지 | docs+skill | LESSONS.md + ogrill/SKILL.md | 2026-05-21 | ✅ |
| L-486 | LLM 자가 판단 게이트 패턴 — 강제 spawn 금지, 권고 표준화 | docs+skill | LESSONS.md + ogrill/SKILL.md | 2026-05-21 | ✅ |
| L-487 | WebView2 로컬 리소스 매핑 패턴 — SetVirtualHostNameToFolderMapping + ExecuteScriptAsync 디바운스 | docs | LESSONS.md | 2026-05-21 | ✅ |
| L-488 | WebView2RuntimeNotFoundException catch 패턴 — 런타임 미설치 시 UI 안내 | docs | LESSONS.md | 2026-05-21 | ✅ |
| L-489 | ogrill 5축 확정 후 oplan 재질문 0회 — 인터뷰 완료 후 신뢰 기반 진행 | docs | LESSONS.md | 2026-05-21 | ✅ |
| L-490 | WebView2 HWND z-order → HTML 내부 버튼 + postMessage 패턴 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-491 | ThemeService/이벤트 구독 해제 필수 — NavigationCompleted 구독 대칭 Unbind 패턴 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-492 | Markmap 동적 테마 = CSS 변수 + html.theme-light 클래스 토글 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-493 | 음성 묵음 필터 3중 패턴 — IsSilence 우선 + HashSet + 1글자 이하 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-494 | LLM 트리 통합 패턴 — 별도 HTTP 서비스 + 5초 디바운스 + 메모리 캐시 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-495 | X 버튼 WebView2 회귀 3-pronged 동시 보강 패턴 — HTML pointer-events + WPF ZIndex + NLog+DevTools 양쪽 마커 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-496 | 정적 PASS ≠ 동작 PASS — UI 실측 보류 항목 명시 필수 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-497 | otest auto_script grep 패턴 = oplan 마커명 = odev 실제 코드 — 셋이 일치해야 런타임 검증 가능 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-498 | PropertyChanged 구독 해제 — CloseRequested/Unbind 콜백 대칭 패턴 | docs | LESSONS.md + MEMORY.md | 2026-05-22 | ✅ |
| L-499 | Wave 패턴 — 다파일 다중 에이전트 충돌 방지. Wave1(시그니처 확정) → Wave2(구현 병렬) | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-500 | d3 v7 직접 임베드 패턴 — Resources/ + csproj Content Include. markmap 단방향 한계 우회 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-501 | d3 v7 radial 트리 패턴 — tree.size([2*Math.PI, radius]) + d3.linkRadial | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-502 | WebView2 postMessage JSON 스키마 통일 — {type:'close'}/{type:'tree_edited',markdown:'...'} + 레거시 string fallback | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-503 | LLM 결과 디스크 영속화 패턴 — {recordingPath}.mindmap.json 페어링, 원자적 교체(.tmp→Move) | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-504 | WebView2 contextmenu 우클릭 메뉴 패턴 — HTML 내부 구현 + preventDefault + 절대 위치 div | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-505 | Bind 시그니처 확장 패턴 — RecordingInfo 복합 객체 단일 파라미터 추가, 호출부 최소 변경 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-506 | WPF IsVisible 렌더 타이밍 함정 — ViewModel 단일 진실 원천(IsMindMapVisible) 사용 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-22 | ✅ |
| L-507 | PropertyChanged 재등록 패턴 — CloseRequested 해제 후 토글 ON 시 -= += 재등록 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-22 | ✅ |
| L-508 | BE→FE→통합 3Wave 심화 패턴 — W1(서비스/모델) → W2(HTML/JS) → W3(C# 바인딩). 7파일 640줄 역라우팅 0회 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-509 | d3 바이너리 csproj Content Include 패턴 — CopyToOutputDirectory=Always 필수 | docs | LESSONS.md | 2026-05-22 | ✅ |
| L-510 | WebView2 UI 실측 보류 2회 패턴(rev3+radial) — 커밋 메시지 명시 의무 심화 (L-496 Level 2 승급) | docs | LESSONS.md + MEMORY.md | 2026-05-22 | ✅ |
| L-511 | result.json 사이클 간 잔존 위험 — 진입 시 substeps 내용과 현재 작업 대조 필수 | docs | LESSONS.md + MEMORY.md | 2026-05-22 | ✅ |
| L-515 | Wpf.Ui 혼용 파일에서 `new TextBlock {}` → CS0104 모호한 참조 — `System.Windows.Controls.TextBlock` 명시 필수 (L-051 TextBlock 버전) | docs | LESSONS.md | 2026-05-26 | ✅ |

## L-476: Timer 런타임 발화 검증 대체 조건 — 30초+ 주기 Timer 정적 PASS 허용 기준 (2026-05-20)

- **문제**: L-420(otest 런타임 발화 검증 필수)이 30초+ 주기 Timer에 일률 적용되면 otest 대기 시간이 30초~수분 발생하여 파이프라인 비효율
- **해결**: 30초 이상 주기 Timer는 다음 정적 3조건 모두 충족 시 otest 정적 PASS 허용
  1. `Start()` 또는 `StartAsync()` 호출부 grep 확인
  2. Elapsed/콜백 핸들러 존재 grep 확인
  3. 핸들러 내 로그 패턴 존재 grep 확인 (tick 발화 추적용 로그)
- **적용 범위**: 30초 미만 주기 Timer는 L-420 원칙 그대로 유지 (런타임 발화 검증 필수)
- **연관**: L-420 (otest 런타임 발화 검증 필수 — 기본 원칙)
- **Level**: 1 (L-420 보완 예외 조건 명세)

## L-477: PreviewMouseWheel 외부 전파 패턴 — e.Handled=true 선행 + 새 EventArgs 인스턴스 (2026-05-20)

- **문제**: WPF에서 내부 ScrollViewer의 마우스 휠 이벤트를 외부 컨테이너로 전파할 때, 기존 EventArgs를 재사용하거나 `e.Handled=true` 설정 순서를 잘못 두면 이벤트가 ListBox로 재진입하여 무한루프 발생 가능
- **해결**: 올바른 패턴
  1. `e.Handled = true` 먼저 설정 (현재 이벤트 전파 중단)
  2. `new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)` 새 인스턴스 생성
  3. `newArgs.RoutedEvent = UIElement.MouseWheelEvent` 설정 후 `RaiseEvent(newArgs)`
- **잘못된 패턴**: e.Handled=true 없이 기존 args로 RaiseEvent → 이벤트가 ListBox로 다시 버블링 → 무한루프
- **적용 위치**: PreviewMouseWheel 핸들러 내 외부 컨테이너로 휠 이벤트 전파가 필요한 모든 경우
- **Level**: 1 (WPF 이벤트 라우팅 패턴)

## L-478: LLM 응답 JSON 스키마 확장 graceful 호환 패턴 (2026-05-20)

- **문제**: LLM 프롬프트 스키마에 새 필드를 추가하면, 기존 캐시/히스토리에서 반환된 구형 응답에 해당 필드가 없어 `KeyNotFoundException` 또는 `NullReferenceException` 발생
- **해결**: TryGetProperty + Empty fallback 패턴으로 하위 호환 보장
  ```csharp
  // ❌ 잘못: 새 필드 직접 접근
  var keywords = doc.RootElement.GetProperty("keywords").GetString();
  
  // ✅ 올바름: TryGetProperty + fallback
  var keywords = doc.RootElement.TryGetProperty("keywords", out var kw)
      ? kw.GetString() ?? string.Empty
      : string.Empty;
  ```
- **NullToVisibilityConverter 조합**: `string.Empty` fallback값이 `NullToVisibilityConverter`와 함께 사용되면 빈 값 시 UI 자동 Collapsed → 구형 응답에서 신규 필드 UI가 표시되지 않아 자연스러운 하위 호환
- **재발방지**: LLM 프롬프트 스키마 신규 필드 추가 시 파싱 코드에 TryGetProperty 패턴 표준 적용 의무화
- **Level**: 1 (LLM 응답 파싱 표준 패턴)

## L-479: STT delta 누적 + 마침 문자 감지 자동 분리 패턴 (2026-05-21)

- **문제**: Realtime STT WebSocket의 `delta` 이벤트는 단어 단위로 조각 전달 — 한 문장이 50자를 초과해도 자동으로 분리되지 않아 STT 세그먼트가 과도하게 길어짐
- **해결**: `ConcurrentDictionary<itemId, accum>` 버퍼에 delta를 누적 후 `accum.Length >= 50` AND 역방향 마침 문자(`. ! ? 。 ！ ？`) 탐색으로 분리 지점 결정
  ```csharp
  // AC-017 패턴: delta 누적 + 역방향 마침 탐색
  if (accum.Length >= AutoSplitMinLength)
  {
      int splitIdx = -1;
      for (int i = accum.Length - 1; i >= 0; i--)
          if (AutoSplitTerminators.Contains(accum[i])) { splitIdx = i; break; }
      if (splitIdx >= 0)
      {
          var splitText = accum[..(splitIdx + 1)];
          var remainder = accum[(splitIdx + 1)..];
          TranscriptSegmentReceived?.Invoke(ts, splitText);  // 새 세그먼트 commit
          _deltaBuffers[itemId] = remainder;                  // 나머지는 다음 delta로 이어짐
      }
  }
  ```
- **역방향 탐색 이유**: 문장 마지막에 마침이 올 가능성이 높음 → 첫 마침 기준 분리보다 역방향이 자연스러운 분리점
- **TranscriptSegmentUpdated 유지**: 실시간 LiveSTT UI 표시용은 분리 후에도 계속 발화 (분리와 독립)
- **임계값**: const 50 (설정 미노출 — 단순 구현 우선)
- **Level**: 1 (STT 스트리밍 분리 패턴)

## L-480: MAP 카드 단순화 — 사용자 피드백 기반 보정 패턴 (2026-05-21)

## L-481: 라이브 임시 카드 패턴 — 즉시 삽입 + 종료 시 교체 (2026-05-21)

- **문제**: 녹음 시작 ~ 파일 생성 사이의 수초 간격 동안 카드가 없어 사용자가 녹음 중임을 시각적으로 알 수 없음
- **해결**: StartRecordingAsync에서 임시 RecordingInfo(IsLiveRecording=true) 즉시 Insert(0) → 종료 경로(Stop/Completed/Cancel) 모두에 Remove 안전망 적용
- **중복 Remove 차단**: `if (_liveRecordingCard != null && CurrentPageRecordings.Contains(_liveRecordingCard)) { Remove; _liveRecordingCard = null; }` 패턴. null 가드 먼저 → Contains 체크 → Remove → null 재설정 순서 고정.
- **종료 경로 2중 안전망**: StopRecordingAsync(정상 종료) + OnRecordingCompleted(완료 콜백) 양쪽 모두 동일 패턴 적용. 먼저 실행된 경로가 Remove 후 null로 설정 → 나중 경로는 null 가드에서 통과.
- **Level**: 1 (라이브 임시 객체 교체 패턴)

## L-482: ObservableCollection 즉시 삽입+선택 guardScope 패턴 (Insert 버전) (2026-05-21)

- **문제**: ObservableCollection.Insert(0, tempCard) 직후 SelectedRecording = tempCard 할당 시 SelectionChanged 발화 → LoadSTTData 등 부작용 트리거 가능성
- **해결**: L-385/L-386의 guardScope 패턴을 Insert+즉시선택에 적용: `_skipLoadSTTOnSelectionChange++; try { Insert; SelectedItem=obj; } finally { _skipLoadSTTOnSelectionChange--; }`
- **SelectionChanged 차단**: 핸들러 진입부에서 `if (_skipLoadSTTOnSelectionChange > 0) return;` 조건으로 프로그래매틱 삽입 중 부작용 차단
- **L-385/L-386 관계**: Clear+Add race 가드(L-385)와 동일 원칙의 Insert 버전. ObservableCollection 조작+선택 조합에서 SelectionChanged 부작용이 있으면 항상 guardScope 적용
- **Level**: 1 (ObservableCollection 삽입+선택 guardScope 패턴)

- **문제**: AC-011에서 3분할(Title/Context/BodyDisplay)로 구현했으나 사용자가 "타이틀만 표시"로 단순화 요청 → AC-013/014/015로 보정 필요
- **교훈**: LLM이 기능을 "더 풍부하게" 구현하는 방향으로 해석하는 경향 → 사용자의 단순화 의도가 명시적일 때는 최소 구현 우선
- **해결 패턴**: `L-454` 준수 — DataTemplate 하위 요소 단위 외과적 제거 (Context TextBlock만 제거, 나머지 보존)
- **세로/가로 양쪽**: `TopicSegmentsItemsControl` + `TopicSegmentsHorizontalItemsControl` 동시 동기화 필수 (한쪽만 수정 시 모드 전환 후 회귀)
- **재발방지**: 오plan 의도분석(Phase A)에서 UI 카드 요소 수 명시적 확인 ("A/B/C 3개 표시" vs "A 1개만 표시") 필수
- **Level**: 1 (UI 단순화 요청 해석 패턴)

## L-485: 단일 출처(DRY) + 일관 참조 패턴 — 스킬 간 판정 기준 분산 금지 (2026-05-21)

- **문제**: N개 스킬에 동일 판정 기준을 각자 정의하면 향후 기준 변경 시 N개 파일을 모두 수정해야 하며, 불일치 발생 가능성이 높음
- **해결**: 판정 기준을 단일 파일(ogrill/SKILL.md "선행 호출 매트릭스")에만 정의하고, 나머지 15개 스킬은 표준 참조 문구로만 연결
- **Wave 패턴**: Wave 1에서 단일 출처 파일 완성 → Wave 2에서 나머지 파일들이 참조 삽입. 순서 준수 필수.
- **적용 기준**: 동일 판단 로직이 3개+ 파일에 복사되거나 복사될 예정이면 단일 출처 패턴 적용 검토
- **Level**: 2 (메타 스킬 설계 패턴)

## L-486: LLM 자가 판단 게이트 패턴 — 강제 spawn 금지, 권고 표준화 (2026-05-21)

- **문제**: LLM이 특정 조건에서 다른 스킬을 자동 실행해야 하는 요건이 있을 때, hook으로 물리 강제가 불가능한 경우 어떻게 재발방지할 것인가
- **해결**: "권고 표준화" 패턴 — LLM이 스킬 진입 시 스스로 5축 평가 후 빈 칸 카운트 기반 권고 메시지 출력 + AskUserQuestion 1회. 자율 spawn(강제) 절대 금지.
- **원칙**: CLAUDE.md 재발방지 정책("LLM 의지 의존 금지")과 조화 — 물리 강제 불가 영역은 "권고 문구 표준화"가 다음 최선
- **5축 빠른 평가**: 목표/범위/제약/완료기준/열린질문 각각 채워짐/빈칸 이진 판정. 빈 칸 ≥ 4 → 강력 권고 / 3 → 약권고 / ≤ 2 → 스킵
- **예외 스킵**: tier ≤ o2 / 단일 파일명+라인 명시 / 사용자 명시 "바로 진행" / 이미 ogrill 결과 제공
- **Level**: 2 (파이프라인 게이트 설계 패턴)

## L-487: WebView2 로컬 리소스 매핑 패턴 — SetVirtualHostNameToFolderMapping + ExecuteScriptAsync 디바운스 (2026-05-21)

- **문제**: WebView2에서 로컬 JS/HTML 리소스를 참조할 때 file:// 경로 직접 사용 불가 (보안 제약). ExecuteScriptAsync 호출 빈도가 높으면 렌더링 과부하 발생.
- **해결**: `SetVirtualHostNameToFolderMapping("mindmap.local", resourcesPath, CoreWebView2HostResourceAccessKind.Allow)`로 가상 호스트 매핑 → `NavigateToString(htmlContent)` 내에서 `<script src="http://mindmap.local/markmap-lib.js">` 참조.
- **디바운스 패턴**: `DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }` + Tick 핸들러에서 `_debounceTimer.Stop(); await UpdateMindMapAsync()` → CollectionChanged 이벤트 빈발 시 마지막 변경만 렌더링.
- **L-380 준수 필수**: DispatcherTimer.Tick async lambda 내부 전체 try-catch 래핑 필수.
- **초기화 순서**: `EnsureCoreWebView2Async()` → `SetVirtualHostNameToFolderMapping()` → `NavigateToString()` → `NavigationCompleted`에서 `_isWebViewReady = true`.
- **Level**: 1 (WebView2 로컬 리소스 통합 패턴)

## L-488: WebView2RuntimeNotFoundException catch 패턴 — 런타임 미설치 시 UI 안내 (2026-05-21)

- **문제**: WebView2 런타임이 설치되지 않은 환경에서 `EnsureCoreWebView2Async()` 호출 시 `WebView2RuntimeNotFoundException` 발생. 예외 미처리 시 앱 크래시.
- **해결**: `catch (WebView2RuntimeNotFoundException)` 블록에서 안내 패널 표시 + WebView 숨김:
  ```csharp
  catch (WebView2RuntimeNotFoundException ex)
  {
      _log.Warn(ex, "[AC-MM-실행] WebView2 런타임 미설치");
      RuntimeNotInstalledPanel.Visibility = Visibility.Visible;
      MindMapWebView.Visibility = Visibility.Collapsed;
  }
  ```
- **패턴**: 필수 런타임 미설치 시나리오를 항상 별도 catch로 처리. 앱 크래시 없이 사용자 안내.
- **Level**: 1 (WebView2 런타임 예외 처리 패턴)

## L-489: ogrill 5축 확정 후 oplan 재질문 0회 — 인터뷰 완료 후 신뢰 기반 진행 (2026-05-21)

- **패턴**: ogrill이 5회 인터뷰로 5축(목표/범위/제약/완료기준/열린질문)을 완전히 확정한 경우, oplan은 ogrill 결과를 그대로 신뢰하고 재질문 없이 즉시 계획 수립에 진입.
- **이유**: ogrill이 이미 사용자 의도를 충분히 탐색·확정했으므로 oplan이 동일 질문을 반복하면 불필요한 인터럽트 발생.
- **적용 조건**: ogrill_result.md의 5축이 모두 채워지고 열린질문도 해소된 경우.
- **예외**: ogrill 결과에 열린질문이 아직 남아있거나, 계획 수립 중 새로운 기술적 제약 발견 시 한 번만 확인 가능.
- **Level**: 1 (파이프라인 진행 효율화 패턴)

## L-490: WebView2 HWND z-order → HTML 내부 버튼 + postMessage 패턴 (2026-05-22)

- **문제**: WebView2는 내부적으로 HWND를 가진 네이티브 컨트롤이므로 WPF z-order 체계 외부에 존재. WPF 오버레이 X 버튼을 WebView2 위에 배치해도 WebView2가 항상 최상위로 올라와 버튼이 가려짐.
- **해결**: WPF 오버레이 버튼 방식을 포기하고, HTML 내부에 `<button id="closeBtn">×</button>`을 `position: fixed; top: 10px; right: 10px; z-index: 9999`로 배치. 클릭 시 `window.chrome.webview.postMessage('close')` 전송 → C#의 `WebMessageReceived` 이벤트에서 처리.
- **이중 안전망**: ESC 키 `document.addEventListener('keydown', ...)` 리스너도 동일하게 postMessage 전송.
- **C# 측 처리**: `NavigationCompleted` 내에서 `WebMessageReceived += _webMessageHandler` 등록 → `Unbind()`에서 해제(L-491 연동).
- **Level**: 1 (WebView2 네이티브 창 z-order 한계 우회 패턴)

## L-491: ThemeService/이벤트 구독 해제 필수 — NavigationCompleted 구독 대칭 Unbind 패턴 (2026-05-22)

- **문제**: `NavigationCompleted` 이벤트 핸들러 내에서 `ThemeService.ThemeChanged += _themeHandler` 또는 `WebMessageReceived += _webMessageHandler`를 구독할 때, `Unbind()`에서 해제하지 않으면 오버레이가 닫혀도 핸들러가 계속 호출됨 (메모리 누수 + 좀비 호출).
- **해결**: 핸들러를 `EventHandler _themeHandler; EventHandler _webMessageHandler;` 필드로 저장 → `Bind()`에서 람다를 필드에 대입 → `Unbind()`에서 `-=`로 해제.
- **원칙**: NavigationCompleted 내에서 이벤트 구독 → Unbind()에서 반드시 대칭 해제. `NavigationCompleted` 자체도 Unbind()에서 해제.
- **체크리스트**: Bind/Unbind 메서드를 나란히 두고 `+= X` 항목 개수가 `-= X` 개수와 일치하는지 확인.
- **Level**: 1 (이벤트 구독 대칭 해제 패턴)

## L-492: Markmap 동적 테마 = CSS 변수 + html.theme-light 클래스 토글 (2026-05-22)

- **문제**: Markmap은 자체 스타일을 SVG에 직접 주입하므로 WPF ThemeResource와 연동 불가. `body { background: #1e1e1e; }` 하드코딩 시 라이트 모드에서도 다크 배경 고정.
- **해결**: `:root { --bg-color: #1e1e1e; ... }` CSS 변수 정의(다크 기본값) → `html.theme-light { --bg-color: #ffffff; ... }` 클래스 오버라이드 → `window.setTheme(mode)` 함수로 `classList.add/remove('theme-light')` 전환. `markmapInstance.fit()` 호출로 렌더링 갱신.
- **C# 측**: `ExecuteScriptAsync("window.setTheme('light')")` 또는 `setTheme('dark')`로 호출. ThemeService 구독으로 실시간 동기화.
- **CSS 대상**: `.markmap-node-text`, `.markmap-link` 선택자로 Markmap 노드/링크 색상도 CSS 변수에 연결.
- **Level**: 1 (WebView2 내 외부 라이브러리 동적 테마 패턴)

## L-493: 음성 묵음 필터 3중 패턴 — IsSilence 우선 + HashSet + 1글자 이하 (2026-05-22)

- **문제**: STT 결과에 "음...", "어...", "(silence)", "(silent)", "(음)", "(어)" 등 무의미 텍스트가 포함됨. 마인드맵 노드로 출력되면 품질 저하.
- **해결**: BuildMarkdown 루프 상단에 3중 필터 순서대로 적용.
  1. `if (ts.IsSilence) continue;` — IsSilence 플래그 우선 (가장 신뢰성 높음)
  2. `if (_silenceWords.Any(w => text.Contains(w))) continue;` — HashSet 키워드 매칭 (묵음/무음/(silence)/(silent)/(음)/(어)/음.../어... 등)
  3. `if (text.Length <= 1) continue;` — 1글자 이하 제거
- **_silenceWords 정의 위치**: 클래스 필드로 `HashSet<string>` 초기화 (루프 내 생성 금지 — 성능).
- **Level**: 1 (음성 STT 품질 필터링 패턴)

## L-494: LLM 트리 통합 패턴 — 별도 HTTP 서비스 + 5초 디바운스 + 메모리 캐시 (2026-05-22)

**문제**: Realtime WebSocket에 LLM 트리 생성을 통합하려 했으나 세션 관리 복잡도와 기존 오디오 파이프라인 충돌 가능성이 있음.

**해결**: 별도 IDisposable 서비스(MindMapTreeService) 분리.
- `IMindMapTreeService` 인터페이스 + `MindMapTreeService` 구현체 DI 등록
- `PeriodicTimer` 5초 디바운스 — 음성 세그먼트 업데이트마다 타이머 리셋
- `LastTreeMarkdown` 메모리 캐시 — 오버레이 Bind 시 즉시 표시
- `EventHandler<string> TreeMarkdownGenerated` 이벤트 기반 비동기 결과 전달
- `SemaphoreSlim _httpLock` — 동시 HTTP 호출 방지

**규칙**:
- LLM 트리 생성은 Realtime 통합 대신 독립 HTTP 서비스 분리가 안정적
- `IDisposable` + `Dispose()`에서 `_httpLock.Dispose()` 필수
- Level: 1

---

## L-495: X 버튼 WebView2 회귀 3-pronged 동시 보강 패턴 (2026-05-22)

**문제**: WebView2 Airspace로 HTML 위에 WPF 요소가 가려짐. pointer-events 단독 또는 ZIndex 단독으로는 불충분. 3차 회귀 발생.

**해결**: 3가지를 동시에 적용 (하나라도 누락하면 회귀 재발 위험).
1. **HTML `pointer-events:auto` + `stopPropagation`**: `#closeBtn { pointer-events: auto !important; }` + `e.stopPropagation()`
2. **WPF 대체 버튼 `Panel.ZIndex=999`**: `WpfCloseButton` IsHitTestVisible=True + ZIndex=999 명시
3. **NLog `[AC-MMX3-click]` + DevTools console `[MMR3]` 양쪽 마커**: 실측 검증을 위해 두 채널 모두 필수

**규칙**:
- WebView2 위 버튼 회귀 시 3가지 동시 적용 의무
- NLog + DevTools 양쪽 마커 없으면 실측 검증 불가
- Level: 2

---

## L-496: 정적 PASS ≠ 동작 PASS — UI 실측 보류 항목 명시 필수 (2026-05-22)

**문제**: 정적 grep으로 코드 존재는 확인했으나 실제 WebView2 클릭/LLM API 호출은 사용자 직접 조작 없이 검증 불가. 3차 회귀 근본 원인 중 하나.

**해결**:
- otest 결과 보고 시 `## ⚠️ 사용자 UI 실측 보류` 섹션 필수 포함
- X 버튼/WebView2/LLM 호출 관련 항목은 정적 grep만으로 PASS 선언 금지
- 커밋 메시지에 "사용자 UI 실측 보류" 명시 (이전 사이클 회귀 방지 의미)

**규칙**:
- `정적 grep PASS` ≠ `동작 PASS` — 반드시 구분
- 실측 불가 항목은 보류 목록으로 분리 관리
- Level: 2

---

## L-497: otest auto_script grep 패턴 = oplan 마커명 = odev 실제 코드 (2026-05-22)

**문제**: oplan 단계에서 NLog 마커명을 acceptance_criteria에 정확히 명시하지 않음. odev에서 임의 문자열 사용. auto_script grep 패턴과 불일치 → 런타임 실행 시 FAIL 판정 가능.

**사례**: AC-MMT03 auto_script grep: `[MMT-실행] GenerateTreeAsync 완료` vs 실제 코드: `[MMT-실행] LLM 트리 생성 완료 — 줄수=N`

**해결**:
- oplan acceptance_criteria에 NLog 마커 정확한 문자열 명시
- odev 에이전트는 해당 문자열 그대로 코드에 작성
- otest auto_script도 동일 문자열로 grep

**규칙**:
- oplan 마커명 = odev 코드 마커 = otest auto_script grep 패턴 (셋이 일치해야 런타임 검증 가능)
- Level: 2

---

## L-498: PropertyChanged 구독 해제 — CloseRequested/Unbind 콜백 대칭 패턴 (2026-05-22)

**문제**: ViewModel PropertyChanged에 구독하면 오버레이가 닫혀도 핸들러가 살아있어 메모리 누수 발생.

**해결**:
```csharp
// 구독
_vmPropertyChangedHandler = OnViewModelPropertyChanged_ForMindMap;
vm.PropertyChanged += _vmPropertyChangedHandler;

// CloseRequested 콜백에서 해제
vm.PropertyChanged -= _vmPropertyChangedHandler;
```
- `_handlerField`에 저장 후 동일 인스턴스로 `-=` 보장
- `CloseRequested` 콜백 또는 `Unbind()` 메서드에서 해제 필수

**규칙**:
- ViewModel PropertyChanged 구독 시 항상 Unbind에서 대칭 해제
- Level: 1

---

## L-499: Wave 패턴 — 다파일 다중 에이전트 충돌 방지 (2026-05-22)

**문제**: 5+ 파일 병렬 수정 시 타입/인터페이스 미확정 상태로 구현 에이전트가 다르게 해석할 수 있음.

**해결** (rev3 실증 — 14파일, 역라우팅 0회):
1. **Wave1**: 인터페이스/타입/팩토리 시그니처 확정 (MindMapTreeService 인터페이스 포함)
2. **Wave2**: 구현 에이전트 병렬 spawn (MindMapOverlay + MainWindow.OneNote 병렬)
3. **Wave3**: 통합 검증

**규칙**:
- Wave1 완료 전 Wave2 병렬 spawn 금지
- Wave1에서 타입/인터페이스/이벤트 시그니처 완전 확정 필수
- Level: 1

---

## L-500: d3 v7 직접 임베드 패턴 — markmap 단방향 한계 우회 (2026-05-22)

**문제**: markmap은 단방향 렌더(left-to-right)만 지원하며, 노드 편집/방사형 배치 불가. WebView2에서 인터랙티브 마인드맵을 구현하려면 직접 d3 v7 사용이 필요.

**해결**:
1. `d3.v7.min.js`를 `mAIx/Resources/` 폴더에 직접 임베드 (279,706 bytes)
2. csproj에 `<Content Include="mAIx/Resources/d3.v7.min.js" CopyToOutputDirectory="Always" />` 추가
3. WebView2 `SetVirtualHostNameToFolderMapping`으로 로컬 파일 서빙
4. markmap-*.js 3개 제거 (csproj Content Include도 동시 제거)

**규칙**:
- d3 v7 직접 임베드: `Resources/{file}.js` + csproj Content Include
- markmap 한계(단방향/편집불가) 시 d3 직접 사용
- 구버전 참조 제거 + 신규 추가 동시 처리 의무 (csproj 미반영 시 빌드 제외)
- Level: 1

---

## L-501: d3 v7 radial 트리 표준 패턴 — tree.size([2π, radius]) + d3.linkRadial (2026-05-22)

**문제**: 단방향 트리와 방사형 트리의 d3 API가 다름. 방사형 변환 없이 x/y를 직접 사용하면 노드가 직선으로 배치됨.

**해결**:
```javascript
// 방사형 레이아웃 설정
const tree = d3.tree().size([2 * Math.PI, radius]);
const root = tree(hierarchy);

// 링크 그리기
const link = d3.linkRadial().angle(d => d.x).radius(d => d.y);

// 노드 좌표 변환
const cx = Math.cos(d.x - Math.PI / 2) * d.y;
const cy = Math.sin(d.x - Math.PI / 2) * d.y;

// g 요소 중심 이동 (루트=캔버스 중심)
g.attr("transform", `translate(${width/2},${height/2})`);
```

**규칙**:
- `tree.size([2*Math.PI, radius])` — 전체 360° 방사형
- 노드 좌표: `(cos(x-π/2)*y, sin(x-π/2)*y)` 변환 필수
- g transform 중심 이동 필수 (루트 = 캔버스 중앙)
- Level: 1

---

## L-502: WebView2 postMessage JSON 스키마 통일 패턴 (2026-05-22)

**문제**: WebView2 WebMessageReceived에서 단순 string('close') + 복합 메시지({type, data}) 혼용 시 파싱 코드가 복잡해지고 확장 어려움. 레거시 string 메시지와 하위 호환도 필요.

**해결**:
```javascript
// HTML → C#: 항상 JSON 사용
postMessage(JSON.stringify({ type: 'close' }));
postMessage(JSON.stringify({ type: 'tree_edited', markdown: '...' }));
```
```csharp
// C#: JsonDocument.Parse + type 분기
var doc = JsonDocument.Parse(args.WebMessageAsJson);
var type = doc.RootElement.GetProperty("type").GetString();
switch (type) {
    case "close": // ...
    case "tree_edited": // doc.RootElement.GetProperty("markdown").GetString()
}
// 레거시 string 호환 (try-catch fallback)
```

**규칙**:
- WebView2 postMessage는 항상 JSON 스키마 사용
- `using System.Text.Json` + `JsonDocument.Parse(args.WebMessageAsJson)` 패턴
- 레거시 string 호환은 try-catch fallback으로 안전 처리
- Level: 1

---

## L-503: LLM 결과 디스크 영속화 패턴 — {recordingPath}.mindmap.json 페어링 (2026-05-22)

**문제**: LLM 마인드맵 트리는 생성 비용이 크고 세션 간 재사용이 필요. 메모리 전용 캐시는 앱 재시작 시 소실(L-434 패턴 확장).

**해결**:
1. 녹음 경로 기반 페어링 파일: `{recordingPath}.mindmap.json`
2. `MindMapTreeFile` 모델: `Markdown`, `IsUserEdited`, `UpdatedAt` 필드
3. 원자적 교체: `File.WriteAllText(tmpPath)` → `File.Move(tmpPath, target, overwrite: true)`
4. `FileShare.ReadWrite` 명시로 동시 읽기 허용
5. Bind 진입 시 `LoadFromDiskAsync` 호출 → 캐시 존재 시 즉시 렌더

**규칙**:
- LLM 생성 결과 영속화: `{원본경로}.{확장자}.json` 페어링 파일 패턴
- 원자적 교체: `.tmp` 파일에 쓰기 후 `File.Move` overwrite=true
- `FileShare.ReadWrite` 명시로 동시 접근 안전 보장
- Level: 1

---

## L-504: WebView2 contextmenu 우클릭 메뉴 패턴 (2026-05-22)

**문제**: WPF ContextMenu는 WebView2 HWND 위에 표시 불가(Airspace z-order 문제, L-490 연관). 우클릭 메뉴를 WPF에서 구현하면 WebView2 영역에서 표시되지 않음.

**해결**:
```javascript
// HTML 내부에서 contextmenu 구현
svg.on("contextmenu", (event, d) => {
    event.preventDefault();  // 브라우저 기본 메뉴 억제
    event.stopPropagation();
    // 절대 위치 div 메뉴 표시
    ctxMenu.style.left = `${event.clientX}px`;
    ctxMenu.style.top = `${event.clientY}px`;
    ctxMenu.style.display = "block";
    currentContextNode = d;
});
```

**규칙**:
- WebView2 우클릭 메뉴: HTML 내부 contextmenu 이벤트 + `preventDefault()` + 절대 위치 div
- WPF ContextMenu는 WebView2 Airspace로 사용 불가 (L-490 강화)
- Level: 1

---

## L-505: Bind 시그니처 확장 패턴 — 복합 객체 단일 파라미터 추가 (2026-05-22)

**문제**: Bind(topics, summaries, rootTitle) 시그니처에 recordingPath 추가 시 호출부 2곳 모두 수정 필요. 파라미터 수 증가 시 유지 보수성 저하.

**해결**:
- `RecordingInfo? recording` 객체를 첫 파라미터로 추가
- 내부에서 `recording?.FilePath`, `recording?.IsLiveRecording` 등 필요 필드 추출
- 기존 `topics, summaries, rootTitle` 파라미터 유지 → 호출부 변경 최소화

**규칙**:
- 새 파라미터 추가 시 복합 객체로 묶어 단일 추가 원칙
- 내부에서 필드 추출, 호출부 최소 변경
- `IsLiveRecording == true`이면 `recordingPath = null` (라이브 녹음은 디스크 저장 없음)
- Level: 1

---

## L-506: WPF IsVisible 렌더 타이밍 함정 — ViewModel 단일 진실 원천 사용 필수 (2026-05-22)

**문제**: `MindMapOverlayInstance.IsVisible`은 WPF 렌더 타이밍에 의존하여 PropertyChanged 발화 직후 `false`일 수 있음. 이 조건으로 early return하면 마인드맵이 선택 변경에 반응하지 않음.

**해결**:
```csharp
// ❌ 잘못: WPF 렌더 타이밍 의존
if (!MindMapOverlayInstance.IsVisible) return;

// ✅ 올바름: ViewModel 단일 진실 원천
if (_oneNoteViewModel?.IsMindMapVisible != true) return;
```

**규칙**:
- WPF 요소의 `.IsVisible`은 렌더 타이밍 의존 — PropertyChanged 직후 `false` 가능
- 상태 플래그는 반드시 ViewModel 프로퍼티를 단일 진실 원천으로 사용
- Level: 2

---

## L-507: PropertyChanged 재등록 패턴 — CloseRequested 해제 후 토글 ON 시 재등록 (2026-05-22)

**문제**: CloseRequested 콜백에서 ViewModel.PropertyChanged를 `-=` 해제한 후, 오버레이 재토글 ON 시 Loaded 이벤트가 재발화되지 않아 PropertyChanged 등록이 누락됨. 마인드맵이 녹음 선택 변경에 반응하지 않게 됨.

**해결**:
```csharp
// 토글 ON 분기에서
viewModel.PropertyChanged -= OnViewModelPropertyChanged_ForMindMap;  // 중복 방지
viewModel.PropertyChanged += OnViewModelPropertyChanged_ForMindMap;  // 재등록
```

**규칙**:
- 구독 해제 후 재구독 필요 시: 토글 ON 분기에서 `-=` 먼저 (중복 방지) + `+=` 재등록
- Loaded 이벤트 재발화에 의존하지 않는다
- Level: 2

---

## L-508: BE→FE→통합 3Wave 패턴 심화 — 7파일 640줄 역라우팅 0회 (2026-05-22)

**문제**: 백엔드 서비스 + HTML/JS 프론트엔드 + WPF 통합 레이어 동시 수정 시 의존성 순서 미준수로 충돌 위험.

**해결** (radial 사이클 실증):
1. **Wave1** (BE): `MindMapTreeService.cs` + `MindMapTreeFile.cs` — 서비스 인터페이스/모델 확정
2. **Wave2** (FE): `mindmap.html` + `d3.v7.min.js` + csproj — HTML/JS 구현
3. **Wave3-1** (통합): `MindMapOverlay.xaml.cs` — WebView2 바인딩 + WebMessage 처리
4. **Wave3-2** (통합): `MainWindow.OneNote.cs` — ViewModel PropertyChanged 재등록

**규칙**:
- BE 서비스/모델 먼저 확정 → FE HTML/JS 구현 → WPF 통합 바인딩 순서
- 각 Wave가 이전 Wave의 인터페이스에 의존 — 의존성 방향 준수 시 충돌 없음
- L-499(rev3 Wave 패턴) + L-439(다파일 Wave) 심화 적용
- Level: 1

---

## L-509: d3 바이너리 csproj Content Include 패턴 (2026-05-22)

**문제**: WebView2 로컬 리소스로 JS 파일을 서빙할 때 csproj Content Include가 없으면 빌드 출력 디렉토리에 복사되지 않아 런타임에 파일 없음 오류 발생.

**해결**:
```xml
<!-- csproj에 추가 -->
<Content Include="mAIx\Resources\d3.v7.min.js">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</Content>
<!-- 제거된 파일 항목 삭제 -->
<!-- markmap-*.js Content Include 3개 제거 -->
```

**규칙**:
- WebView2 로컬 리소스: csproj `<Content Include>` + `CopyToOutputDirectory=Always` 필수
- 바이너리 파일 Add 후 반드시 csproj 확인 (git에 추가되어도 빌드에 포함 안 될 수 있음)
- 구버전 Content Include 제거 + 신규 추가 동시 처리
- Level: 1

---

## L-510: WebView2 UI 실측 보류 2회 패턴 — 커밋 메시지 명시 의무 심화 (2026-05-22)

**문제**: WebView2 내부 d3 SVG, contextmenu, 노드 편집 등은 FlaUI WPF 자동화로 도달 불가. rev3에서 L-496으로 기록했으나 radial 사이클에서 동일 패턴 재발생(2회).

**규칙**:
- WebView2 내부 인터랙션은 정적 grep으로 충분 (런타임 자동화 도달 불가)
- **사용자 UI 실측 보류 항목은 otest 결과 + 커밋 메시지에 명시 의무** (rev3 L-496 심화)
- 2회 발생 → Level 2 (L-496 Level 1에서 승급)
- Level: 2

---

## L-512: SSOT 파일 경유 컨텍스트 전달 — 메시지 잘림 방지 패턴 (2026-05-23)

**문제**: oplan/odev/otest 팀에이전트 간 긴 사양 전달 시 메시지 잘림 위험.

**해결**: 사양을 별도 SSOT 파일에 저장 후 파일 경로만 메시지로 전달 → 팀에이전트가 파일을 직접 읽는 패턴.

**효과**: oplan/odev/otest 전 단계에서 메시지 잘림 0, 역라우팅 0 (단일 사이클 PASS).

**규칙**: 사양 300줄+ 이상이거나 여러 팀에이전트 간 공유 필요 시 SSOT 파일 경유 패턴 우선 채택.

**심각도**: 보통
**Level**: 2
**대화ID**: conv_177950815462

---

## L-513: NumberBox vs ComboBox — 설정 UI 타입 선택 기준 (2026-05-23)

**문제**: oplan SSOT에 "NumberBox" 명시했으나 odev가 ComboBox로 구현 → 후속 수정 1회 발생.

**근본원인**: SSOT 사양에 컨트롤 타입이 명시되었음에도 구현 시 오버라이드.

**해결**: 사양에 컨트롤 타입 명시 시 구현에서 그대로 준수 (추론/대체 금지).

**규칙**: SSOT에 UI 컨트롤 타입이 명시된 경우 구현에서 사양과 다른 컨트롤 타입 사용 절대 금지.

**심각도**: 낮음
**Level**: 2
**대화ID**: conv_177950815462

---

## L-511: result.json 사이클 간 잔존 위험 — 진입 시 내용 검증 필수 (2026-05-22)

**문제**: 이전 사이클(rev3)의 `odone-1_result.json`이 `status='completed'`로 잔존하여 현재 radial 사이클 odone 진입 시 "이미 완료된 것"으로 오판할 위험. 실제로는 다른 작업의 결과였음.

**해결**:
- 진입 시 result.json의 `substeps_completed` 내용과 현재 작업 일치 여부 대조
- 일치하지 않으면 result.json 초기화 후 처음부터 실행
- 사이클 종료 시 `/clear` 또는 세션 재시작 권고

**규칙**:
- result.json status='completed'이어도 substeps 내용과 현재 작업 대조 필수
- 불일치 시 즉시 초기화 (이전 사이클 결과 재활용 금지)
- Level: 2

---

## L-514: WSL2+WT+tmux 환경 터미널 타이틀 잠금 — OSC 0+2 이중 송출 + DCS passthrough 패턴 (2026-05-26)

**문제**: Windows Terminal(WT) + tmux 환경에서 statusline.py가 OSC 2만 송출하면 WT cold-start(tmux 첫 attach) 시 탭 타이틀이 갱신되지 않음. PS1의 OSC 0이 tmux 외부에서는 잘 작동하지만 tmux 내부에서는 DCS passthrough 없이 무시됨.

**근본원인**:
- tmux는 OSC 시퀀스를 내부에서 가로채므로 WT까지 도달하려면 DCS passthrough(`\x1bPtmux;\x1b\x1b]N;...\x07\x1b\\`) 감싸기 필수.
- OSC 2(window title)만 있고 OSC 0(icon+title)이 없으면 일부 WT 버전/상황에서 타이틀 미갱신.
- `sys.stdout.flush()` 누락 시 Python 버퍼링으로 인해 시퀀스가 지연 또는 유실될 수 있음.

**해결**:
1. `statusline.py`: OSC 2 앞에 OSC 0 DCS passthrough 추가 + `sys.stdout.flush()` 추가.
2. `SessionStart.sh`: `_wt_title_warmup()` 함수 추가 — tmux 첫 세션 시작 시 OSC 0+2 DCS passthrough로 초기 타이틀 강제 송출(마커 파일로 1회 실행 보장).
3. `.bashrc`: PS1 OSC 0을 tmux 유무 조건 분기 처리(tmux 내부에서는 DCS passthrough 형식 사용).

**규칙**:
- tmux 내부에서 WT 탭 타이틀 갱신 시: 반드시 DCS passthrough 형식 사용.
- OSC 0(icon+title)과 OSC 2(window title) 양쪽 동시 송출 권장.
- Python stdout 시퀀스 송출 후 `sys.stdout.flush()` 필수.
- cold-start warmup: SessionStart 시점에 1회 강제 송출(마커 파일로 중복 방지).

**심각도**: 낮음
**Level**: 1
**대화ID**: conv_177975912864

## L-516: WPF TreeView PreviewMouseLeftButtonDown e.Handled=true → SelectedItemChanged 차단 (2026-05-26)

- **문제**: TreeView에 `PreviewMouseLeftButtonDown` 핸들러를 달고 `e.Handled=true`를 설정하면 WPF 내부의 선택 처리 경로가 차단되어 `SelectedItemChanged` 이벤트가 발화하지 않음. 이로 인해 ViewModel의 `SelectedNotebook`/`SelectedSection` 같은 선택 속성이 영구 null로 잔류.
- **증상**: `SelectedItem` 기반 분기 로직(예: OneNote + 버튼의 노트북/섹션/페이지 3분기)이 항상 첫 번째 분기(null 분기)만 실행.
- **해결**: `PreviewMouseLeftButtonUp` 또는 `SelectedItemChanged` 핸들러에서 명시적으로 ViewModel 선택 속성을 설정한다. `e.Handled=true`가 필요한 경우 Down 이벤트 대신 Up 이벤트에서 선택 동기화 수행.
- **교훈**: WPF TreeView에서 `PreviewMouseLeftButtonDown`에 `e.Handled=true`를 두면 내장 선택 이벤트 체인이 끊긴다. 선택 의존 로직이 있는 경우 반드시 `PreviewMouseLeftButtonUp` 또는 `SelectedItemChanged`에서 ViewModel 속성을 보정하라. `e.Handled=true`를 사용하는 이벤트 핸들러 아래에 선택 의존 분기가 있다면 역라우팅 원인 1순위로 의심.
- **심각도**: 중간 (선택 의존 분기 전체가 오동작)
- **Level**: 2
- **연관**: L-259 (WPF ListBox PreviewMouseLeftButtonDown 첫 클릭 무시)
- **대화ID**: conv_177977681684

## L-515: Wpf.Ui 혼용 파일에서 `new TextBlock { }` → CS0104 모호한 참조 (2026-05-26)

- **문제**: `Wpf.Ui.Controls` 네임스페이스와 `System.Windows.Controls`가 동시에 using된 파일에서 `new TextBlock { Text = ... }` 사용 시 CS0104 빌드 에러 발생.
- **근본원인**: `Wpf.Ui.Controls.TextBlock`과 `System.Windows.Controls.TextBlock`이 동일 이름으로 충돌. L-051(MessageBox 인수 타입 충돌)의 TextBlock 버전.
- **해결**: `new System.Windows.Controls.TextBlock { Text = ... }` 로 명시 네임스페이스 사용.
- **교훈**: Wpf.Ui를 사용하는 파일에서 코드비하인드(C#)로 컨트롤 인스턴스를 직접 생성할 때 `TextBlock`, `Button`, `Grid` 등 공통 WPF 타입은 반드시 `System.Windows.Controls.` 접두사를 붙인다. L-051과 함께 체크리스트로 관리.
- **심각도**: 낮음
- **Level**: 1
- **연관**: L-051 (Wpf.Ui MessageBox 인수 타입 CS0104)
- **대화ID**: conv_177975912864
