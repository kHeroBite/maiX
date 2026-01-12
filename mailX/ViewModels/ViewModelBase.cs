using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace mailX.ViewModels;

/// <summary>
/// ViewModel 기본 클래스 - 공통 로딩/에러 처리 제공
/// </summary>
public partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// 로딩 중 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    /// <summary>
    /// 로딩 중이 아닌지 여부 (버튼 활성화용)
    /// </summary>
    public bool IsNotLoading => !IsLoading;

    /// <summary>
    /// 에러 메시지 (null이면 에러 없음)
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 비동기 작업 실행 (로딩/에러 자동 처리)
    /// </summary>
    /// <param name="action">실행할 비동기 작업</param>
    /// <param name="errorPrefix">에러 발생 시 접두사 메시지</param>
    protected async Task ExecuteAsync(Func<Task> action, string? errorPrefix = null)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = string.IsNullOrEmpty(errorPrefix)
                ? ex.Message
                : $"{errorPrefix}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 비동기 작업 실행 (반환값 있는 버전)
    /// </summary>
    /// <typeparam name="T">반환 타입</typeparam>
    /// <param name="action">실행할 비동기 작업</param>
    /// <param name="errorPrefix">에러 발생 시 접두사 메시지</param>
    /// <returns>작업 결과 (에러 시 default)</returns>
    protected async Task<T?> ExecuteAsync<T>(Func<Task<T>> action, string? errorPrefix = null)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            return await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = string.IsNullOrEmpty(errorPrefix)
                ? ex.Message
                : $"{errorPrefix}: {ex.Message}";
            return default;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
