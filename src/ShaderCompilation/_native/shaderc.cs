using System.Runtime.InteropServices;

namespace ShaderCompilation;

#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
// ReSharper disable once InconsistentNaming
internal unsafe partial struct shaderc {
    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compiler_initialize")]
    internal static partial nint shaderc_compiler_initialize();

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compiler_release")]
    internal static partial void shaderc_compiler_release(nint compiler);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_into_spv")]
    internal static partial nint shaderc_compile_into_spv(
        nint compiler,
        byte* sourceText,
        nuint sourceTextSize,
        int shaderKind,
        byte* inputFileName,
        byte* entryPointName,
        nint additionalOptions
    );

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_get_length")]
    internal static partial nuint shaderc_result_get_length(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_get_num_warnings")]
    internal static partial ulong shaderc_result_get_num_warnings(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_get_num_errors")]
    internal static partial ulong shaderc_result_get_num_errors(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_get_compilation_status")]
    internal static partial Status shaderc_result_get_compilation_status(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_get_bytes")]
    internal static partial void* shaderc_result_get_bytes(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_get_error_message")]
    internal static partial nint shaderc_result_get_error_message(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_result_release")]
    internal static partial void shaderc_result_release(nint result);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_initialize")]
    internal static partial nint shaderc_compile_options_initialize();

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_release")]
    internal static partial void shaderc_compile_options_release(nint options);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_set_source_language")]
    internal static partial void shaderc_compile_options_set_source_language(nint options, int lang);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_set_optimization_level")]
    internal static partial void shaderc_compile_options_set_optimization_level(nint options, int level);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_set_target_env")]
    internal static partial void shaderc_compile_options_set_target_env(nint options, int env, uint version);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_set_target_spirv")]
    internal static partial void shaderc_compile_options_set_target_spirv(nint options, uint version);

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_set_generate_debug_info")]
    internal static partial void shaderc_compile_options_set_generate_debug_info(nint options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint IncludeResolveFn(
        nint user_data,
        byte* requested_source,
        int type,
        byte* requesting_source,
        nuint include_depth
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void IncludeReleaseFn(
        nint user_data,
        nint include_result_ptr
    );

    [LibraryImport("shaderc_shared", EntryPoint = "shaderc_compile_options_set_include_callbacks")]
    internal static partial void shaderc_compile_options_set_include_callbacks(
        nint options,
        [MarshalAs(UnmanagedType.FunctionPtr)] IncludeResolveFn resolver,
        [MarshalAs(UnmanagedType.FunctionPtr)] IncludeReleaseFn releaser,
        nint user_data
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct IncludeResultNative {
        public IntPtr source_name;
        public nuint source_name_length;
        public IntPtr content;
        public nuint content_length;
        public IntPtr user_data;
    }
}

public enum Status {
    Success = 0,
    InvalidStage = 1,
    CompilationError = 2,
    InternalError = 3,
    NullResultObject = 4,
    InvalidAssembly = 5,
    ValidationError = 6,
    TransformationError = 7,
    ConfigurationError = 8
}
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.