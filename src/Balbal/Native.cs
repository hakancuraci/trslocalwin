using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Balbal;
internal sealed class DecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
 public DecoderHandle():base(true){}
 protected override bool ReleaseHandle(){Native.bb_destroy(handle);return true;}
}
internal static class Native
{
 const string Lib="balbal_native";
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr bb_error();
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern DecoderHandle bb_create([MarshalAs(UnmanagedType.LPUTF8Str)]string tokenizer,[MarshalAs(UnmanagedType.LPUTF8Str)]string lm,[MarshalAs(UnmanagedType.LPUTF8Str)]string domain,double weight,double alpha,double beta);
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern void bb_destroy(IntPtr c);
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern int bb_vocab(DecoderHandle c);
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr bb_piece(DecoderHandle c,int id);
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr bb_text(DecoderHandle c,int[] ids,int n);
 [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)] internal static extern int bb_decode(DecoderHandle c,float[] emissions,int frames,int classes,int beam,int top,double threshold,double alpha,double beta,int[] tokens,float[] bonuses,int[] lengths,int count,int[] output,int capacity);
 internal static Exception Error()=>new InvalidOperationException(Marshal.PtrToStringUTF8(bb_error())??"Native decoder failed");
}
