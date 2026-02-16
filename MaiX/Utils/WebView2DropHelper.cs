using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using MaiX.Services.Editor;
namespace MaiX.Utils
{
    /// <summary>
    /// WebView2의 Chrome_WidgetWin hwnd에 커스텀 OLE IDropTarget을 등록하여
    /// 비이미지 파일 드래그&드롭 시 file:/// 링크를 TinyMCE에 삽입합니다.
    /// </summary>
    public static class WebView2DropHelper
    {
        #region Win32 P/Invoke

        [DllImport("ole32.dll")]
        private static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr lParam);

        // HRESULT 상수
        private const int S_OK = 0;
        private const int DRAGDROP_E_ALREADYREGISTERED = unchecked((int)0x80040101);

        // CF_HDROP
        private const short CF_HDROP = 15;

        // DROPEFFECT
        private const int DROPEFFECT_NONE = 0;
        private const int DROPEFFECT_COPY = 1;

        #endregion

        // GC 방지: DropTargetWrapper 인스턴스를 static으로 보관
        private static readonly Dictionary<IntPtr, DropTargetWrapper> _registeredTargets = new();

        /// <summary>
        /// WebView2 컨트롤에 커스텀 드래그&드롭 핸들러를 등록합니다.
        /// CoreWebView2 초기화 완료 후 호출해야 합니다.
        /// </summary>
        public static void RegisterDropHandler(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return;

            // WebView2 컨트롤의 윈도우 핸들 가져오기
            var hwndSource = PresentationSource.FromVisual(webView) as HwndSource;
            if (hwndSource == null) return;

            var parentHwnd = hwndSource.Handle;

            // Chrome_WidgetWin_1 자식 윈도우 찾기 (Chromium 렌더링 hwnd)
            IntPtr chromeHwnd = FindChromeWidgetWin(parentHwnd);
            if (chromeHwnd == IntPtr.Zero)
            {
                Log4.Debug("[WebView2DropHelper] Chrome_WidgetWin 찾기 실패");
                return;
            }

            Log4.Debug($"[WebView2DropHelper] Chrome_WidgetWin hwnd: 0x{chromeHwnd:X8}");

            // 기존 IDropTarget 해제 (WebView2 기본 등록 제거)
            int revokeHr = RevokeDragDrop(chromeHwnd);
            Log4.Debug($"[WebView2DropHelper] RevokeDragDrop 결과: 0x{revokeHr:X8}");

            // 커스텀 IDropTarget 등록 (static Dictionary에 보관하여 GC 방지)
            var dropTarget = new DropTargetWrapper(webView);
            int hr = RegisterDragDrop(chromeHwnd, dropTarget);

            if (hr == S_OK)
            {
                _registeredTargets[chromeHwnd] = dropTarget;
                Log4.Debug("[WebView2DropHelper] 커스텀 IDropTarget 등록 성공");
            }
            else
            {
                Log4.Debug($"[WebView2DropHelper] RegisterDragDrop 실패: 0x{hr:X8}");
            }
        }

        /// <summary>
        /// 부모 윈도우에서 Chrome_WidgetWin_1 또는 Chrome_WidgetWin_0 자식 hwnd를 찾습니다.
        /// </summary>
        private static IntPtr FindChromeWidgetWin(IntPtr parentHwnd)
        {
            IntPtr found = IntPtr.Zero;
            var className = new StringBuilder(256);

            EnumChildWindows(parentHwnd, (hwnd, lParam) =>
            {
                GetClassName(hwnd, className, className.Capacity);
                var name = className.ToString();
                if (name.StartsWith("Chrome_WidgetWin_"))
                {
                    found = hwnd;
                    return false; // 탐색 중지
                }

                // 재귀 탐색: 더 깊은 자식에 있을 수 있음
                IntPtr deeper = IntPtr.Zero;
                EnumChildWindows(hwnd, (childHwnd, childLParam) =>
                {
                    GetClassName(childHwnd, className, className.Capacity);
                    if (className.ToString().StartsWith("Chrome_WidgetWin_"))
                    {
                        deeper = childHwnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (deeper != IntPtr.Zero)
                {
                    found = deeper;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        /// <summary>
        /// OLE ComIDataObject에서 CF_HDROP 형식의 파일 경로 목록을 추출합니다.
        /// </summary>
        private static string[]? ExtractFilePaths(ComIDataObject dataObject)
        {
            var format = new FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            };

            try
            {
                dataObject.GetData(ref format, out STGMEDIUM medium);

                try
                {
                    uint fileCount = DragQueryFile(medium.unionmember, 0xFFFFFFFF, null, 0);
                    if (fileCount == 0) return null;

                    var files = new string[fileCount];
                    for (uint i = 0; i < fileCount; i++)
                    {
                        uint size = DragQueryFile(medium.unionmember, i, null, 0);
                        var sb = new StringBuilder((int)size + 1);
                        DragQueryFile(medium.unionmember, i, sb, (uint)sb.Capacity);
                        files[i] = sb.ToString();
                    }

                    return files;
                }
                finally
                {
                    if (medium.unionmember != IntPtr.Zero)
                        Marshal.FreeHGlobal(medium.unionmember);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 파일 경로 배열에 CF_HDROP 형식이 포함되어 있는지 확인합니다.
        /// </summary>
        private static bool HasFileDrop(ComIDataObject dataObject)
        {
            var format = new FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            };

            try
            {
                return dataObject.QueryGetData(ref format) == S_OK;
            }
            catch
            {
                return false;
            }
        }

        #region IDropTarget 구현

        [ComVisible(true)]
        private class DropTargetWrapper : IDropTarget
        {
            private readonly WebView2 _webView;

            public DropTargetWrapper(WebView2 webView)
            {
                _webView = webView;
            }

            public int DragEnter(ComIDataObject pDataObj, int grfKeyState, POINTL pt, ref int pdwEffect)
            {
                bool hasFiles = HasFileDrop(pDataObj);
                Log4.Debug($"[WebView2DropHelper] DragEnter — 파일 드롭: {hasFiles}");
                pdwEffect = hasFiles ? DROPEFFECT_COPY : DROPEFFECT_NONE;

                return S_OK;
            }

            public int DragOver(int grfKeyState, POINTL pt, ref int pdwEffect)
            {
                pdwEffect = DROPEFFECT_COPY;
                return S_OK;
            }

            public int DragLeave()
            {
                return S_OK;
            }

            public int Drop(ComIDataObject pDataObj, int grfKeyState, POINTL pt, ref int pdwEffect)
            {
                pdwEffect = DROPEFFECT_COPY;

                var files = ExtractFilePaths(pDataObj);
                Log4.Debug($"[WebView2DropHelper] Drop 호출됨 — 파일 수: {files?.Length ?? 0}");
                if (files == null || files.Length == 0) return S_OK;

                // UI 스레드에서 비동기 실행
                _webView.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView.CoreWebView2 == null) return;

                    foreach (var filePath in files)
                    {
                        if (!File.Exists(filePath)) continue;

                        var fileName = Path.GetFileName(filePath);

                        if (TinyMCEEditorService.IsImageFile(filePath))
                        {
                            // 이미지 → Base64 data URL로 삽입
                            var dataUrl = TinyMCEEditorService.ConvertFileToDataUrl(filePath);
                            if (dataUrl == null) continue;

                            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(dataUrl);
                            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                            await _webView.CoreWebView2.ExecuteScriptAsync(
                                $"window.insertDroppedImage({escapedUrl}, {escapedName})");
                        }
                        else
                        {
                            // 비이미지 → file:/// 링크로 삽입
                            var fileUrl = "file:///" + filePath.Replace("\\", "/");
                            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
                            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                            await _webView.CoreWebView2.ExecuteScriptAsync(
                                $"window.insertDroppedFileLink({escapedUrl}, {escapedName})");
                        }
                    }
                });

                return S_OK;
            }
        }

        #endregion
    }

    #region COM IDropTarget 인터페이스

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig]
        int DragEnter([In, MarshalAs(UnmanagedType.Interface)] ComIDataObject pDataObj, [In] int grfKeyState, [In] POINTL pt, [In, Out] ref int pdwEffect);

        [PreserveSig]
        int DragOver([In] int grfKeyState, [In] POINTL pt, [In, Out] ref int pdwEffect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] ComIDataObject pDataObj, [In] int grfKeyState, [In] POINTL pt, [In, Out] ref int pdwEffect);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    #endregion
}
