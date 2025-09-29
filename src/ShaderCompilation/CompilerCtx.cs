using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.SpirvCross;
using Vortice.Vulkan;

namespace ShaderCompilation;

public enum LangKind {
    Glsl = 0,
    Hlsl = 1
}

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

public enum SpirvVersion {
    Spirv10 = 0x00010000,
    Spirv11 = 0x00010100,
    Spirv12 = 0x00010200,
    Spirv13 = 0x00010300,
    Spirv14 = 0x00010400,
    Spirv15 = 0x00010500,
    Spirv16 = 0x00010600,
}

public enum ShaderKind {
    Vertex = 0,
    Fragment = 1,
    Compute = 2,
    Geometry = 3,
    TessControl = 4,
    TessEvaluation = 5,
}

public enum IncludeType {
    Relative = 0,
    Absolute = 1
}

public unsafe struct CompilerCtx : IDisposable {
    private nint _compilerHandle;
    public Options _options;

    public CompilerCtx() {
        _compilerHandle = shaderc.shaderc_compiler_initialize();
        if (_compilerHandle == IntPtr.Zero) {
            throw new Exception("failed to initialize shaderc ctx.");
        }

        _options = new Options();
    }

    public CompileResult Compile(string source, ShaderKind kind, string inputFileName, string entryPoint) {
        var srcPtr = Marshal.StringToHGlobalAnsi(source);
        var inputPtr = Marshal.StringToHGlobalAnsi(inputFileName);
        var entryPointPtr = Marshal.StringToHGlobalAnsi(entryPoint);

        try {
            nint resultPtr = shaderc.shaderc_compile_into_spv(
                _compilerHandle,
                (byte*)srcPtr,
                (nuint)source.Length,
                (int)kind,
                (byte*)inputPtr,
                (byte*)entryPointPtr,
                _options.Handle
            );
            return new CompileResult(resultPtr);
        }
        finally {
            Marshal.FreeHGlobal(srcPtr);
            Marshal.FreeHGlobal(inputPtr);
            Marshal.FreeHGlobal(entryPointPtr);
        }
    }

    public void Dispose() {
        _options.Dispose();
        if (_compilerHandle != IntPtr.Zero)
        {
            shaderc.shaderc_compiler_release(_compilerHandle);
            _compilerHandle = IntPtr.Zero;
        }
    }

    public unsafe struct Options : IDisposable {
        public readonly nint Handle;

        private shaderc.IncludeResolveFn _includeResolveDelegate;
        private shaderc.IncludeReleaseFn _includeReleaseDelegate;

        public delegate IncludeResult IncludeHandler(string requestedSource, string requestingSource, IncludeType type);
        public IncludeHandler? CustomIncludeHandler { get; set; }

        public Options() {
            Handle = shaderc.shaderc_compile_options_initialize();
            if (Handle == IntPtr.Zero) {
                throw new Exception("failed to initialize shaderc ctx options.");
            }

            _includeResolveDelegate = IncludeResolveCallback;
            _includeReleaseDelegate = IncludeReleaseCallback;
            
            shaderc.shaderc_compile_options_set_include_callbacks(
                Handle,
                _includeResolveDelegate,
                _includeReleaseDelegate,
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

        private nint IncludeResolveCallback(nint user_data, byte* requested_source, int type, byte* requesting_source, nuint include_depth) {
            string requestedSourceStr = Marshal.PtrToStringAnsi((IntPtr)requested_source) ?? string.Empty;
            string requestingSourceStr = Marshal.PtrToStringAnsi((IntPtr)requesting_source) ?? string.Empty;
            IncludeType includeType = (IncludeType)type;

            IncludeResult managedResult;
            try {
                if (CustomIncludeHandler != null) {
                    managedResult = CustomIncludeHandler.Invoke(requestedSourceStr, requestingSourceStr, includeType);
                }
                else
                {
                    managedResult = new IncludeResult(
                        requestedSourceStr,
                        $"cannot resolve include '{requestedSourceStr}'. no include handler specified or handler failed.",
                        isError: true
                    );
                }
            }
            catch (Exception ex) {
                managedResult = new IncludeResult(
                    requestedSourceStr,
                    $"error in include handler for '{requestedSourceStr}': {ex.Message}",
                    isError: true
                );
            }

            return managedResult.Alloc();
        }

        private void IncludeReleaseCallback(nint user_data, nint include_result_ptr) {
            IncludeResult.Free(include_result_ptr);
        }
    }

    public struct IncludeResult : IDisposable {
        private IntPtr _sourceNamePtr = IntPtr.Zero;
        private IntPtr _contentPtr = IntPtr.Zero;
        private IntPtr _nativeStructPtr = IntPtr.Zero;

        public string SourceName { get; }
        public string Content { get; }
        public string ErrorMessage { get; }

        public IncludeResult(string sourceName, string content) {
            SourceName = sourceName;
            Content = content;
            ErrorMessage = string.Empty;
        }

        public IncludeResult(string sourceName, string errorMessage, bool isError = true) {
            SourceName = sourceName;
            Content = string.Empty;
            ErrorMessage = errorMessage;
        }

        internal nint Alloc() {
            Dispose();

            _sourceNamePtr = Marshal.StringToHGlobalAnsi(SourceName);
            _contentPtr = Marshal.StringToHGlobalAnsi(Content);

            var nativeResult = new shaderc.IncludeResultNative {
                source_name = _sourceNamePtr,
                source_name_length = (nuint)SourceName.Length,
                content = _contentPtr,
                content_length = (nuint)Content.Length,
                user_data = IntPtr.Zero
            };

            _nativeStructPtr = Marshal.AllocHGlobal(Marshal.SizeOf<shaderc.IncludeResultNative>());
            Marshal.StructureToPtr(nativeResult, _nativeStructPtr, false);

            return _nativeStructPtr;
        }

        internal static void Free(nint nativeResultPtr) {
            if (nativeResultPtr == IntPtr.Zero) return;

            var nativeResult = Marshal.PtrToStructure<shaderc.IncludeResultNative>(nativeResultPtr);

            if (nativeResult.source_name != IntPtr.Zero) Marshal.FreeHGlobal(nativeResult.source_name);
            if (nativeResult.content != IntPtr.Zero) Marshal.FreeHGlobal(nativeResult.content);

            Marshal.FreeHGlobal(nativeResultPtr);
        }

        public void Dispose() {
            _sourceNamePtr = IntPtr.Zero;
            _contentPtr = IntPtr.Zero;
            _nativeStructPtr = IntPtr.Zero;
        }
    }

    public readonly unsafe struct CompileResult : IDisposable {
        private readonly nint _ptr;

        internal nint Handle => _ptr;

        public readonly ulong NumWarnings => _ptr == IntPtr.Zero ? 0 : shaderc.shaderc_result_get_num_warnings(_ptr);
        public readonly ulong NumErrors => _ptr == IntPtr.Zero ? 0 : shaderc.shaderc_result_get_num_errors(_ptr);
        public readonly Status CompilationStatus => _ptr == IntPtr.Zero ? Status.InternalError : shaderc.shaderc_result_get_compilation_status(_ptr);

        public string ErrorMessage {
            get {
                if (_ptr == IntPtr.Zero) return "result is null!";
                var errorPtr = shaderc.shaderc_result_get_error_message(_ptr);
                return Marshal.PtrToStringAnsi(errorPtr) ?? string.Empty;
            }
        }
        
        public ReadOnlySpan<byte> GetSPVBytes() {
            if (_ptr == IntPtr.Zero) return ReadOnlySpan<byte>.Empty;
            nuint size = shaderc.shaderc_result_get_length(_ptr);
            if (size == 0) return ReadOnlySpan<byte>.Empty;

            void* nativeBuf = shaderc.shaderc_result_get_bytes(_ptr);
            return new ReadOnlySpan<byte>(nativeBuf, (int)size);
        }

        public string GetString() {
            var bytes = GetSPVBytes();
            if (bytes.Length == 0 && ErrorMessage == string.Empty) return string.Empty;

            if (bytes.Length > 0) return Encoding.ASCII.GetString(bytes);

            return ErrorMessage;
        }

        internal CompileResult(nint handle) {
            _ptr = handle;
        }

        public void Dispose() {
            if (_ptr != IntPtr.Zero) {
                shaderc.shaderc_result_release(_ptr);
            }
        }
    }
}