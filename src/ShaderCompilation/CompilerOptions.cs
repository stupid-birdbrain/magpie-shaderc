using System.Runtime.InteropServices;

namespace ShaderCompilation;

public enum OptimizationLevel {
    None = 0,
    Size = 1,
    Performance = 2,
}

public enum TargetEnv {
    Vulkan = 0,
    OpenGL = 1,
    OpenGL_Compat = 2,
    D3D12 = 4,
}

/// <summary>
///     The target SPIR-V version for this compiler.
/// </summary>
public enum SpirvVersion {
    Spirv10 = 0x00010000,
    Spirv11 = 0x00010100,
    Spirv12 = 0x00010200,
    Spirv13 = 0x00010300,
    Spirv14 = 0x00010400,
    Spirv15 = 0x00010500,
    Spirv16 = 0x00010600,
}

/// <summary>
///     Defines the type of include directive.
/// </summary>
public enum IncludeType {
    Relative = 0,
    Absolute = 1
}

/// <summary>
///     Represents a shader include.
/// </summary>
/// <param name="SourceName"></param>
/// <param name="Content"></param>
/// <param name="ErrorMessage"></param>
public readonly record struct Include(string SourceName, string Content, string ErrorMessage = "") {
    public bool IsError => !string.IsNullOrEmpty(ErrorMessage);
    
    /// <summary>
    ///     Allocates native memory for shaderc include callbacks.
    /// </summary>
    internal static nint Alloc(Include include) {
        IntPtr sourceNamePtr = Marshal.StringToHGlobalAnsi(include.SourceName);
        IntPtr contentPtr = Marshal.StringToHGlobalAnsi(include.IsError ? include.ErrorMessage : include.Content);
        
        var nativeResult = new shaderc.IncludeResultNative {
            source_name = sourceNamePtr,
            source_name_length = (nuint)include.SourceName.Length,
            content = contentPtr,
            content_length = (nuint)(include.IsError ? include.ErrorMessage.Length : include.Content.Length),
            user_data = IntPtr.Zero
        };

        nint nativeStructPtr = Marshal.AllocHGlobal(Marshal.SizeOf<shaderc.IncludeResultNative>());
        Marshal.StructureToPtr(nativeResult, nativeStructPtr, false);

        return nativeStructPtr;
    }
    
    /// <summary>
    ///     Frees the native memory previously allocated by the <see cref="Alloc"/> method.
    /// </summary>
    internal static void Dealloc(nint nativeResultPtr) {
        if (nativeResultPtr == IntPtr.Zero) return;

        var nativeResult = Marshal.PtrToStructure<shaderc.IncludeResultNative>(nativeResultPtr);

        if (nativeResult.source_name != IntPtr.Zero) Marshal.FreeHGlobal(nativeResult.source_name);
        if (nativeResult.content != IntPtr.Zero) Marshal.FreeHGlobal(nativeResult.content);

        Marshal.FreeHGlobal(nativeResultPtr);
    }
}

public unsafe class CompilerOptions : IDisposable {
    public readonly nint Handle;

    public delegate Include IncludeHandler(string requestedSource, string requestingSource, IncludeType type);
    public IncludeHandler? CustomIncludeHandler { get; set; }

    public CompilerOptions() {
        Handle = shaderc.shaderc_compile_options_initialize();
        if (Handle == IntPtr.Zero) {
            throw new Exception("failed to initialize shaderc ctx options.");
        }

        shaderc.IncludeResolveFn includeResolveDelegate = IncludeResolveCallback;
        shaderc.IncludeReleaseFn includeReleaseDelegate = IncludeReleaseCallback;
            
        shaderc.shaderc_compile_options_set_include_callbacks(
            Handle,
            includeResolveDelegate,
            includeReleaseDelegate,
            IntPtr.Zero
        );
    }

    public void Dispose() {
        if (Handle != IntPtr.Zero) {
            shaderc.shaderc_compile_options_release(Handle);
        }
    }

    public void SetSourceLanguage(LangKind lang) =>
        shaderc.shaderc_compile_options_set_source_language(Handle, (int)lang);

    public void SetOptimizationLevel(OptimizationLevel level) =>
        shaderc.shaderc_compile_options_set_optimization_level(Handle, (int)level);

    public void SetTargetEnv(TargetEnv env, uint version) =>
        shaderc.shaderc_compile_options_set_target_env(Handle, (int)env, version);

    public void SetTargetSpirv(SpirvVersion version) =>
        shaderc.shaderc_compile_options_set_target_spirv(Handle, (uint)version);

    public void SetGenerateDebugInfo(bool generateDebugInfo) {
        if (generateDebugInfo)
            shaderc.shaderc_compile_options_set_generate_debug_info(Handle);
    }

    private nint IncludeResolveCallback(nint userData, byte* requestedSource, int type, byte* requestingSource, nuint includeDepth) {
        string requestedSourceStr = Marshal.PtrToStringAnsi((IntPtr)requestedSource) ?? string.Empty;
        string requestingSourceStr = Marshal.PtrToStringAnsi((IntPtr)requestingSource) ?? string.Empty;
        IncludeType includeType = (IncludeType)type;

        Include result;
        try {
            result = CustomIncludeHandler?.Invoke(requestedSourceStr, requestingSourceStr, includeType) 
                            ?? new Include(requestedSourceStr, "", $"cannot resolve include '{requestedSourceStr}'. no include handler specified or handler failed.");
        }
        catch (Exception ex) {
            result = new Include(requestedSourceStr, "", $"error in include handler for '{requestedSourceStr}': {ex.Message}");
        }

        return Include.Alloc(result);
    }

    private void IncludeReleaseCallback(nint userData, nint includeResultPtr) {
        Include.Dealloc(includeResultPtr);
    }
}