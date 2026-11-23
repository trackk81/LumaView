#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>
#include <shlguid.h>
#include <shlwapi.h>
#include <thumbcache.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdint>
#include <memory>
#include <new>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
static const CLSID providerId={0x4b64e150,0xe9b5,0x409a,{0xa8,0xf7,0x02,0xd0,0x7d,0x6d,0x1b,0xdd}};
static const CLSID modelId={0x4b64e151,0xe9b5,0x409a,{0xa8,0xf7,0x02,0xd0,0x7d,0x6d,0x1b,0xdd}};
static const CLSID gltfId={0x4b64e152,0xe9b5,0x409a,{0xa8,0xf7,0x02,0xd0,0x7d,0x6d,0x1b,0xdd}};
static const CLSID pdfId={0x4b64e153,0xe9b5,0x409a,{0xa8,0xf7,0x02,0xd0,0x7d,0x6d,0x1b,0xdd}};
enum class Kind { Image, Model, Gltf, Pdf };
static HINSTANCE module;
static LONG objects;

struct Handle {
    HANDLE h=nullptr;
    Handle()=default;
    Handle(const Handle&)=delete;
    ~Handle(){close();}
    void close(){if(h && h!=INVALID_HANDLE_VALUE)CloseHandle(h);h=nullptr;}
};
static bool transfer(HANDLE pipe,void* buffer,DWORD size,bool write){
    auto data=static_cast<BYTE*>(buffer);
    while(size){
        DWORD done=0;
        BOOL ok=write?WriteFile(pipe,data,size,&done,nullptr):ReadFile(pipe,data,size,&done,nullptr);
        if(!ok||!done)return false;
        data+=done;size-=done;
    }
    return true;
}
struct Attributes {
    std::vector<BYTE> bytes;
    LPPROC_THREAD_ATTRIBUTE_LIST list=nullptr;
    ~Attributes(){if(list)DeleteProcThreadAttributeList(list);}
    bool restrictTo(HANDLE* handles,size_t count){
        SIZE_T size=0;
        InitializeProcThreadAttributeList(nullptr,1,0,&size);
        bytes.resize(size);
        auto storage=reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(bytes.data());
        if(!InitializeProcThreadAttributeList(storage,1,0,&size))return false;
        list=storage;
        return UpdateProcThreadAttribute(list,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,handles,count*sizeof(HANDLE),nullptr,nullptr)!=FALSE;
    }
};
static void CALLBACK timeout(PTP_CALLBACK_INSTANCE,void* job,PTP_TIMER){TerminateJobObject(job,ERROR_TIMEOUT);}
struct Deadline {
    PTP_TIMER timer=nullptr;
    ~Deadline(){if(timer){SetThreadpoolTimer(timer,nullptr,0,0);WaitForThreadpoolTimerCallbacks(timer,TRUE);CloseThreadpoolTimer(timer);}}
    bool start(HANDLE job){
        timer=CreateThreadpoolTimer(timeout,job,nullptr);
        if(!timer)return false;
        LARGE_INTEGER due;due.QuadPart=-25LL*1000*10000;
        FILETIME time={due.LowPart,static_cast<DWORD>(due.HighPart)};
        SetThreadpoolTimer(timer,&time,0,0);return true;
    }
};

static HRESULT render(IStream* stream,Kind kind,const std::wstring& origin,UINT side,HBITMAP* result){
    STATSTG stat={};
    if(FAILED(stream->Stat(&stat,STATFLAG_DEFAULT))&&FAILED(stream->Stat(&stat,STATFLAG_NONAME)))return E_FAIL;
    std::unique_ptr<wchar_t,decltype(&CoTaskMemFree)> name(stat.pwcsName,CoTaskMemFree);
    std::wstring filename=name?name.get():L"";
    auto slash=filename.find_last_of(L"/\\");
    if(slash!=std::wstring::npos)filename.erase(0,slash+1);
    if(filename.size()>260)filename.clear();
    if(kind==Kind::Gltf&&!origin.empty())filename=origin;
    if(filename.size()>32767)return E_INVALIDARG;
    if(!stat.cbSize.QuadPart || stat.cbSize.HighPart>=0x80000000)return E_FAIL;

    wchar_t location[32768];
    DWORD size=GetModuleFileNameW(module,location,ARRAYSIZE(location));
    if(!size||size>=ARRAYSIZE(location))return E_FAIL;
    std::wstring directory(location,size);
    directory.resize(directory.find_last_of(L'\\'));
    bool model=kind==Kind::Model||kind==Kind::Gltf;
    std::wstring exe=directory+(model?L"\\model-converter\\DollView.Convert.exe":L"\\DollView.exe");
    const wchar_t* option=model?L" --thumbnail-stream ":kind==Kind::Pdf?L" --explorer-pdf-thumbnail ":L" --explorer-thumbnail ";
    std::wstring command=L"\""+exe+L"\""+option+std::to_wstring(side);

    SECURITY_ATTRIBUTES security={sizeof(security),nullptr,TRUE};
    Handle childIn,input,output,childOut,errors,job,process,thread;
    if(!CreatePipe(&childIn.h,&input.h,&security,0)||!CreatePipe(&output.h,&childOut.h,&security,0))return E_FAIL;
    if(!SetHandleInformation(input.h,HANDLE_FLAG_INHERIT,0)||!SetHandleInformation(output.h,HANDLE_FLAG_INHERIT,0))return E_FAIL;
    errors.h=CreateFileW(L"NUL",GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,&security,OPEN_EXISTING,0,nullptr);
    if(errors.h==INVALID_HANDLE_VALUE)return E_FAIL;
    HANDLE inherit[]={childIn.h,childOut.h,errors.h};
    Attributes attributes;
    if(!attributes.restrictTo(inherit,ARRAYSIZE(inherit)))return E_FAIL;
    STARTUPINFOEXW startup={};startup.StartupInfo.cb=sizeof(startup);
    startup.StartupInfo.dwFlags=STARTF_USESTDHANDLES;
    startup.StartupInfo.hStdInput=childIn.h;startup.StartupInfo.hStdOutput=childOut.h;startup.StartupInfo.hStdError=errors.h;
    startup.lpAttributeList=attributes.list;
    job.h=CreateJobObjectW(nullptr,nullptr);
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits={};
    limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE|JOB_OBJECT_LIMIT_PROCESS_MEMORY;
    limits.ProcessMemoryLimit=1536ULL*1024*1024;
    if(!job.h||!SetInformationJobObject(job.h,JobObjectExtendedLimitInformation,&limits,sizeof(limits)))return E_FAIL;
    Deadline deadline;
    if(!deadline.start(job.h))return E_FAIL;
    PROCESS_INFORMATION pi={};
    if(!CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,TRUE,
        EXTENDED_STARTUPINFO_PRESENT|CREATE_NO_WINDOW|CREATE_SUSPENDED,nullptr,directory.c_str(),&startup.StartupInfo,&pi))return E_FAIL;
    process.h=pi.hProcess;thread.h=pi.hThread;
    if(!AssignProcessToJobObject(job.h,process.h)){TerminateProcess(process.h,1);return E_FAIL;}
    if(ResumeThread(thread.h)==DWORD(-1))return E_FAIL;
    childIn.close();childOut.close();errors.close();
    DWORD chars=static_cast<DWORD>(filename.size());
    if(!transfer(input.h,&stat.cbSize.QuadPart,8,true)||!transfer(input.h,&chars,4,true)||
       !transfer(input.h,filename.data(),chars*sizeof(wchar_t),true))return E_FAIL;

    // Each R is a bounded read at an absolute offset. B contains a top-down premultiplied DIB.
    std::vector<BYTE> buffer(128*1024);
    for(;;){
        BYTE op;
        if(!transfer(output.h,&op,1,false))return E_FAIL;
        if(op=='R'){
            LARGE_INTEGER offset;DWORD count=0,got=0;
            if(!transfer(output.h,&offset.QuadPart,8,false)||!transfer(output.h,&count,4,false)||
               count>buffer.size()||offset.QuadPart<0||static_cast<ULONGLONG>(offset.QuadPart)>stat.cbSize.QuadPart)return E_FAIL;
            if(FAILED(stream->Seek(offset,STREAM_SEEK_SET,nullptr))||FAILED(stream->Read(buffer.data(),count,&got))||got>count)return E_FAIL;
            if(!transfer(input.h,&got,4,true)||!transfer(input.h,buffer.data(),got,true))return E_FAIL;
        }else if(op=='B'){
            DWORD w=0,h=0;
            if(!transfer(output.h,&w,4,false)||!transfer(output.h,&h,4,false)||!w||!h||w>side||h>side)return E_FAIL;
            BITMAPINFO info={};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth=w;info.bmiHeader.biHeight=-static_cast<LONG>(h);
            info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
            void* pixels=nullptr;
            HBITMAP bitmap=CreateDIBSection(nullptr,&info,DIB_RGB_COLORS,&pixels,nullptr,0);
            if(!bitmap)return E_OUTOFMEMORY;
            if(!transfer(output.h,pixels,w*h*4,false)){DeleteObject(bitmap);return E_FAIL;}
            *result=bitmap;return S_OK;
        }else return E_FAIL;
    }
}

class Provider final : public IThumbnailProvider,public IInitializeWithStream,public IInitializeWithFile,public IInitializeWithItem {
    LONG refs=1;
    Kind kind;
    std::wstring origin;
    ComPtr<IStream> source;
public:
    explicit Provider(Kind kind):kind(kind){InterlockedIncrement(&objects);}
    ~Provider(){InterlockedDecrement(&objects);}
    STDMETHODIMP QueryInterface(REFIID id,void** out) override{
        if(!out)return E_POINTER;
        *out=nullptr;
        if(id==IID_IUnknown||id==__uuidof(IThumbnailProvider))*out=static_cast<IThumbnailProvider*>(this);
        else if(id==__uuidof(IInitializeWithStream)&&kind!=Kind::Gltf)*out=static_cast<IInitializeWithStream*>(this);
        else if(id==__uuidof(IInitializeWithFile)&&kind==Kind::Gltf)*out=static_cast<IInitializeWithFile*>(this);
        else if(id==__uuidof(IInitializeWithItem)&&kind==Kind::Gltf)*out=static_cast<IInitializeWithItem*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override{return InterlockedIncrement(&refs);}
    STDMETHODIMP_(ULONG) Release() override{LONG left=InterlockedDecrement(&refs);if(!left)delete this;return left;}
    STDMETHODIMP Initialize(IStream* stream,DWORD) override{
        if(!stream)return E_INVALIDARG;
        if(source)return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        source=stream;return S_OK;
    }
    STDMETHODIMP Initialize(LPCWSTR file,DWORD) override{
        if(!file)return E_INVALIDARG;
        if(source)return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        try{origin=file;}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
        return SHCreateStreamOnFileEx(file,STGM_READ|STGM_SHARE_DENY_NONE,0,FALSE,nullptr,source.GetAddressOf());
    }
    STDMETHODIMP Initialize(IShellItem* item,DWORD mode) override{
        if(!item)return E_INVALIDARG;
        if(source)return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        PWSTR path=nullptr;
        if(SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH,&path))){
            HRESULT hr=Initialize(path,mode);CoTaskMemFree(path);return hr;
        }
        return item->BindToHandler(nullptr,BHID_Stream,IID_PPV_ARGS(source.GetAddressOf()));
    }
    STDMETHODIMP GetThumbnail(UINT side,HBITMAP* bitmap,WTS_ALPHATYPE* alpha) override{
        if(!bitmap||!alpha)return E_POINTER;
        *bitmap=nullptr;*alpha=WTSAT_UNKNOWN;
        if(!source||!side)return E_INVALIDARG;
        try{
            HRESULT hr=render(source.Get(),kind,origin,std::min(side,1024u),bitmap);
            if(SUCCEEDED(hr))*alpha=WTSAT_ARGB;
            return hr;
        }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}catch(...){return E_FAIL;}
    }
};
class Factory final : public IClassFactory {
    LONG refs=1;
    Kind kind;
public:
    explicit Factory(Kind kind):kind(kind){InterlockedIncrement(&objects);}
    ~Factory(){InterlockedDecrement(&objects);}
    STDMETHODIMP QueryInterface(REFIID id,void** out) override{
        if(!out)return E_POINTER;*out=nullptr;
        if(id!=IID_IUnknown&&id!=IID_IClassFactory)return E_NOINTERFACE;
        *out=static_cast<IClassFactory*>(this);AddRef();return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override{return InterlockedIncrement(&refs);}
    STDMETHODIMP_(ULONG) Release() override{LONG left=InterlockedDecrement(&refs);if(!left)delete this;return left;}
    STDMETHODIMP CreateInstance(IUnknown* outer,REFIID id,void** out) override{
        if(!out)return E_POINTER;*out=nullptr;if(outer)return CLASS_E_NOAGGREGATION;
        auto obj=new(std::nothrow) Provider(kind);if(!obj)return E_OUTOFMEMORY;
        HRESULT hr=obj->QueryInterface(id,out);obj->Release();return hr;
    }
    STDMETHODIMP LockServer(BOOL lock) override{if(lock)InterlockedIncrement(&objects);else InterlockedDecrement(&objects);return S_OK;}
};
STDAPI DllGetClassObject(REFCLSID cls,REFIID id,void** out){
    if(!out)return E_POINTER;*out=nullptr;
    if(cls!=providerId&&cls!=modelId&&cls!=gltfId&&cls!=pdfId)return CLASS_E_CLASSNOTAVAILABLE;
    Kind kind=cls==providerId?Kind::Image:cls==gltfId?Kind::Gltf:cls==pdfId?Kind::Pdf:Kind::Model;
    auto factory=new(std::nothrow) Factory(kind);if(!factory)return E_OUTOFMEMORY;
    HRESULT hr=factory->QueryInterface(id,out);factory->Release();return hr;
}
STDAPI DllCanUnloadNow(){return objects?S_FALSE:S_OK;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void*){
    if(reason==DLL_PROCESS_ATTACH)module=instance;
    return TRUE;
}
