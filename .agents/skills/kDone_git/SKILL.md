---
name: kDone_git
description: "Git 커밋 & 푸시. 한국어+이모지 커밋 메시지 생성."
---
# kDone_git — 커밋 & 푸시

## 커밋 절차

```yaml
1_환경설정:
  set GIT_TERMINAL_PROMPT=0

2_스테이징:
  권장: git add 특정파일1 특정파일2 (이 세션 파일만)
  주의: git add .는 모든 세션 변경사항 포함
  확인: git status로 스테이징 확인

  gitignore_체크 (필수 — git add 전):
    절차: git check-ignore {파일} 로 각 파일 검사
    매칭_시: 해당 파일 스킵 (커밋 대상에서 제외)
    금지: git add -f 절대 금지 (gitignore 우회 = 추적 오염)
    교훈: L-031 — git add가 조용히 무시되면 -f가 아닌 gitignore 확인이 정답

3_커밋:
  git -c core.editor=true commit -F .commit_message.txt

4_푸시:
  절차: kInfra_{project} 프로젝트스킬의 Git Push 설정 참조
  주의: WSL 환경 등 플랫폼별 제약 사항은 프로젝트스킬에 명시
```

## 커밋 메시지 형식

```yaml
본문: 한국어 + 이모지
footer: |
  🤖 Generated with [Claude Code](https://claude.com/claude-code) (claude-opus-4-6)

  Co-Authored-By: Claude <noreply@anthropic.com>
주의: 모델 버전은 실제 사용 중인 모델로 업데이트
```

## 세션 임시파일 삭제 (git push 전 — 필수)

> 커밋 완료 후, 푸시 전에 세션 중 생성된 임시파일을 모두 삭제.

```yaml
시점: 3_커밋 완료 후, 4_푸시 직전
목적: 세션 임시 상태가 다음 세션에 영향 주지 않도록 정리

삭제_대상:
  에러_로그: /tmp/claude_errors_*.md
  파이프라인_상태: /tmp/claude_pipeline_state
  분류_상태: /tmp/claude_task_classification
  kO_활성화: /tmp/claude_ko_activated
  팀_생성_플래그: /tmp/claude_team_created
  Gate_검증_리포트: /tmp/claude_gate_verify_report.json
  테스트_증거 (기본 4개 + run 세부 3개):
    - /tmp/claude_test_build_ok
    - /tmp/claude_test_deploy_ok
    - /tmp/claude_test_run_ok
    - /tmp/claude_test_quality_ok
    - /tmp/claude_test_run_restapi_ok
    - /tmp/claude_test_run_screenshot_ok
    - /tmp/claude_test_run_log_ok
  Review_JSON: /tmp/claude_review_actions.json
  Watchdog_알림: /tmp/claude_watchdog_alert
  커밋_메시지: /tmp/dotfiles_commit_msg.txt, /tmp/ai_commit_msg.txt
  TODO_파일: {프로젝트루트}/TODO_*.md

삭제_명령:
  rm -f /tmp/claude_errors_*.md
  rm -f /tmp/claude_pipeline_state /tmp/claude_task_classification
  rm -f /tmp/claude_ko_activated /tmp/claude_team_created
  rm -f /tmp/claude_gate_verify_report.json /tmp/claude_review_actions.json
  rm -f /tmp/claude_test_build_ok /tmp/claude_test_deploy_ok /tmp/claude_test_run_ok /tmp/claude_test_quality_ok
  rm -f /tmp/claude_test_run_restapi_ok /tmp/claude_test_run_screenshot_ok /tmp/claude_test_run_log_ok
  rm -f /tmp/claude_watchdog_alert
  rm -f /tmp/dotfiles_commit_msg.txt /tmp/ai_commit_msg.txt

주의:
  - kDone_review Step 0.5에서 에러 파일을 이미 읽고 JSON으로 변환 완료한 상태
  - 삭제 순서: 커밋 → 임시파일 삭제 → 푸시 (커밋에는 영향 없음)
  - TODO_*.md는 .gitignore에 포함되어 있으므로 커밋과 무관
```

## dotfiles 자동 커밋/push

> 프로젝트 커밋 완료 후, dotfiles repo 변경사항도 자동 커밋/push.

```yaml
시점: 프로젝트 git push 완료 직후 (4_푸시 이후)
조건: ~/dotfiles/ 디렉토리 존재 AND git status에 변경사항 있음

5_dotfiles_커밋:
  감지: git -C ~/dotfiles status --porcelain
  변경_없음: 스킵 (출력 없이 통과)
  변경_있음:
    1. git -C ~/dotfiles add -A
    2. 커밋 메시지 자동 생성:
       형식: "🔧 update: {변경 파일 요약}"
       예시: "🔧 update: statusline.py, tmux.conf 설정 변경"
       footer: 프로젝트 커밋과 동일 (Claude Code + Co-Authored-By)
    3. git -C ~/dotfiles commit -F /tmp/dotfiles_commit_msg.txt
    4. git -C ~/dotfiles push
  실패_시: 경고 출력 후 계속 진행 (프로젝트 커밋은 이미 완료)
  원칙: dotfiles push 실패가 전체 파이프라인을 블로킹하지 않음
```

## AI repo 자동 커밋/push

> dotfiles 커밋 완료 후, AI repo(범용 스킬/hooks 원본) 변경사항도 자동 커밋/push.

```yaml
시점: dotfiles push 완료 직후 (5_dotfiles_커밋 이후)
경로: /mnt/c/DATA/Project/AI
조건: AI repo 디렉토리 존재 AND git status에 변경사항 있음

6_ai_repo_커밋:
  감지: git -C "/mnt/c/DATA/Project/AI" status --porcelain
  변경_없음: 스킵 (출력 없이 통과)
  변경_있음:
    1. git -C "/mnt/c/DATA/Project/AI" add -A
    2. 커밋 메시지 자동 생성:
       형식: "🔧 update: {변경 파일 요약}"
       예시: "🔧 update: kDev/SKILL.md, ko_check.sh hook 수정"
       footer: 프로젝트 커밋과 동일 (Claude Code + Co-Authored-By)
    3. git -C "/mnt/c/DATA/Project/AI" commit -F /tmp/ai_commit_msg.txt
    4. git -C "/mnt/c/DATA/Project/AI" push
  실패_시: 경고 출력 후 계속 진행 (프로젝트/dotfiles 커밋은 이미 완료)
  원칙: AI push 실패가 전체 파이프라인을 블로킹하지 않음

참고:
  - AI repo의 skills/hooks는 Mars/MaiX와 하드링크 공유
  - 어떤 프로젝트에서 수정해도 AI repo에 변경이 감지됨
  - 따라서 매 kDone_git 실행 시 AI repo 변경 여부를 항상 체크
```

## 다중 세션 주의

```yaml
충돌_방지:
  작업_전: git pull, git status 확인
  작업_중: 단일 파일 집중, 자주 커밋
  충돌_시: git stash → pull → stash pop

브랜치_분리:
  세션별: feature/task-a, feature/task-b
  확인: 커밋 전 다른 세션 작업 확인
```
