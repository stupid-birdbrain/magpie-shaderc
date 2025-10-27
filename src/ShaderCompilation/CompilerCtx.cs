using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.SpirvCross;

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
    private spvc_context _spvc;

    public CompilerCtx() {
        _compilerHandle = shaderc.shaderc_compiler_initialize();
        if (_compilerHandle == IntPtr.Zero) {
            throw new Exception("failed to initialize shaderc ctx.");
        }

        _options = new Options();
        SpirvCrossApi.spvc_context_create(out _spvc).CheckResult();
        SpirvCrossApi.spvc_context_set_error_callback(_spvc, &SpirvCrossErrorCallback, 0);
    }
    

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SpirvCrossErrorCallback(nint userData, sbyte* error) {
        throw new Exception($"SPIRV-Cross error: {new string(error)}");
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
        if (_spvc != IntPtr.Zero)
        {
            SpirvCrossApi.spvc_context_destroy(_spvc);
            _spvc = IntPtr.Zero;
        }
    }
    
    public ReflectedShaderData AttemptSpvReflect(byte[] spirvBytes, Backend backend) {
        spvc_context context = default;

        try {
            SpirvCrossApi.spvc_context_create(out context).CheckResult();
            SpirvCrossApi.spvc_context_set_error_callback(context, &SpirvCrossErrorCallback, IntPtr.Zero);

            spvc_parsed_ir parsedIr;
            fixed (byte* spirvPtr = spirvBytes) {
                if (spirvBytes.Length % sizeof(uint) != 0) {
                    throw new Exception("spvc bytecode length is not a multiple of 4 bytes (uint).");
                }
                nuint wordCount = (nuint)spirvBytes.Length / sizeof(uint);
                SpirvCrossApi.spvc_context_parse_spirv(context, (uint*)spirvPtr, wordCount, out parsedIr).CheckResult();
            }

            SpirvCrossApi.spvc_context_create_compiler(context, backend, parsedIr, CaptureMode.TakeOwnership, out spvc_compiler compiler).CheckResult();
            SpirvCrossApi.spvc_compiler_create_compiler_options(compiler, out spvc_compiler_options options).CheckResult();

            if (backend == Backend.GLSL) {
                SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.GLSLVersion, 460).CheckResult();
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLES, SpvcBool.False).CheckResult();
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLVulkanSemantics, SpvcBool.True).CheckResult();
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLEmitUniformBufferAsPlainUniforms, SpvcBool.False).CheckResult();
            }
            else if (backend == Backend.HLSL) {
                SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.HLSLShaderModel, 61).CheckResult();
            }

            SpirvCrossApi.spvc_compiler_install_compiler_options(compiler, options).CheckResult();

            byte* codePtr;
            SpirvCrossApi.spvc_compiler_compile(compiler, &codePtr).CheckResult();
            string reflectedCode = Marshal.PtrToStringAnsi((IntPtr)codePtr) ?? string.Empty;

            ReflectedShaderData data = new() { Code = spirvBytes, ReflectedCode = reflectedCode };

            spvc_entry_point* entryPointsPtr;
            nuint numEntryPoints;
            SpirvCrossApi.spvc_compiler_get_entry_points(compiler, &entryPointsPtr, &numEntryPoints).CheckResult();
            if (numEntryPoints > 0) {
                data.EntryPoint = Marshal.PtrToStringAnsi((IntPtr)entryPointsPtr[0].name) ?? "unknown";
            }
            else {
                data.EntryPoint = "none";
            }

            SpirvCrossApi.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();

            spvc_reflected_resource* resourceListPtr;
            nuint resourceCount;

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.UniformBuffer, &resourceListPtr, &resourceCount).CheckResult();
            data.UniformBuffers = (uint)resourceCount;

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.SampledImage, &resourceListPtr, &resourceCount).CheckResult();
            data.Samplers = (uint)resourceCount;
            
            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.SeparateSamplers, &resourceListPtr, &resourceCount).CheckResult();
            data.Samplers += (uint)resourceCount;

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.StorageBuffer, &resourceListPtr, &resourceCount).CheckResult();
            data.StorageBuffers = (uint)resourceCount;

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.StorageImage, &resourceListPtr, &resourceCount).CheckResult();
            data.StorageImages = (uint)resourceCount;

            return data;
        }
        catch (Exception e) {
            string errorMessage = $"spv reflection failed: {e.Message}";
            var lastError = SpirvCrossApi.spvc_context_get_last_error_string(context);
            if (!string.IsNullOrEmpty(lastError)) {
                errorMessage += $"\n spv last error: {lastError}";
            }
            throw new Exception(errorMessage, e);
        }
        finally {
            // if (!context.IsNull) {
            //     SpirvCrossApi.spvc_context_release_allocations(context);
            //     SpirvCrossApi.spvc_context_destroy(context);
            // }
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

        private nint IncludeResolveCallback(nint userData, byte* requestedSource, int type, byte* requestingSource, nuint includeDepth) {
            string requestedSourceStr = Marshal.PtrToStringAnsi((IntPtr)requestedSource) ?? string.Empty;
            string requestingSourceStr = Marshal.PtrToStringAnsi((IntPtr)requestingSource) ?? string.Empty;
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

        private void IncludeReleaseCallback(nint userData, nint includeResultPtr) {
            IncludeResult.Free(includeResultPtr);
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
        
        public ReadOnlySpan<byte> GetSpvBytes() {
            if (_ptr == IntPtr.Zero) return ReadOnlySpan<byte>.Empty;
            nuint size = shaderc.shaderc_result_get_length(_ptr);
            if (size == 0) return ReadOnlySpan<byte>.Empty;

            void* nativeBuf = shaderc.shaderc_result_get_bytes(_ptr);
            return new ReadOnlySpan<byte>(nativeBuf, (int)size);
        }
        
        public byte[] GetBytesCopy() {
            var span = GetSpvBytes();
            if (span.IsEmpty) return Array.Empty<byte>();
            return span.ToArray();
        }

        public string GetString() {
            var bytes = GetSpvBytes();
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