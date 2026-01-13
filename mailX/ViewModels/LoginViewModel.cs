using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using mailX.Data;
using mailX.Models;
using mailX.Utils;

namespace mailX.ViewModels;

/// <summary>
/// 로그인 화면 ViewModel - Microsoft 계정 인증 관리
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly MailXDbContext _dbContext;

    public LoginViewModel(MailXDbContext dbContext)
    {
        _dbContext = dbContext;

        // IsLoading 변경 시 CanLoginWithSaved 알림
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsLoading))
            {
                OnPropertyChanged(nameof(CanLoginWithSaved));
            }
        };
    }

    /// <summary>
    /// 저장된 계정 목록
    /// </summary>
    [ObservableProperty]
    private List<Account> _savedAccounts = new();

    /// <summary>
    /// 선택된 계정
    /// </summary>
    [ObservableProperty]
    private Account? _selectedAccount;

    /// <summary>
    /// 에러 발생 여부
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// 저장된 계정이 있는지 여부
    /// </summary>
    public bool HasSavedAccounts => SavedAccounts.Count > 0;

    /// <summary>
    /// 저장된 계정으로 로그인 가능 여부
    /// </summary>
    public bool CanLoginWithSaved => SelectedAccount != null && !IsLoading;

    /// <summary>
    /// 로그인 성공 이벤트 (View에서 DialogResult 설정용)
    /// </summary>
    public event Action? LoginSucceeded;

    /// <summary>
    /// SavedAccounts 변경 시 computed 속성 알림
    /// </summary>
    partial void OnSavedAccountsChanged(List<Account> value)
    {
        OnPropertyChanged(nameof(HasSavedAccounts));
    }

    /// <summary>
    /// SelectedAccount 변경 시 computed 속성 알림
    /// </summary>
    partial void OnSelectedAccountChanged(Account? value)
    {
        OnPropertyChanged(nameof(CanLoginWithSaved));
    }

    /// <summary>
    /// 저장된 계정 목록 로드
    /// </summary>
    public async Task LoadSavedAccountsAsync()
    {
        await ExecuteAsync(async () =>
        {
            SavedAccounts = await _dbContext.Accounts
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.LastLoginAt)
                .ToListAsync();

            // 기본 계정 자동 선택
            SelectedAccount = SavedAccounts.FirstOrDefault(a => a.IsDefault)
                ?? SavedAccounts.FirstOrDefault();
        }, "계정 목록 로드 실패");
    }

    /// <summary>
    /// 대화형 로그인 (Microsoft 로그인 창 표시)
    /// </summary>
    [RelayCommand]
    private async Task LoginAsync()
    {
        Log4.Debug("LoginAsync 시작");
        await ExecuteAsync(async () =>
        {
            Log4.Debug("ExecuteAsync 내부 시작");
            HasError = false;

            // TODO: MSAL 대화형 로그인 구현
            // var result = await _msalClient.AcquireTokenInteractive(scopes).ExecuteAsync();

            // 임시: 로그인 시뮬레이션
            Log4.Debug("로그인 시뮬레이션 시작 (1초 대기)");
            await Task.Delay(1000);
            Log4.Debug("로그인 시뮬레이션 완료");

            // 로그인 성공 시 계정 저장 및 이벤트 발생
            var account = new Account
            {
                Email = "user@example.com",
                DisplayName = "Test User",
                LastLoginAt = DateTime.Now,
                IsDefault = true
            };
            Log4.Debug($"계정 객체 생성: {account.Email}");

            // 기존 계정 확인
            Log4.Debug("DB에서 기존 계정 조회 시작");
            var existing = await _dbContext.Accounts.FindAsync(account.Email);
            Log4.Debug($"DB 조회 완료 - existing: {(existing != null ? "있음" : "없음")}");

            if (existing != null)
            {
                Log4.Debug("기존 계정 업데이트");
                existing.LastLoginAt = DateTime.Now;
                existing.DisplayName = account.DisplayName;
            }
            else
            {
                Log4.Debug("새 계정 추가");
                await _dbContext.Accounts.AddAsync(account);
            }

            Log4.Debug("DB SaveChangesAsync 시작");
            await _dbContext.SaveChangesAsync();
            Log4.Debug("DB SaveChangesAsync 완료");

            Log4.Debug("LoginSucceeded 이벤트 발생 직전");
            LoginSucceeded?.Invoke();
            Log4.Debug("LoginSucceeded 이벤트 발생 완료");

        }, "로그인 실패");

        Log4.Debug($"ExecuteAsync 완료 - ErrorMessage: {ErrorMessage ?? "(null)"}");

        if (ErrorMessage != null)
        {
            Log4.Debug("에러 발생 - HasError = true 설정");
            HasError = true;
        }

        Log4.Debug("LoginAsync 종료");
    }

    /// <summary>
    /// 저장된 계정으로 로그인
    /// </summary>
    [RelayCommand]
    private async Task LoginWithSavedAccountAsync()
    {
        if (SelectedAccount == null)
            return;

        await ExecuteAsync(async () =>
        {
            HasError = false;

            // TODO: MSAL 토큰 캐시에서 자동 로그인
            // var result = await _msalClient.AcquireTokenSilent(scopes, account).ExecuteAsync();

            // 임시: 로그인 시뮬레이션
            await Task.Delay(500);

            // 마지막 로그인 시간 업데이트
            SelectedAccount.LastLoginAt = DateTime.Now;
            await _dbContext.SaveChangesAsync();

            LoginSucceeded?.Invoke();
        }, "자동 로그인 실패");

        if (ErrorMessage != null)
        {
            HasError = true;
        }
    }
}
