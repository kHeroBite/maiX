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
