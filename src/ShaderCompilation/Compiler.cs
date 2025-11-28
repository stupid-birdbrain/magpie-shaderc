using Vortice.SpirvCross;

namespace ShaderCompilation;

public struct Compiler {
    private spvc_compiler _compiler;
    
    public Compiler() {
        
    }

    public CompileResult CompileGlslToSpv() {
        return default;
    }
    
    public CompileResult CompileHlslToSpv() {
        return default;
    }
    
    public readonly ReadOnlySpan<byte> SpvToGlsl() {
        return default;
    }
    
    public readonly ReadOnlySpan<byte> SpvToHlsl() {
        return default;
    }
}