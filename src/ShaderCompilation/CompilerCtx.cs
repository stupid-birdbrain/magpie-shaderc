using ShaderCompilation.Models;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.SPIRV;
using Vortice.SpirvCross;
using Vortice.Vulkan;

using SPV = Vortice.SpirvCross.SpirvCrossApi;

namespace ShaderCompilation;

public enum LangKind {
    Glsl = 0,
    Hlsl = 1
}

public enum ShaderKind {
    Vertex = 0,
    Fragment = 1,
    Compute = 2,
    Geometry = 3,
    TessControl = 4,
    TessEvaluation = 5,
}

public unsafe class CompilerCtx : IDisposable {
    private nint _compilerHandle;
    private spvc_context _spvc;
    
    public CompilerOptions Options;

    private readonly Backend _backend;

    public CompilerCtx(Backend backend) {
        _compilerHandle = shaderc.shaderc_compiler_initialize();
        if (_compilerHandle == IntPtr.Zero) {
            throw new Exception("failed to initialize shaderc ctx.");
        }
        
        _backend = backend;
        Options = new CompilerOptions();
        SPV.spvc_context_create(out _spvc).CheckResult();
        SPV.spvc_context_set_error_callback(_spvc, &SpirvCrossErrorCallback, 0);
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
    
    public ReflectedShaderData AttemptSpvReflect(byte[] spirvBytes) {
        try {
            spvc_parsed_ir parsedIr;
            fixed (byte* spirvPtr = spirvBytes) {
                if (spirvBytes.Length % sizeof(uint) != 0) {
                    throw new Exception("spvc bytecode length is not a multiple of 4 bytes (uint).");
                }
                nuint wordCount = (nuint)spirvBytes.Length / sizeof(uint);
                SPV.spvc_context_parse_spirv(_spvc, (uint*)spirvPtr, wordCount, out parsedIr).CheckResult();
            }

            SPV.spvc_context_create_compiler(_spvc, _backend, parsedIr, CaptureMode.TakeOwnership, out spvc_compiler compiler).CheckResult();
            SPV.spvc_compiler_create_compiler_options(compiler, out spvc_compiler_options options).CheckResult();

            if (_backend == Backend.GLSL) {
                SPV.spvc_compiler_options_set_uint(options, CompilerOption.GLSLVersion, 460).CheckResult();
                SPV.spvc_compiler_options_set_bool(options, CompilerOption.GLSLES, false).CheckResult();
                SPV.spvc_compiler_options_set_bool(options, CompilerOption.GLSLVulkanSemantics, true).CheckResult();
                SPV.spvc_compiler_options_set_bool(options, CompilerOption.GLSLEmitUniformBufferAsPlainUniforms, false).CheckResult();
            }
            else if (_backend == Backend.HLSL) {
                SPV.spvc_compiler_options_set_uint(options, CompilerOption.HLSLShaderModel, 61).CheckResult();
            }

            SPV.spvc_compiler_install_compiler_options(compiler, options).CheckResult();

            byte* codePtr;
            SPV.spvc_compiler_compile(compiler, &codePtr).CheckResult();
            string reflectedCode = Marshal.PtrToStringAnsi((IntPtr)codePtr) ?? string.Empty;

            ReflectedShaderData data = new() { Code = spirvBytes, ReflectedCode = reflectedCode };

            spvc_entry_point* entryPointsPtr;
            nuint numEntryPoints;
            SPV.spvc_compiler_get_entry_points(compiler, &entryPointsPtr, &numEntryPoints).CheckResult();
            
            if (numEntryPoints > 0) {
                data.EntryPoint = Marshal.PtrToStringAnsi((IntPtr)entryPointsPtr[0].name) ?? "unknown";
            }
            else {
                data.EntryPoint = "none";
            }
            
            SPV.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();
            
            List<ReflectedSampler> samplers = new();
            List<ReflectedUniformBuffer> buffers = new();
            List<ReflectedStorageImage> storageImages = new();
            List<ReflectedPushConstantBlock> pushConstants = new();
            List<ReflectedStorageBufferBlock> ssbos = new();

            ReadSamplers(samplers, compiler);
            data.Samplers = samplers;
            
            ReadStorageImages(storageImages, compiler);
            data.StorageImages = storageImages;
            
            ReadUniformBuffers(buffers, compiler);
            data.UniformBuffers = buffers;
            
            ReadPushConstants(pushConstants, compiler);
            data.PushConstants = pushConstants;
            
            ReadStorageBuffers(ssbos, compiler);
            data.StorageBuffers = ssbos;
            
            return data;
        }
        catch (Exception e) {
            string errorMessage = $"spv reflection failed: {e.Message}";
            var lastError = SPV.spvc_context_get_last_error_string(_spvc);
            if (!string.IsNullOrEmpty(lastError)) {
                errorMessage += $"\n spv last error: {lastError}";
            }
            throw new Exception(errorMessage, e);
        }
    }

    static void ReadStorageImages(List<ReflectedStorageImage> images, spvc_compiler compiler) {
        SPV.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();
        SPV.spvc_resources_get_resource_list_for_type(resources, ResourceType.StorageImage, out var resourceList, out var count).CheckResult();
        var newResources = new Span<spvc_reflected_resource>(resourceList, (int)count);

        foreach(var resource in newResources) {
            var name = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed";
            uint set = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);
            uint binding = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
            spvc_type typeHandle = SPV.spvc_compiler_get_type_handle(compiler, resource.type_id);
            var baseType = SPV.spvc_type_get_basetype(typeHandle);
            uint vectorSize = SPV.spvc_type_get_vector_size(typeHandle);
            uint columns = SPV.spvc_type_get_columns(typeHandle);
            ShaderDataType dataType = DataTypeExt.SpvTypeToDataType(baseType, vectorSize, columns);

            images.Add(new ReflectedStorageImage(name, binding, set, dataType));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void ReadSamplers(List<ReflectedSampler> samplers, spvc_compiler compiler) {
        SPV.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();
        SPV.spvc_resources_get_resource_list_for_type(resources, ResourceType.SampledImage, out var resourceList, out var count).CheckResult();

        var newResources = new Span<spvc_reflected_resource>(resourceList, (int)count);
        foreach (var resource in newResources) {
            var name = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed";
            uint set = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);
            uint binding = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
            var type = SPV.spvc_compiler_get_type_handle(compiler, resource.type_id);
            var baseType = SPV.spvc_type_get_basetype(type);
            
            ReflectedSampler sampler = default;
            
            switch(baseType) {
                case Basetype.SampledImage: sampler = new ReflectedSampler(name, binding, set, ShaderDataType.Sampler2D);
                    break;
            }
            
            samplers.Add(sampler);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void ReadUniformBuffers(List<ReflectedUniformBuffer> buffers, spvc_compiler compiler) {
        SPV.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();
        SPV.spvc_resources_get_resource_list_for_type(resources, ResourceType.UniformBuffer, out var resourceList, out var count).CheckResult();
        
        var newResources = new Span<spvc_reflected_resource>(resourceList, (int)count);
        foreach(var resource in newResources) {
            var name = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed";
            uint set = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);
            uint binding = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
            var type = SPV.spvc_compiler_get_type_handle(compiler, resource.type_id);
            var baseType = SPV.spvc_type_get_basetype(type);

            if(baseType == Basetype.Struct) {
                List<ReflectedBufferMember> members = new();
                uint baseTypeId = SPV.spvc_type_get_base_type_id(type);
                uint memberCount = SPV.spvc_type_get_num_member_types(type);
                
                uint structTypeId = resource.type_id;

                for(uint m = 0; m < memberCount; m++) {
                    uint memberTypeId = SPV.spvc_type_get_member_type(type, m);
                    var memberTypeHandle = SPV.spvc_compiler_get_type_handle(compiler, memberTypeId);

                    byte* mName = SPV.spvc_compiler_get_member_name(compiler, baseTypeId, m);
                    var realName = Marshal.PtrToStringUTF8((IntPtr)mName);

                    uint offset = SPV.spvc_compiler_get_member_decoration(compiler, structTypeId, m, SpvDecoration.Offset);

                    uint vectorSize = SPV.spvc_type_get_vector_size(memberTypeHandle);
                    uint columns = SPV.spvc_type_get_columns(memberTypeHandle);
                    var memberBaseType = SPV.spvc_type_get_basetype(memberTypeHandle);
                    var dataType = DataTypeExt.SpvTypeToDataType(memberBaseType, vectorSize, columns);

                    nuint memberSize;
                    SPV.spvc_compiler_get_declared_struct_member_size(compiler, type, m, &memberSize).CheckResult();

                    members.Add(new ReflectedBufferMember(realName, dataType, offset, (uint)memberSize));
                }

                nuint bufferTypeSize;
                SPV.spvc_compiler_get_declared_struct_size(compiler, type, &bufferTypeSize).CheckResult();
                uint bufferSize = (uint)bufferTypeSize;

                buffers.Add(new ReflectedUniformBuffer(name, binding, set, members, bufferSize));
            }
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void ReadPushConstants(List<ReflectedPushConstantBlock> list,  spvc_compiler compiler) {
        SPV.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();
        
        SPV.spvc_resources_get_resource_list_for_type(resources, ResourceType.PushConstant, out spvc_reflected_resource* resourceList, out nuint resourceCount);
        Span<spvc_reflected_resource> resourcesSpan = new(resourceList, (int)resourceCount);
        spvc_buffer_range** ranges = stackalloc spvc_buffer_range*[16];
        
        foreach (spvc_reflected_resource resource in resourcesSpan) {
            string propertyName = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed_push_constants_block";
            spvc_type type = SPV.spvc_compiler_get_type_handle(compiler, resource.type_id);
            Basetype baseType = SPV.spvc_type_get_basetype(type);

            if (baseType == Basetype.Struct) {
                List<ReflectedPushConstantMember> members = new();
                nuint memberCount = SPV.spvc_type_get_num_member_types(type);
                uint baseTypeId = SPV.spvc_type_get_base_type_id(type);
                uint structTypeId = resource.type_id;
                
                nuint blockSize;
                SPV.spvc_compiler_get_declared_struct_size(compiler, type, &blockSize).CheckResult();

                spvc_buffer_range* activeRangesPtr;
                nuint numActiveRanges;

                SPV.spvc_compiler_get_active_buffer_ranges(compiler, resource.id, &activeRangesPtr, &numActiveRanges).CheckResult();

                for (uint m = 0; m < memberCount; m++) {
                    uint memberTypeId = SPV.spvc_type_get_member_type(type, m);
                    spvc_type memberTypeHandle = SPV.spvc_compiler_get_type_handle(compiler, memberTypeId);

                    uint offset = SPV.spvc_compiler_get_member_decoration(compiler, resource.type_id, m, SpvDecoration.Offset);

                    uint vectorSize = SPV.spvc_type_get_vector_size(memberTypeHandle);
                    uint columns = SPV.spvc_type_get_columns(memberTypeHandle);
                    Basetype memberBaseType = SPV.spvc_type_get_basetype(memberTypeHandle);
                    ShaderDataType dataType = DataTypeExt.SpvTypeToDataType(memberBaseType, vectorSize, columns);
                    
                    var memberName = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed";

                    nuint memberSize;
                    SPV.spvc_compiler_get_declared_struct_member_size(compiler, type, m, &memberSize).CheckResult();
                    
                    byte* mName = SPV.spvc_compiler_get_member_name(compiler, baseTypeId, m);
                    var realName = Marshal.PtrToStringUTF8((IntPtr)mName);

                    members.Add(new ReflectedPushConstantMember(realName, dataType, offset, (uint)memberSize));
                }
                
                list.Add(new ReflectedPushConstantBlock(propertyName, members, (uint)blockSize));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void ReadStorageBuffers(List<ReflectedStorageBufferBlock> buffers, spvc_compiler compiler) {
        SPV.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();
        SPV.spvc_resources_get_resource_list_for_type(resources, ResourceType.StorageBuffer, out spvc_reflected_resource* resourceList, out nuint resourceCount).CheckResult();
        
        var newResources = new Span<spvc_reflected_resource>(resourceList, (int)resourceCount);
        foreach(var resource in newResources) {
            var variableName = Marshal.PtrToStringAnsi((IntPtr)resource.name) ?? "unnamed_ssbo_var";
            var structTypeName = SPV.spvc_compiler_get_name(compiler, resource.type_id) ?? "unnamed_ssbo_type";

            uint set = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);
            uint binding = SPV.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
            
            spvc_type type = SPV.spvc_compiler_get_type_handle(compiler, resource.type_id);
            var baseType = SPV.spvc_type_get_basetype(type);

            if(baseType == Basetype.Struct) {
                List<ReflectedStorageBufferMember> members = new();
                
                uint structDefinitionId = SPV.spvc_type_get_base_type_id(type);
                
                uint memberCount = SPV.spvc_type_get_num_member_types(type);

                nuint blockSize;
                SPV.spvc_compiler_get_declared_struct_size(compiler, type, &blockSize).CheckResult();

                ReflectedStorageBufferBlock.SsboFlags flags = ReflectedStorageBufferBlock.SsboFlags.ReadWrite;
                if (SPV.spvc_compiler_has_decoration(compiler, resource.id, SpvDecoration.NonReadable)) {
                    flags = ReflectedStorageBufferBlock.SsboFlags.WriteOnly;
                }
                if (SPV.spvc_compiler_has_decoration(compiler, resource.id, SpvDecoration.NonWritable)) {
                    flags = ReflectedStorageBufferBlock.SsboFlags.ReadOnly;
                }

                for(uint m = 0; m < memberCount; m++) {
                    uint memberTypeId = SPV.spvc_type_get_member_type(type, m);
                    var memberTypeHandle = SPV.spvc_compiler_get_type_handle(compiler, memberTypeId);

                    byte* memberNamePtr = SPV.spvc_compiler_get_member_name(compiler, structDefinitionId, m);
                    string memberName = Marshal.PtrToStringUTF8((IntPtr)memberNamePtr) ?? $"member_{m}";

                    uint offset = SPV.spvc_compiler_get_member_decoration(compiler, structDefinitionId, m, SpvDecoration.Offset);

                    uint vectorSize = SPV.spvc_type_get_vector_size(memberTypeHandle);
                    uint columns = SPV.spvc_type_get_columns(memberTypeHandle);
                    Basetype memberBaseType = SPV.spvc_type_get_basetype(memberTypeHandle);
                    ShaderDataType dataType = DataTypeExt.SpvTypeToDataType(memberBaseType, vectorSize, columns);

                    nuint memberSize;
                    SPV.spvc_compiler_get_declared_struct_member_size(compiler, type, m, &memberSize).CheckResult();
                    
                    uint arraySize = 1;
                    if (SPV.spvc_type_get_num_array_dimensions(memberTypeHandle) > 0) {
                        arraySize = SPV.spvc_type_get_array_dimension(memberTypeHandle, 0);
                    }

                    members.Add(new ReflectedStorageBufferMember(memberName, dataType, offset, (uint)memberSize, arraySize));
                }
                buffers.Add(new ReflectedStorageBufferBlock(variableName, structTypeName, binding, set, (uint)blockSize, flags, members));
            }
        }
    }

    public void Dispose() {
        Options.Dispose();
        if (_compilerHandle != IntPtr.Zero) {
            shaderc.shaderc_compiler_release(_compilerHandle);
            _compilerHandle = IntPtr.Zero;
        }
        if (_spvc != IntPtr.Zero) {
            SPV.spvc_context_destroy(_spvc);
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