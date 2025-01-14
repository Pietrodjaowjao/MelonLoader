#include "Il2Cpp.h"
#include "Game.h"
#include "../Utils/Assertion.h"
#include "../Utils/AssemblyGenerator.h"
#include "Hook.h"
#include "Mono.h"
#include "../Utils/Console/Console.h"
#include "../Utils/Console/Debug.h"
#include <string>
#include "AssemblyVerifier.h"
#include "InternalCalls.h"
#include "BaseAssembly.h"
#include "BHapticsBridge.h"
#include "../Utils/Console/Logger.h"

#ifdef __ANDROID__
#include <dlfcn.h>
#include <android/log.h>
#endif
#include "../Utils/Helpers/ImportLibHelper.h"

Il2Cpp::Domain* Il2Cpp::domain = NULL;
char* Il2Cpp::GameAssemblyPath = NULL;
void* Il2Cpp::UnityTLSInterfaceStruct = NULL;

Il2Cpp::Exports::il2cpp_init_t Il2Cpp::Exports::il2cpp_init = NULL;
Il2Cpp::Exports::il2cpp_runtime_invoke_t Il2Cpp::Exports::il2cpp_runtime_invoke = NULL;
Il2Cpp::Exports::il2cpp_method_get_name_t Il2Cpp::Exports::il2cpp_method_get_name = NULL;
Il2Cpp::Exports::il2cpp_unity_install_unitytls_interface_t Il2Cpp::Exports::il2cpp_unity_install_unitytls_interface = NULL;
il2cpp_thread_get_all_attached_threads_t Il2Cpp::Exports::il2cpp_thread_get_all_attached_threads = NULL;
il2cpp_thread_attach_t Il2Cpp::Exports::il2cpp_thread_attach = NULL;
il2cpp_thread_detach_t Il2Cpp::Exports::il2cpp_thread_detach = NULL;
il2cpp_gc_set_mode_t Il2Cpp::Exports::il2cpp_gc_set_mode = NULL;


#ifdef _WIN32
HMODULE Il2Cpp::Module = NULL;

bool Il2Cpp::Initialize()
{
	if (!Game::IsIl2Cpp)
		return true;
	Debug::Msg("Initializing Il2Cpp...");
	Debug::Msg(("Il2Cpp::GameAssemblyPath = " + std::string(GameAssemblyPath)).c_str());
	Module = LoadLibraryA(GameAssemblyPath);
	if (Module == NULL)
	{
		Assertion::ThrowInternalFailure("Failed to Load GameAssembly!");
		return false;
	}
	return Exports::Initialize();
}

bool Il2Cpp::Exports::Initialize()
{
	Debug::Msg("Initializing Il2Cpp Exports...");
	il2cpp_init = (il2cpp_init_t)Assertion::GetExport(Module, "il2cpp_init");
	il2cpp_runtime_invoke = (il2cpp_runtime_invoke_t)Assertion::GetExport(Module, "il2cpp_runtime_invoke");
	il2cpp_method_get_name = (il2cpp_method_get_name_t)Assertion::GetExport(Module, "il2cpp_method_get_name");
	if (!Mono::IsOldMono)
		il2cpp_unity_install_unitytls_interface = (il2cpp_unity_install_unitytls_interface_t)Assertion::GetExport(Module, "il2cpp_unity_install_unitytls_interface");
	return Assertion::ShouldContinue;
}

Il2Cpp::Domain* Il2Cpp::Hooks::il2cpp_init(const char* name)
{
	if (!Debug::Enabled)
		Console::SetHandles();
	Debug::Msg("Detaching Hook from il2cpp_init...");
	Hook::Detach(&(LPVOID&)Exports::il2cpp_init, il2cpp_init);
	if (AssemblyGenerator::Initialize())
	{
		Mono::CreateDomain(name);
		InternalCalls::Initialize();
		// todo: check if it works/is necessary on mono games
		AssemblyVerifier::InstallHooks();
		if (BaseAssembly::Initialize())
		{
			Debug::Msg("Attaching Hook to il2cpp_runtime_invoke...");
			Hook::Attach(&(LPVOID&)Exports::il2cpp_runtime_invoke, il2cpp_runtime_invoke);
		}
	}
	Debug::Msg("Creating Il2Cpp Domain...");
	domain = Exports::il2cpp_init(name);
	return domain;
}

Il2Cpp::Object* Il2Cpp::Hooks::il2cpp_runtime_invoke(Method* method, Object* obj, void** params, Object** exec)
{
	const char* method_name = Exports::il2cpp_method_get_name(method);
	if (strstr(method_name, "Internal_ActiveSceneChanged") != NULL)
	{
		Debug::Msg("Detaching Hook from il2cpp_runtime_invoke...");
		Hook::Detach(&(LPVOID&)Exports::il2cpp_runtime_invoke, il2cpp_runtime_invoke);
		BaseAssembly::Start();
	}
	return Exports::il2cpp_runtime_invoke(method, obj, params, exec);
}

void Il2Cpp::Hooks::il2cpp_unity_install_unitytls_interface(void* unitytlsInterfaceStruct)
{
	Exports::il2cpp_unity_install_unitytls_interface(unitytlsInterfaceStruct);
	UnityTLSInterfaceStruct = unitytlsInterfaceStruct;
}
#elif defined(__ANDROID__)
void* Il2Cpp::Handle = NULL;
void* Il2Cpp::MemLoc = NULL;
const char* Il2Cpp::LibPath = NULL;
int Il2Cpp::SceneChanges = 0;

bool Il2Cpp::Initialize()
{
	Debug::Msg("Initializing Il2Cpp...");
	Handle = dlopen("libil2cpp.so", RTLD_NOW | RTLD_GLOBAL | RTLD_GLOBAL);

	if (Handle == nullptr)
	{
		// TODO: ASSERT ERROR
		Logger::Error(dlerror());
		return false;
	}

	Debug::Msg("Loaded Il2Cpp");
	//__android_log_print(ANDROID_LOG_INFO, "MelonLoader", "%p", Il2Cpp::Handle);

	return Exports::Initialize();
}

bool Il2Cpp::Exports::Initialize()
{
	Debug::Msg("Initializing Il2Cpp Exports...");
	
	il2cpp_init = (il2cpp_init_t)ImportLibHelper::GetExport(Handle, "il2cpp_init");
	il2cpp_runtime_invoke = (il2cpp_runtime_invoke_t)ImportLibHelper::GetExport(Handle, "il2cpp_runtime_invoke");
	il2cpp_method_get_name = (il2cpp_method_get_name_t)ImportLibHelper::GetExport(Handle, "il2cpp_method_get_name");
	il2cpp_unity_install_unitytls_interface = (il2cpp_unity_install_unitytls_interface_t)ImportLibHelper::GetExport(Handle, "il2cpp_unity_install_unitytls_interface");

    il2cpp_thread_get_all_attached_threads = (il2cpp_thread_get_all_attached_threads_t)ImportLibHelper::GetExport(Handle, "il2cpp_thread_get_all_attached_threads");
    il2cpp_thread_attach = (il2cpp_thread_attach_t)ImportLibHelper::GetExport(Handle, "il2cpp_thread_attach");
    il2cpp_thread_detach = (il2cpp_thread_detach_t)ImportLibHelper::GetExport(Handle, "il2cpp_thread_detach");
//    il2cpp_gc_set_mode = (il2cpp_gc_set_mode_t)ImportLibHelper::GetExport(Handle, "il2cpp_gc_set_mode");

	Dl_info dlInfo;
	dladdr((void*)il2cpp_init, &dlInfo);
	MemLoc = dlInfo.dli_fbase;
	LibPath = dlInfo.dli_fname;

	Dl_info dlInfo1;
	dladdr((void*)il2cpp_runtime_invoke, &dlInfo1);
	
	if (MemLoc != dlInfo1.dli_fbase)
		Assertion::ThrowInternalFailure("Address mismatch");

	if (!Assertion::ShouldContinue)
	{
		Logger::Error("One or more symbols failed to load.");
	}

	return Assertion::ShouldContinue;
}

bool Il2Cpp::ApplyPatches()
{
	Debug::Msg("Applying patches for Il2CPP");

	Hook::Attach((void**)&Exports::il2cpp_init, (void*)Hooks::il2cpp_init);

//    Debug::Msg("Attaching Hook to il2cpp_start_gc_world...");
//    Hook::Attach((void**)&Exports::il2cpp_gc_set_mode, (void*)Hooks::on_il2cpp_gc_set_mode);

//    Hook::Attach((void**)&Exports::il2cpp_thread_attach, (void*)Hooks::on_il2cpp_thread_attach);
//    Hook::Attach((void**)&Exports::il2cpp_thread_detach, (void*)Hooks::on_il2cpp_thread_detach);
#ifdef _WIN32
	Hook::Attach((void**)&Exports::il2cpp_unity_install_unitytls_interface, (void*)Hooks::il2cpp_unity_install_unitytls_interface);
#endif

	return true;
}

void Il2Cpp::OnIl2cppReady() {
//    std::thread t(MonoThreadHandle);
//    Debug::Msg("starting thread");
//    t.detach();
//    MonoThreadHandle();
    if (BaseAssembly::PreStart())
        BaseAssembly::Start();
}

void Il2Cpp::MonoThreadHandle() {
    Mono::CreateDomain("Mono Domain");

//    BaseAssembly::LoadAssembly();
    InternalCalls::Initialize();
    // todo: check if it works/is necessary on mono games
//    AssemblyVerifier::InstallHooks();
    std::this_thread::sleep_for (std::chrono::milliseconds (500));
    BaseAssembly::LoadAssembly();

    if (!BaseAssembly::Initialize())
    {
        Debug::Msg("Base assembly failed to setup.");
        return;
    }

//    if (BaseAssembly::PreStart())
//        BaseAssembly::Start();
}

#pragma region Hooks
Il2Cpp::Domain* Il2Cpp::Hooks::il2cpp_init(const char* name)
{
#ifdef _WIN32
	 if (!Debug::Enabled)
		 Console::SetHandles();
#endif

    Debug::Msgf("domain: %s", name);

    if (!Mono::CheckPaths())
	{
		Logger::Error("Skipping initialization of MelonLoader");
		return NULL;
	}

//    MonoThreadHandle();

	Debug::Msg("Attaching Hook to il2cpp_runtime_invoke...");
	Hook::Attach((void**)&Exports::il2cpp_runtime_invoke, (void*)Hooks::il2cpp_runtime_invoke);

//    Mono::CreateDomain("IL2CPP Root Domain");
//    Mono::Exports::mono_thread_set_main(Mono::Exports::mono_thread_current());


    domain = Exports::il2cpp_init(name);

//    if (!BHapticsBridge::UIRunner::InitLooper())
//        goto exit_early;

    MonoThreadHandle();

//    Mono::Exports::mono_melonloader_thread_suspend_reload();

    exit_early:

	Debug::Msg("Detaching Hook from il2cpp_init...");
	Hook::Detach((void**)&Exports::il2cpp_init, (void*)Hooks::il2cpp_init);

	return domain;
}

Il2Cpp::Object* Il2Cpp::Hooks::il2cpp_runtime_invoke(Method* method, Object* obj, void** params, Object** exec)
{
    const char* method_name = Exports::il2cpp_method_get_name(method);

    auto sceneChange = strstr(method_name, "Internal_ActiveSceneChanged") != NULL;
//    auto sceneChange = strstr(method_name, "Update") != NULL;

    if (sceneChange)
        SceneChanges++;

    // mono cannot be initialized on the first scene change on android
    // otherwise it breaks GC
    if (sceneChange && SceneChanges >= 2)
//    if (sceneChange)
    {
        Debug::Msg("Detaching Hook from il2cpp_runtime_invoke...");
        Hook::Detach((void**)&(Exports::il2cpp_runtime_invoke), (void*)il2cpp_runtime_invoke);

//        MonoThreadHandle();
        Mono::Exports::mono_melonloader_thread_suspend_reload();
        OnIl2cppReady();
    }

    return Exports::il2cpp_runtime_invoke(method, obj, params, exec);
}

void Il2Cpp::Hooks::il2cpp_unity_install_unitytls_interface(void* unitytlsInterfaceStruct)
{
	Debug::Msg("Unity TLS");
	UnityTLSInterfaceStruct = unitytlsInterfaceStruct;
	return Exports::il2cpp_unity_install_unitytls_interface(unitytlsInterfaceStruct);
}

Il2CppThread *Il2Cpp::Hooks::on_il2cpp_thread_attach(Il2CppDomain *domain) {
    Debug::Msgf("Il2cpp Attach %p", pthread_self());

    Mono::Exports::mono_thread_attach(Mono::domain);

    return Exports::il2cpp_thread_attach(domain);
}

void Il2Cpp::Hooks::on_il2cpp_thread_detach(Il2CppThread *thread) {
//    Debug::Msgf("Il2cpp detach %p", pthread_self());

//    Mono::Exports::mono(Mono::domain);

    Exports::il2cpp_thread_detach(thread);

    Debug::Msgf("Il2cpp detach %p", pthread_self());
}

void Il2Cpp::Hooks::on_il2cpp_gc_set_mode(Il2CppGCMode mode) {
    Exports::il2cpp_gc_set_mode(mode);

    Debug::Msgf("GC Started");
}

#pragma endregion Hooks

#endif
