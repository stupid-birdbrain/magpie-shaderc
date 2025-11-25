using System.Runtime.InteropServices;

namespace ShaderCompilation;

    public unsafe struct CompilerOptions : IDisposable {
        public readonly nint Handle;

        private shaderc.IncludeResolveFn _includeResolveDelegate;
        private shaderc.IncludeReleaseFn _includeReleaseDelegate;

        public delegate IncludeResult IncludeHandler(string requestedSource, string requestingSource, IncludeType type);
        public IncludeHandler? CustomIncludeHandler { get; set; }

        public CompilerOptions() {
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