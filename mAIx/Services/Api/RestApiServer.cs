/*
 * ==========================================
 * MaiX REST API 서버 - MARS RestApiServer 이식
 * ==========================================
 *
 * HttpListener 기반 REST API 서버입니다.
 * 테스트 자동화 및 외부 제어를 위한 API를 제공합니다.
 *
 * 기본 포트: 5858
 * 지원 API:
 * - GET  /api/health - 헬스 체크
 * - GET  /api/status - 앱 상태 조회
 * - POST /api/shutdown - 앱 종료
 * - POST /api/shutdown/force - 강제 종료
 * - POST /api/screenshot - 스크린샷 촬영
 * - GET  /api/logs/latest - 최신 로그 조회
 * - GET  /api/logs/search - 로그 검색
 * - POST /api/window/maximize - 창 최대화
 * - POST /api/window/minimize - 창 최소화
 * - POST /api/window/restore - 창 복원
 * - POST /api/window/activate - 창 활성화
 * - POST /api/navigate/{tab} - 탭 전환 (mail, calendar, chat, teams, planner, onedrive, onenote, activity, calls)
 * - GET  /api/current-tab - 현재 탭 조회
 */

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaiX.Utils;

#nullable enable

namespace MaiX.Services.Api;

/// <summary>
/// REST API 서버 클래스
/// </summary>
public class RestApiServer
{
    private int _port;
    private HttpListener? _listener;
    private Thread? _listenerThread;
    private bool _isRunning;
    private readonly Application _app;

    /// <summary>
    /// 현재 사용 중인 포트
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// 서버 실행 중 여부
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 메인 윈도우 참조 (스크린샷용)
    /// </summary>
    public Window? MainWindow { get; set; }

    /// <summary>
    /// 현재 활성화된 탭
    /// </summary>
    public string CurrentTab { get; set; } = "mail";

    /// <summary>
    /// 탭 전환 요청 이벤트
    /// </summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>
    /// Planner 플랜 선택 요청 이벤트
    /// </summary>
    public event EventHandler<int>? PlannerPlanSelectRequested;

    /// <summary>
    /// OneDrive 네비게이션 요청 이벤트 (뷰 전환)
    /// </summary>
    public event EventHandler<string>? OneDriveNavigateRequested;

    /// <summary>
    /// 지원하는 탭 목록
    /// </summary>
    private static readonly string[] ValidTabs = new[]
    {
        "mail", "calendar", "chat", "teams", "planner", "onedrive", "onenote", "activity", "calls"
    };

    public RestApiServer(int port = 5858)
    {
        _port = port;
        _app = Application.Current;
    }

    /// <summary>
    /// REST API 서버 시작
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            Log4.Warn("[RestAPI] 서버가 이미 실행 중입니다.");
            return;
        }

        int maxRetries = 10;
        int currentPort = _port;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{currentPort}/");
                _listener.Start();
                _port = currentPort;
                _isRunning = true;

                _listenerThread = new Thread(ListenForRequests)
                {
                    IsBackground = true,
                    Name = "RestAPI-Listener"
                };
                _listenerThread.Start();

                Log4.Info($"[RestAPI] 서버 시작 완료 - http://localhost:{_port}/");
                return;
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 48 || ex.Message.Contains("conflicts") || ex.Message.Contains("사용"))
            {
                Log4.Debug($"[RestAPI] 포트 {currentPort} 사용 중, 다음 포트 시도: {currentPort + 1}");
                currentPort++;
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Log4.Error($"[RestAPI] 서버 시작 실패: {ex.Message}");
                _listener?.Close();
                throw;
            }
        }

        Log4.Error($"[RestAPI] 포트 {_port}~{currentPort - 1} 모두 사용 중 - 서버 시작 실패");
    }

    /// <summary>
    /// REST API 서버 종료
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _listener?.Stop();
        _listener?.Close();
        Log4.Info("[RestAPI] 서버 종료 완료");
    }

    /// <summary>
    /// HTTP 요청 대기 루프
    /// </summary>
    private void ListenForRequests()
    {
        while (_isRunning && _listener != null)
        {
            try
            {
                var context = _listener.GetContext();
                Task.Run(() => HandleRequest(context));
            }
            catch (HttpListenerException)
            {
                if (!_isRunning) break;
            }
            catch (Exception ex)
            {
                Log4.Error($"[RestAPI] 요청 처리 오류: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// HTTP 요청 처리 및 라우팅
    /// </summary>
    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            Log4.Debug($"[RestAPI] {request.HttpMethod} {request.Url?.PathAndQuery}");

            // CORS 헤더 추가
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            // OPTIONS 요청 처리 (CORS preflight)
            if (request.HttpMethod == "OPTIONS")
            {
                SendResponse(response, 200, new { message = "OK" });
                return;
            }

            // 라우팅 실행
            var path = request.Url?.AbsolutePath ?? "";
            var method = request.HttpMethod;

            switch ((method, path))
            {
                case ("GET", "/api/health"):
                    HandleHealth(response);
                    break;

                case ("GET", "/api/status"):
                    HandleStatus(response);
                    break;

                case ("POST", "/api/shutdown"):
                    HandleShutdown(request, response);
                    break;

                case ("POST", "/api/shutdown/force"):
                    HandleShutdownForce(response);
                    break;

                case ("POST", "/api/screenshot"):
                    HandleScreenshot(response);
                    break;

                case ("GET", "/api/logs/latest"):
                    HandleLogsLatest(request, response);
                    break;

                case ("GET", "/api/logs/search"):
                    HandleLogsSearch(request, response);
                    break;

                case ("POST", "/api/window/maximize"):
                    HandleWindowMaximize(response);
                    break;

                case ("POST", "/api/window/minimize"):
                    HandleWindowMinimize(response);
                    break;

                case ("POST", "/api/window/restore"):
                    HandleWindowRestore(response);
                    break;

                case ("POST", "/api/window/activate"):
                    HandleWindowActivate(response);
                    break;

                case ("GET", "/api/current-tab"):
                    HandleGetCurrentTab(response);
                    break;

                case ("POST", "/api/theme/toggle"):
                    HandleThemeToggle(response);
                    break;

                case ("GET", "/api/theme"):
                    HandleGetTheme(response);
                    break;

                default:
                    // 동적 경로 매칭: /api/navigate/{tab}
                    if (method == "POST" && path.StartsWith("/api/navigate/"))
                    {
                        var tab = path.Substring("/api/navigate/".Length).ToLowerInvariant();
                        HandleNavigate(tab, response);
                    }
                    // 동적 경로 매칭: /api/planner/select/{index}
                    else if (method == "POST" && path.StartsWith("/api/planner/select/"))
                    {
                        var indexStr = path.Substring("/api/planner/select/".Length);
                        if (int.TryParse(indexStr, out int index))
                        {
                            HandlePlannerSelect(index, response);
                        }
                        else
                        {
                            SendResponse(response, 400, new { error = "Invalid index", path = path });
                        }
                    }
                    // 동적 경로 매칭: /api/onedrive/{view}
                    else if (method == "POST" && path.StartsWith("/api/onedrive/"))
                    {
                        var view = path.Substring("/api/onedrive/".Length).ToLowerInvariant();
                        Log4.Info($"[RestAPI] POST /api/onedrive/{view} 라우팅 매칭됨");
                        HandleOneDriveNavigate(view, response);
                    }
                    else
                    {
                        SendResponse(response, 404, new { error = "Not Found", path = path });
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[RestAPI] 요청 처리 오류: {ex.Message}");
            SendResponse(response, 500, new { error = "Internal Server Error", message = ex.Message });
        }
    }

    #region API Handlers

    /// <summary>
    /// GET /api/health - 헬스 체크
    /// </summary>
    private void HandleHealth(HttpListenerResponse response)
    {
        SendResponse(response, 200, new
        {
            status = "healthy",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            port = _port
        });
    }

    /// <summary>
    /// GET /api/status - 앱 상태 조회
    /// </summary>
    private void HandleStatus(HttpListenerResponse response)
    {
        string? mainWindowState = null;
        bool mainWindowVisible = false;

        _app.Dispatcher.Invoke(() =>
        {
            if (MainWindow != null)
            {
                mainWindowState = MainWindow.WindowState.ToString();
                mainWindowVisible = MainWindow.IsVisible;
            }
        });

        SendResponse(response, 200, new
        {
            status = "running",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            port = _port,
            logCount = Log4.LogCount,
            debugMode = Log4.DebugMode,
            mainWindow = new
            {
                state = mainWindowState,
                visible = mainWindowVisible
            }
        });
    }

    /// <summary>
    /// POST /api/shutdown - 앱 종료
    /// </summary>
    private void HandleShutdown(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var body = ReadRequestBody(request);
            bool force = false;

            if (!string.IsNullOrEmpty(body) && body != "{}")
            {
                var data = JsonSerializer.Deserialize<JsonElement>(body);
                if (data.TryGetProperty("force", out var forceElement))
                {
                    force = forceElement.GetBoolean();
                }
            }

            Log4.Info($"[RestAPI] 종료 요청 수신 - Force: {force}");

            // 응답 먼저 전송
            SendResponse(response, 200, new { message = "Shutdown initiated", force = force });

            // UI 스레드에서 종료 실행
            _app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (force)
                {
                    Environment.Exit(0);
                }
                else
                {
                    _app.Shutdown();
                }
            }));
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/shutdown/force - 강제 종료
    /// </summary>
    private void HandleShutdownForce(HttpListenerResponse response)
    {
        try
        {
            Log4.Info("[RestAPI] 강제 종료 요청 수신");

            // 응답 먼저 전송
            SendResponse(response, 200, new { message = "Force shutdown initiated", force = true });

            // 짧은 지연 후 프로세스 강제 종료
            Task.Run(async () =>
            {
                await Task.Delay(100);
                Environment.Exit(0);
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/screenshot - 스크린샷 촬영
    /// </summary>
    private void HandleScreenshot(HttpListenerResponse response)
    {
        try
        {
            if (MainWindow == null)
            {
                SendResponse(response, 503, new { error = "메인 창이 아직 초기화되지 않았습니다." });
                return;
            }

            string? filePath = null;
            string? errorMessage = null;

            _app.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 스크린샷 저장 경로
                    var screenshotDir = Path.Combine(App.AppDataPath, "screenshots");
                    if (!Directory.Exists(screenshotDir))
                    {
                        Directory.CreateDirectory(screenshotDir);
                    }

                    var fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    filePath = Path.Combine(screenshotDir, fileName);

                    // WPF 창 스크린샷 캡처
                    var bounds = new Rect(
                        MainWindow.Left,
                        MainWindow.Top,
                        MainWindow.ActualWidth,
                        MainWindow.ActualHeight);

                    // DPI 스케일링 고려
                    var source = PresentationSource.FromVisual(MainWindow);
                    double dpiX = 96.0, dpiY = 96.0;
                    if (source?.CompositionTarget != null)
                    {
                        dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                        dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
                    }

                    int width = (int)(bounds.Width * dpiX / 96.0);
                    int height = (int)(bounds.Height * dpiY / 96.0);

                    // WPF RenderTargetBitmap 사용
                    var renderTarget = new RenderTargetBitmap(
                        width, height,
                        dpiX, dpiY,
                        PixelFormats.Pbgra32);
                    renderTarget.Render(MainWindow);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                    using var stream = new FileStream(filePath, FileMode.Create);
                    encoder.Save(stream);
                    Log4.Info($"[RestAPI] 스크린샷 저장 완료: {filePath}");
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    Log4.Error($"[RestAPI] 스크린샷 촬영 실패: {ex.Message}");
                }
            });

            if (errorMessage != null)
            {
                SendResponse(response, 500, new { error = "스크린샷 촬영 실패", message = errorMessage });
            }
            else
            {
                SendResponse(response, 200, new
                {
                    message = "Screenshot captured",
                    filePath = filePath,
                    capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "스크린샷 촬영 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/logs/latest - 최신 로그 조회
    /// </summary>
    private void HandleLogsLatest(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            // 쿼리 파라미터에서 lines 수 가져오기 (기본 50)
            int lines = 50;
            var queryString = request.QueryString;
            if (queryString["lines"] != null && int.TryParse(queryString["lines"], out var parsedLines))
            {
                lines = Math.Min(parsedLines, 500); // 최대 500줄
            }

            // 오늘 날짜 로그 파일 경로
            var logPath = Path.Combine(App.LogPath, $"{DateTime.Now:yyyyMMdd}.log");

            if (!File.Exists(logPath))
            {
                SendResponse(response, 404, new { error = "오늘 로그 파일이 없습니다.", path = logPath });
                return;
            }

            // 파일 읽기 (공유 모드로)
            string[] logLines;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                var allLines = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                logLines = allLines.Length <= lines
                    ? allLines
                    : allLines[^lines..];
            }

            SendResponse(response, 200, new
            {
                lines = logLines.Length,
                logPath = logPath,
                logs = logLines
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "로그 조회 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/logs/search - 로그 검색
    /// </summary>
    private void HandleLogsSearch(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var queryString = request.QueryString;
            var searchQuery = queryString["query"] ?? "";

            int lines = 50;
            if (queryString["lines"] != null && int.TryParse(queryString["lines"], out var parsedLines))
            {
                lines = Math.Min(parsedLines, 500);
            }

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                SendResponse(response, 400, new { error = "검색어(query)가 필요합니다." });
                return;
            }

            var logPath = Path.Combine(App.LogPath, $"{DateTime.Now:yyyyMMdd}.log");

            if (!File.Exists(logPath))
            {
                SendResponse(response, 404, new { error = "오늘 로그 파일이 없습니다.", path = logPath });
                return;
            }

            string[] matchedLines;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                var allLines = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var filtered = allLines
                    .Where(line => line.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    .TakeLast(lines)
                    .ToArray();
                matchedLines = filtered;
            }

            SendResponse(response, 200, new
            {
                query = searchQuery,
                count = matchedLines.Length,
                logPath = logPath,
                logs = matchedLines
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "로그 검색 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/window/maximize - 창 최대화
    /// </summary>
    private void HandleWindowMaximize(HttpListenerResponse response)
    {
        try
        {
            if (MainWindow == null)
            {
                SendResponse(response, 503, new { error = "메인 창이 아직 초기화되지 않았습니다." });
                return;
            }

            _app.Dispatcher.Invoke(() =>
            {
                MainWindow.WindowState = WindowState.Maximized;
            });

            SendResponse(response, 200, new
            {
                message = "Window maximized",
                state = "Maximized"
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "창 최대화 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/window/minimize - 창 최소화
    /// </summary>
    private void HandleWindowMinimize(HttpListenerResponse response)
    {
        try
        {
            if (MainWindow == null)
            {
                SendResponse(response, 503, new { error = "메인 창이 아직 초기화되지 않았습니다." });
                return;
            }

            _app.Dispatcher.Invoke(() =>
            {
                MainWindow.WindowState = WindowState.Minimized;
            });

            SendResponse(response, 200, new
            {
                message = "Window minimized",
                state = "Minimized"
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "창 최소화 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/window/restore - 창 복원
    /// </summary>
    private void HandleWindowRestore(HttpListenerResponse response)
    {
        try
        {
            if (MainWindow == null)
            {
                SendResponse(response, 503, new { error = "메인 창이 아직 초기화되지 않았습니다." });
                return;
            }

            _app.Dispatcher.Invoke(() =>
            {
                MainWindow.WindowState = WindowState.Normal;
            });

            SendResponse(response, 200, new
            {
                message = "Window restored",
                state = "Normal"
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "창 복원 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/window/activate - 창 활성화 (포그라운드로 가져오기)
    /// </summary>
    private void HandleWindowActivate(HttpListenerResponse response)
    {
        try
        {
            if (MainWindow == null)
            {
                SendResponse(response, 503, new { error = "메인 창이 아직 초기화되지 않았습니다." });
                return;
            }

            _app.Dispatcher.Invoke(() =>
            {
                // 최소화 상태면 복원
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }

                // 창을 포그라운드로 가져오기
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus();
            });

            SendResponse(response, 200, new
            {
                message = "Window activated",
                state = "Active"
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "창 활성화 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/theme/toggle - 테마 전환 (다크/라이트)
    /// </summary>
    private void HandleThemeToggle(HttpListenerResponse response)
    {
        try
        {
            _app.Dispatcher.Invoke(() =>
            {
                Services.Theme.ThemeService.Instance.ToggleTheme();
            });

            var isDark = Services.Theme.ThemeService.Instance.IsDarkMode;
            SendResponse(response, 200, new
            {
                message = "Theme toggled",
                theme = isDark ? "dark" : "light"
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "테마 전환 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/theme - 현재 테마 조회
    /// </summary>
    private void HandleGetTheme(HttpListenerResponse response)
    {
        try
        {
            var isDark = Services.Theme.ThemeService.Instance.IsDarkMode;
            SendResponse(response, 200, new
            {
                theme = isDark ? "dark" : "light",
                isDarkMode = isDark
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "테마 조회 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/navigate/{tab} - 탭 전환
    /// </summary>
    private void HandleNavigate(string tab, HttpListenerResponse response)
    {
        try
        {
            if (string.IsNullOrEmpty(tab))
            {
                SendResponse(response, 400, new { error = "탭 이름이 필요합니다.", validTabs = ValidTabs });
                return;
            }

            if (!ValidTabs.Contains(tab))
            {
                SendResponse(response, 400, new { error = $"유효하지 않은 탭: {tab}", validTabs = ValidTabs });
                return;
            }

            Log4.Info($"[RestAPI] 탭 전환 요청: {tab}");

            // UI 스레드에서 탭 전환 실행
            _app.Dispatcher.Invoke(() =>
            {
                CurrentTab = tab;
                NavigateRequested?.Invoke(this, tab);
            });

            SendResponse(response, 200, new
            {
                message = "Tab navigation successful",
                tab = tab,
                previousTab = CurrentTab
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "탭 전환 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/current-tab - 현재 탭 조회
    /// </summary>
    private void HandleGetCurrentTab(HttpListenerResponse response)
    {
        try
        {
            SendResponse(response, 200, new
            {
                tab = CurrentTab,
                validTabs = ValidTabs
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "현재 탭 조회 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/planner/select/{index} - Planner 플랜 선택
    /// </summary>
    private void HandlePlannerSelect(int index, HttpListenerResponse response)
    {
        try
        {
            Log4.Info($"[RestAPI] Planner 플랜 선택 요청: 인덱스 {index}");

            // UI 스레드에서 플랜 선택 실행
            _app.Dispatcher.Invoke(() =>
            {
                PlannerPlanSelectRequested?.Invoke(this, index);
            });

            SendResponse(response, 200, new
            {
                message = "Planner plan selection requested",
                index = index
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "플랜 선택 실패", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/onedrive/{view} - OneDrive 뷰 전환 (home, myfiles, shared, favorites, trash)
    /// </summary>
    private static readonly string[] ValidOneDriveViews = new[]
    {
        "home", "myfiles", "shared", "favorites", "trash", "people", "meetings", "media"
    };

    private void HandleOneDriveNavigate(string view, HttpListenerResponse response)
    {
        try
        {
            if (string.IsNullOrEmpty(view))
            {
                SendResponse(response, 400, new { error = "뷰 이름이 필요합니다.", validViews = ValidOneDriveViews });
                return;
            }

            if (!ValidOneDriveViews.Contains(view))
            {
                SendResponse(response, 400, new { error = $"유효하지 않은 뷰: {view}", validViews = ValidOneDriveViews });
                return;
            }

            Log4.Info($"[RestAPI] OneDrive 뷰 전환 요청: {view}");

            // UI 스레드에서 뷰 전환 실행
            Log4.Debug($"[RestAPI] OneDrive 이벤트 발생 시도 - 핸들러 등록 여부: {OneDriveNavigateRequested != null}");
            _app.Dispatcher.Invoke(() =>
            {
                Log4.Debug($"[RestAPI] Dispatcher 내부 - 이벤트 호출 시작");
                OneDriveNavigateRequested?.Invoke(this, view);
                Log4.Debug($"[RestAPI] Dispatcher 내부 - 이벤트 호출 완료");
            });

            SendResponse(response, 200, new
            {
                message = "OneDrive view navigation requested",
                view = view
            });
        }
        catch (Exception ex)
        {
            SendResponse(response, 500, new { error = "OneDrive 뷰 전환 실패", message = ex.Message });
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// HTTP 요청 Body 읽기
    /// </summary>
    private string ReadRequestBody(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
            return "{}";

        var encoding = request.ContentEncoding ?? Encoding.UTF8;
        using var reader = new StreamReader(request.InputStream, encoding);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// JSON 응답 전송
    /// </summary>
    private void SendResponse(HttpListenerResponse response, int statusCode, object data)
    {
        try
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Log4.Error($"[RestAPI] 응답 전송 오류: {ex.Message}");
        }
    }

    #endregion
}
