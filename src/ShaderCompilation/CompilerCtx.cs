using ShaderCompilation.Models;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.SPIRV;
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
    private spvc_context _spvc;
    
    public CompilerOptions Options;

    private spvc_compiler _compiler;
    private spvc_resources _resources;

    public CompilerCtx() {
        _compilerHandle = shaderc.shaderc_compiler_initialize();
        if (_compilerHandle == IntPtr.Zero) {
            throw new Exception("failed to initialize shaderc ctx.");
        }

        Options = new CompilerOptions();
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
                Options.Handle
            );
            return new CompileResult(resultPtr);
        }
        finally {
            Marshal.FreeHGlobal(srcPtr);
            Marshal.FreeHGlobal(inputPtr);
            Marshal.FreeHGlobal(entryPointPtr);
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
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLES, false).CheckResult();
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLVulkanSemantics, true).CheckResult();
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLEmitUniformBufferAsPlainUniforms, false).CheckResult();
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
            
            List<ReflectedSampler> samplers = new();

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.UniformBuffer, &resourceListPtr, &resourceCount).CheckResult();
            data.UniformBuffers = (uint)resourceCount;

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, ResourceType.SampledImage, &resourceListPtr, &resourceCount).CheckResult();
            for (int i = 0; i < (int)resourceCount; i++) {
                var resource = resourceListPtr[i];
                var name = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed";
                uint set = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);
                uint binding = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
                
                samplers.Add(new ReflectedSampler(name, binding, set, ShaderDataType.Sampler2D));
            }
            data.ReadSamplers = samplers;

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
    }

    void ReadSamplers(List<ReflectedSampler> samplers) {
        
    }
    
    public void Dispose() {
        Options.Dispose();
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
    
    private VkShaderStageFlags ConvertSpvExecutionModelToVkShaderStageFlags(SpvExecutionModel model) {
        return model switch {
            SpvExecutionModel.Vertex => VkShaderStageFlags.Vertex,
            SpvExecutionModel.Fragment => VkShaderStageFlags.Fragment,
            SpvExecutionModel.GLCompute => VkShaderStageFlags.Compute,
            SpvExecutionModel.Geometry => VkShaderStageFlags.Geometry,
            SpvExecutionModel.TessellationControl => VkShaderStageFlags.TessellationControl,
            SpvExecutionModel.TessellationEvaluation => VkShaderStageFlags.TessellationEvaluation,
            _ => VkShaderStageFlags.None,
        };
    }
}