// Wysicraft app host: shows an exported Wysicraft web app (the "app" folder beside this .exe) in its own window,
// using Microsoft Edge WebView2, which comes with Windows 10 and 11. Settings come from app.ini beside the .exe:
//   title=My App   width=960   height=720   id=my_app   background=#15181D
// The page is served from https://app.wysicraft/ (a private name mapped to the app folder); nothing else loads.
// "--capture file.png" saves a screenshot two seconds after loading and exits (used by Wysicraft's tests).
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <wrl.h>
#include <string>
#include <fstream>
#include <sstream>
#include "WebView2.h"
using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;

static HWND window;
static ComPtr<ICoreWebView2Controller> controller;
static ComPtr<ICoreWebView2> webview;
static std::wstring capturePath;
static bool captured;

static std::wstring Widen(const std::string& text) {
    if (text.empty()) return L"";
    int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), (int)text.size(), nullptr, 0);
    std::wstring out(n, L'\0'); MultiByteToWideChar(CP_UTF8, 0, text.data(), (int)text.size(), out.data(), n); return out;
}
static std::wstring ExeDirectory() {
    wchar_t path[MAX_PATH * 4]; DWORD n = GetModuleFileNameW(nullptr, path, (DWORD)std::size(path));
    std::wstring p(path, n); return p.substr(0, p.find_last_of(L"\\/"));
}
struct Config { std::wstring title = L"Wysicraft app", id = L"wysicraft_app"; int width = 960, height = 720; COLORREF background = RGB(0x15, 0x18, 0x1D); };
static Config ReadConfig(const std::wstring& folder) {
    Config c; std::ifstream in(folder + L"\\app.ini", std::ios::binary); std::string line;
    while (std::getline(in, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.size() >= 3 && (unsigned char)line[0] == 0xEF) line = line.substr(3); // UTF-8 BOM
        auto eq = line.find('='); if (eq == std::string::npos) continue;
        std::string key = line.substr(0, eq), value = line.substr(eq + 1);
        if (key == "title" && !value.empty()) c.title = Widen(value);
        else if (key == "id" && !value.empty()) c.id = Widen(value);
        else if (key == "width") c.width = max(320, min(8192, atoi(value.c_str())));
        else if (key == "height") c.height = max(240, min(8192, atoi(value.c_str())));
        else if (key == "background" && value.size() == 7 && value[0] == '#') { unsigned v = strtoul(value.c_str() + 1, nullptr, 16); c.background = RGB((v >> 16) & 255, (v >> 8) & 255, v & 255); }
    }
    // The id names this app's private browser data folder, so keep it to safe characters.
    for (auto& ch : c.id) if (!iswalnum(ch) && ch != L'_' && ch != L'-') ch = L'_';
    return c;
}
static void Fail(const wchar_t* what, HRESULT hr) {
    std::wstringstream text;
    text << what << L" (error 0x" << std::hex << (unsigned)hr << L").\n\nThis app needs the Microsoft Edge WebView2 Runtime, which is part of Windows 10 and 11. "
         << L"Open the download page now?";
    if (MessageBoxW(window, text.str().c_str(), L"Can't start", MB_ICONERROR | MB_YESNO) == IDYES)
        ShellExecuteW(nullptr, L"open", L"https://go.microsoft.com/fwlink/p/?LinkId=2124703", nullptr, nullptr, SW_SHOWNORMAL);
    PostQuitMessage(1);
}
static void Resize() { if (!controller) return; RECT r; GetClientRect(window, &r); controller->put_Bounds(r); }

static void Capture() {
    if (!webview || captured) return; captured = true;
    ComPtr<IStream> stream;
    if (FAILED(SHCreateStreamOnFileEx(capturePath.c_str(), STGM_CREATE | STGM_WRITE, FILE_ATTRIBUTE_NORMAL, TRUE, nullptr, &stream))) { PostQuitMessage(2); return; }
    webview->CapturePreview(COREWEBVIEW2_CAPTURE_PREVIEW_IMAGE_FORMAT_PNG, stream.Get(),
        Callback<ICoreWebView2CapturePreviewCompletedHandler>([stream](HRESULT hr) -> HRESULT { stream->Commit(STGC_DEFAULT); PostQuitMessage(SUCCEEDED(hr) ? 0 : 3); return S_OK; }).Get());
}

static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_SIZE: Resize(); return 0;
        case WM_SETFOCUS: if (controller) controller->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC); return 0;
        case WM_DPICHANGED: { auto* r = (RECT*)lParam; SetWindowPos(hwnd, nullptr, r->left, r->top, r->right - r->left, r->bottom - r->top, SWP_NOZORDER | SWP_NOACTIVATE); return 0; }
        case WM_TIMER: KillTimer(hwnd, wParam); Capture(); return 0;
        case WM_DESTROY: controller = nullptr; webview = nullptr; PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

static void Start(const std::wstring& appFolder, const Config& config) {
    wchar_t* local = nullptr; SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local);
    std::wstring data = std::wstring(local ? local : L".") + L"\\" + config.id + L"\\WebView2"; CoTaskMemFree(local);
    HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, data.c_str(), nullptr,
        Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>([appFolder, config](HRESULT result, ICoreWebView2Environment* env) -> HRESULT {
            if (FAILED(result) || !env) { Fail(L"WebView2 could not start", result); return S_OK; }
            return env->CreateCoreWebView2Controller(window, Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                [appFolder, config](HRESULT result, ICoreWebView2Controller* created) -> HRESULT {
                    if (FAILED(result) || !created) { Fail(L"WebView2 could not open a view", result); return S_OK; }
                    controller = created; controller->get_CoreWebView2(&webview);
                    ComPtr<ICoreWebView2Controller2> controller2;
                    if (SUCCEEDED(controller.As(&controller2))) controller2->put_DefaultBackgroundColor({ 255, GetRValue(config.background), GetGValue(config.background), GetBValue(config.background) });
                    ComPtr<ICoreWebView2Settings> settings; webview->get_Settings(&settings);
                    settings->put_AreDevToolsEnabled(FALSE); settings->put_AreDefaultContextMenusEnabled(FALSE);
                    settings->put_IsStatusBarEnabled(FALSE); settings->put_IsZoomControlEnabled(FALSE);
                    ComPtr<ICoreWebView2Settings3> settings3; if (SUCCEEDED(settings.As(&settings3))) settings3->put_AreBrowserAcceleratorKeysEnabled(FALSE);
                    ComPtr<ICoreWebView2_3> webview3;
                    if (FAILED(webview.As(&webview3)) || FAILED(webview3->SetVirtualHostNameToFolderMapping(L"app.wysicraft", appFolder.c_str(), COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_DENY))) { Fail(L"This WebView2 version is too old", E_NOINTERFACE); return S_OK; }
                    // The app only ever shows its own pages: other navigation and pop-ups are blocked.
                    webview->add_NavigationStarting(Callback<ICoreWebView2NavigationStartingEventHandler>([](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
                        LPWSTR uri = nullptr; args->get_Uri(&uri);
                        if (!uri || wcsncmp(uri, L"https://app.wysicraft/", 22) != 0) args->put_Cancel(TRUE);
                        CoTaskMemFree(uri); return S_OK; }).Get(), nullptr);
                    webview->add_NewWindowRequested(Callback<ICoreWebView2NewWindowRequestedEventHandler>([](ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs* args) -> HRESULT { args->put_Handled(TRUE); return S_OK; }).Get(), nullptr);
                    webview->add_DocumentTitleChanged(Callback<ICoreWebView2DocumentTitleChangedEventHandler>([](ICoreWebView2* sender, IUnknown*) -> HRESULT {
                        LPWSTR title = nullptr; sender->get_DocumentTitle(&title); if (title && *title) SetWindowTextW(window, title); CoTaskMemFree(title); return S_OK; }).Get(), nullptr);
                    webview->add_WindowCloseRequested(Callback<ICoreWebView2WindowCloseRequestedEventHandler>([](ICoreWebView2*, IUnknown*) -> HRESULT { DestroyWindow(window); return S_OK; }).Get(), nullptr);
                    if (!capturePath.empty())
                        webview->add_NavigationCompleted(Callback<ICoreWebView2NavigationCompletedEventHandler>([](ICoreWebView2*, ICoreWebView2NavigationCompletedEventArgs*) -> HRESULT { SetTimer(window, 1, 2000, nullptr); return S_OK; }).Get(), nullptr);
                    Resize(); controller->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
                    webview->Navigate(L"https://app.wysicraft/index.html");
                    return S_OK;
                }).Get());
        }).Get());
    if (FAILED(hr)) Fail(L"WebView2 is not installed", hr);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int show) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 1;
    int argc = 0; LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    for (int i = 1; i + 1 < argc; i++) if (wcscmp(argv[i], L"--capture") == 0) capturePath = argv[i + 1];
    LocalFree(argv);
    std::wstring folder = ExeDirectory(), appFolder = folder + L"\\app";
    Config config = ReadConfig(folder);
    if (GetFileAttributesW((appFolder + L"\\index.html").c_str()) == INVALID_FILE_ATTRIBUTES) {
        MessageBoxW(nullptr, L"The \"app\" folder is missing. Keep it next to this program.", config.title.c_str(), MB_ICONERROR); return 1;
    }
    WNDCLASSEXW wc{ sizeof(wc) }; wc.lpfnWndProc = WindowProc; wc.hInstance = instance; wc.lpszClassName = L"WysicraftAppHost";
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW); wc.hbrBackground = CreateSolidBrush(config.background);
    wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1)); if (!wc.hIcon) wc.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
    RegisterClassExW(&wc);
    // Size the window so its inside is width × height in display-independent pixels.
    UINT dpi = GetDpiForSystem(); RECT r{ 0, 0, MulDiv(config.width, dpi, 96), MulDiv(config.height, dpi, 96) };
    AdjustWindowRectExForDpi(&r, WS_OVERLAPPEDWINDOW, FALSE, 0, dpi);
    window = CreateWindowExW(0, wc.lpszClassName, config.title.c_str(), WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, r.right - r.left, r.bottom - r.top, nullptr, nullptr, instance, nullptr);
    if (!window) return 1;
    ShowWindow(window, capturePath.empty() ? show : SW_SHOWNOACTIVATE); UpdateWindow(window);
    Start(appFolder, config);
    MSG msg; while (GetMessageW(&msg, nullptr, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    CoUninitialize(); return (int)msg.wParam;
}
