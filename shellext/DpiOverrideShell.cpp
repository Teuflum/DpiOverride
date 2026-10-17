// "DPI scaling override…" in Windows 11's compact context menu for programs and shortcuts.
//
// Windows 11 only puts entries from apps with package identity there, and they're IExplorerCommand objects. The app
// registers a sparse package for this (ShellPackage in src/Native.cs) whose manifest points the menu at this DLL.
// Explorer loads it, so the menu opens without starting anything; only a click starts DpiOverride.exe (next to this
// DLL) with the selected files, which adds and selects them (or hands them to the window that's already open).
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <new>
#include <string>

// Also in the package manifest (ShellPackage.Clsid)
// {609A4A05-67E8-4286-9B12-8C7307118B8D}
static const CLSID CLSID_DpiOverrideCommand = { 0x609a4a05, 0x67e8, 0x4286, { 0x9b, 0x12, 0x8c, 0x73, 0x07, 0x11, 0x8b, 0x8d } };

// Same as ExplorerMenu.Label for the classic menu
static const wchar_t Title[] = L"DPI scaling override…";

static HMODULE g_module;
static LONG g_objects;   // live objects and server locks, for DllCanUnloadNow

// DpiOverride.exe in this DLL's folder
static std::wstring ExePath()
{
    wchar_t buf[4096];
    DWORD n = GetModuleFileNameW(g_module, buf, ARRAYSIZE(buf));
    std::wstring p(buf, n);
    return p.substr(0, p.find_last_of(L'\\') + 1) + L"DpiOverride.exe";
}

class Command : public IExplorerCommand
{
    LONG m_refs = 1;
public:
    Command() { InterlockedIncrement(&g_objects); }
    ~Command() { InterlockedDecrement(&g_objects); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == __uuidof(IExplorerCommand))
        {
            *ppv = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&m_refs); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG r = InterlockedDecrement(&m_refs);
        if (r == 0) delete this;
        return r;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) { return SHStrDupW(Title, name); }

    // The app's own icon
    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon)
    {
        std::wstring s = ExePath() + L",0";
        return SHStrDupW(s.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* guid) { *guid = CLSID_DpiOverrideCommand; return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) { *state = ECS_ENABLED; return S_OK; }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) { *flags = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) { *e = nullptr; return E_NOTIMPL; }

    // DpiOverride.exe "<file>" "<file>" ...
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*)
    {
        std::wstring exe = ExePath();
        std::wstring cmd = L"\"" + exe + L"\"";
        DWORD count = 0;
        if (items) items->GetCount(&count);
        for (DWORD i = 0; i < count; i++)
        {
            IShellItem* item;
            if (FAILED(items->GetItemAt(i, &item))) continue;
            LPWSTR path;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
            {
                cmd += L" \"";
                cmd += path;
                cmd += L"\"";
                CoTaskMemFree(path);
            }
            item->Release();
        }
        std::wstring dir = exe.substr(0, exe.find_last_of(L'\\'));
        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi;
        if (!CreateProcessW(exe.c_str(), &cmd[0], nullptr, nullptr, FALSE, 0, nullptr, dir.c_str(), &si, &pi))
            return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return S_OK;
    }
};

class Factory : public IClassFactory
{
public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *ppv = static_cast<IClassFactory*>(this);
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() { return 2; }    // static instance
    IFACEMETHODIMP_(ULONG) Release() { return 1; }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv)
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        Command* c = new (std::nothrow) Command();
        if (!c) return E_OUTOFMEMORY;
        HRESULT hr = c->QueryInterface(riid, ppv);
        c->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock)
    {
        if (lock) InterlockedIncrement(&g_objects); else InterlockedDecrement(&g_objects);
        return S_OK;
    }
};

static Factory g_factory;

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    if (clsid != CLSID_DpiOverrideCommand) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow() { return g_objects == 0 ? S_OK : S_FALSE; }

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
