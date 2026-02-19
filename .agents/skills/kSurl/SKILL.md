# kSurl — 최신 스크린샷 조회

Windows 스크린샷 폴더에서 가장 최근 파일을 찾아 경로를 표시하고 클립보드에 복사.

## 사용법

```
/kSurl
```

## 동작 절차

1. Glob 도구로 최신 PNG 파일 탐색:
   ```
   Glob: pattern="*.png" path="/mnt/c/Users/rioky/OneDrive - (주)다이퀘스트/그림/스크린샷"
   ```
   - Glob 결과는 수정시간 순 → **마지막 항목이 최신**

2. 파일이 있으면:
   - WSL 경로와 Windows 경로 모두 표시
   - Windows 경로를 클립보드에 자동 복사 (한글 깨짐 방지):
     ```bash
     powershell.exe -NoProfile -Command "Set-Clipboard -Value 'Windows_경로'"
     ```
   - clip.exe 사용 금지 (UTF-8 한글 깨짐)
   - "클립보드에 복사됨" 메시지 표시

3. 파일이 없으면:
   - "스크린샷 없음" 메시지 표시

## 경로 변환 규칙

```yaml
WSL: /mnt/c/Users/rioky/OneDrive - (주)다이퀘스트/그림/스크린샷/파일명.png
Windows: C:\Users\rioky\OneDrive - (주)다이퀘스트\그림\스크린샷\파일명.png
변환: /mnt/c/ → C:\ + / → \
```

## 스크린샷 경로

```yaml
폴더: /mnt/c/Users/rioky/OneDrive - (주)다이퀘스트/그림/스크린샷/
파일명_패턴: 스크린샷 YYYY-MM-DD HHMMSS.png
도구: Glob (Bash ls 금지 — 경로에 괄호 포함되어 bash 파싱 오류 발생)
```
