using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.Services;

/// <summary>
/// 대용량 파일 청크 업로드 서비스
/// GraphOneDriveService.UploadLargeFileAsync를 래핑하여 진행률/취소 관리
/// </summary>
public class ChunkedUploadService
{
    private static readonly ILogger _log = Log.ForContext<ChunkedUploadService>();
    private readonly GraphOneDriveService _oneDriveService;

    /// <summary>
    /// 현재 업로드 진행률 (0~100)
    /// </summary>
    public double CurrentProgress { get; private set; }

    /// <summary>
    /// 업로드 중 여부
    /// </summary>
    public bool IsUploading { get; private set; }

    /// <summary>
    /// 현재 업로드 파일 이름
    /// </summary>
    public string? CurrentFileName { get; private set; }

    /// <summary>
    /// 진행률 변경 이벤트
    /// </summary>
    public event EventHandler<double>? ProgressChanged;

    /// <summary>
    /// 업로드 완료 이벤트
    /// </summary>
    public event EventHandler<string>? UploadCompleted;

    /// <summary>
    /// 업로드 실패 이벤트
    /// </summary>
    public event EventHandler<string>? UploadFailed;

    public ChunkedUploadService(GraphOneDriveService oneDriveService)
    {
        _oneDriveService = oneDriveService ?? throw new ArgumentNullException(nameof(oneDriveService));
    }

    /// <summary>
    /// 대용량 파일 업로드 (5MB 청크)
    /// </summary>
    public async Task<bool> UploadAsync(string filePath, string? parentFolderId, CancellationToken ct = default)
    {
        if (IsUploading)
        {
            _log.Warning("이미 업로드가 진행 중입니다.");
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        CurrentFileName = fileName;
        IsUploading = true;
        CurrentProgress = 0;

        try
        {
            _log.Information("청크 업로드 시작: {FileName}", fileName);

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                _log.Error("파일을 찾을 수 없습니다: {FilePath}", filePath);
                UploadFailed?.Invoke(this, $"파일을 찾을 수 없습니다: {fileName}");
                return false;
            }

            var progress = new Progress<double>(p =>
            {
                CurrentProgress = p;
                ProgressChanged?.Invoke(this, p);
            });

            await using var stream = File.OpenRead(filePath);

            // 5MB 미만이면 소규모 업로드 사용
            if (fileInfo.Length < 5 * 1024 * 1024)
            {
                var result = await _oneDriveService.UploadSmallFileAsync(parentFolderId, fileName, stream);
                if (result != null)
                {
                    CurrentProgress = 100;
                    ProgressChanged?.Invoke(this, 100);
                    UploadCompleted?.Invoke(this, fileName);
                    _log.Information("소규모 파일 업로드 완료: {FileName}", fileName);
                    return true;
                }
                UploadFailed?.Invoke(this, $"업로드 실패: {fileName}");
                return false;
            }

            // 대용량 업로드
            var item = await _oneDriveService.UploadLargeFileAsync(stream, parentFolderId, fileName, progress, ct);
            CurrentProgress = 100;
            ProgressChanged?.Invoke(this, 100);
            UploadCompleted?.Invoke(this, fileName);
            _log.Information("대용량 파일 업로드 완료: {FileName}", fileName);
            return true;
        }
        catch (OperationCanceledException)
        {
            _log.Warning("업로드 취소됨: {FileName}", fileName);
            UploadFailed?.Invoke(this, $"업로드가 취소되었습니다: {fileName}");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "업로드 실패: {FileName}", fileName);
            UploadFailed?.Invoke(this, $"업로드 실패: {ex.Message}");
            return false;
        }
        finally
        {
            IsUploading = false;
            CurrentFileName = null;
        }
    }
}
