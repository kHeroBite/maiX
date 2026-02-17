# LESSONS.md — MaiX 프로젝트 교훈 로그

## L-045: AI 프롬프트 negative examples 필수 (2026-02-17)

- **문제**: OneNote AI 분석에서 마커 카테고리명(★중요★, ⚠주의⚠) 도배 — 3회 반복 지적
- **근본원인**: 프롬프트에 올바른 예시만 제공, 금지 패턴(negative examples) 미명시 → AI가 카테고리명을 마커 안에 삽입
- **해결**: 프롬프트에 "잘못된 예시 (절대 금지)" 섹션 추가 + C# 렌더링에서 마커 기호 제거
- **교훈**: AI 프롬프트 작성 시 올바른 예시 + 금지 예시(negative examples) 반드시 함께 제공해야 준수율 향상
- **심각도**: 높음 (3회 반복)
- **수정 파일**: Resources/Prompts/*.txt 3개, MainWindow.xaml.cs
