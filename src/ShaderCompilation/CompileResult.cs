using System.Runtime.InteropServices;
using System.Text;

namespace ShaderCompilation;

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