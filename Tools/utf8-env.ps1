# UTF-8 콘솔/파이프라인 설정 (Windows PowerShell 호환)
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
try { chcp 65001 > $null } catch {}

# 기본 인코딩 기본값(가능한 경우)
try { $script:PSDefaultParameterValues['*:Encoding'] = 'utf8' } catch {}

Write-Host "콘솔 및 파이프라인을 UTF-8로 설정했습니다." -ForegroundColor Green

