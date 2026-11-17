using System.Buffers.Binary;
using System.Security.Cryptography;
namespace Balbal;
public static class Audio
{
 public const int SampleRate = 16000;
 public static float[] Canonical(float[] audio, int rate = SampleRate)
 {
  if(audio.Length == 0 || rate <= 0 || audio.Any(x=>!float.IsFinite(x))) throw new ArgumentException("Empty, malformed, or nonfinite audio");
  if(rate == SampleRate) return (float[])audio.Clone();
  int Gcd(int a,int b) { while(b!=0) (a,b)=(b,a%b); return a; }
  int gcd=Gcd(rate,SampleRate), up=SampleRate/gcd, down=rate/gcd, max=Math.Max(up,down), half=10*max;
  // scipy firwin(20*max+1, 1/max, window=('kaiser',5)), cast float32 then scaled by up.
  var taps=new double[2*half+1]; double sum=0, denom=Bessel(5);
  for(int j=0;j<taps.Length;j++) { double t=(j-half)/(double)max, sinc=t==0?1:Math.Sin(Math.PI*t)/(Math.PI*t);
   double z=(j-half)/(double)half; taps[j]=sinc/max*Bessel(5*Math.Sqrt(Math.Max(0,1-z*z)))/denom; sum+=taps[j]; }
  var filter=taps.Select(x=>(float)(x/sum)*up).ToArray();
  var output=new float[checked((int)(((long)audio.Length*up+down-1)/down))];
  for(int n=0;n<output.Length;n++) { long center=(long)n*down+half;
   int lo=(int)Math.Max(0,(center-(filter.Length-1)+up-1)/up), hi=(int)Math.Min(audio.Length-1,center/up);
   float value=0; for(int k=lo;k<=hi;k++) value+=audio[k]*filter[(int)(center-(long)k*up)]; output[n]=value; }
  return output;
 }
 static double Bessel(double x) { double sum=1, term=1; for(int k=1;k<100;k++) { term*=x*x/(4*k*k); sum+=term; if(term<sum*1e-16) break; } return sum; }
 public static float[] Mono(float[,] interleaved,int rate=SampleRate)
 {
  int count=interleaved.GetLength(0),channels=interleaved.GetLength(1); if(channels<1) throw new ArgumentException("No channels");
  var mono=new float[count]; for(int i=0;i<count;i++) { for(int j=0;j<channels;j++) mono[i]+=interleaved[i,j]; mono[i]/=channels; } return Canonical(mono,rate);
 }
 public static string PcmHash(float[] audio)
 {
  var x=Canonical(audio); var bytes=new byte[x.Length*2];
  for(int i=0;i<x.Length;i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i*2), (short)Math.Round(Math.Clamp(x[i],-1,32767f/32768)*32768));
  return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 }
 static float Sample(byte[] data,int offset,int width,int format,int bits)
 {
  var b=data.AsSpan(offset,width); return format==3 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b)) : bits switch {
   8 => (b[0]-128)/128f,16 => BinaryPrimitives.ReadInt16LittleEndian(b)/32768f,
   24 => ((b[0]|b[1]<<8|b[2]<<16)<<8>>8)/8388608f,_ => (float)(BinaryPrimitives.ReadInt32LittleEndian(b)/2147483648d) };
 }
 public static Task<float[]> ReadAsync(string path,CancellationToken cancellationToken=default)
  => ReadAsync(path,new AudioReadOptions(),cancellationToken);
 public static async Task<float[]> ReadAsync(string path,AudioReadOptions options,CancellationToken cancellationToken=default)
 {
  ArgumentNullException.ThrowIfNull(options);
  cancellationToken.ThrowIfCancellationRequested();
  if(options.AudioStreamIndex<0) throw new ArgumentOutOfRangeException(nameof(options.AudioStreamIndex));
  path=Path.GetFullPath(path); if(!File.Exists(path)) throw new FileNotFoundException("Audio file missing",path);
  // Decode PCM WAV at its original rate. Other containers use FFmpeg at 48 kHz, as upstream's fallback.
  if(!options.ForceFfmpeg && options.AudioStreamIndex==0)
  using(var stream=File.OpenRead(path)) using(var reader=new BinaryReader(stream)) {
   if(stream.Length>=12 && new string(reader.ReadChars(4))=="RIFF") {
    reader.ReadUInt32(); if(new string(reader.ReadChars(4))=="WAVE") {
     ushort format=0,channels=0,bits=0; int rate=0; byte[]? data=null;
     while(stream.Position+8<=stream.Length) { string id=new(reader.ReadChars(4)); uint size=reader.ReadUInt32(); long end=stream.Position+size;
      if(end>stream.Length) throw new InvalidDataException("Truncated WAV");
      if(id=="fmt " && size>=16) { format=reader.ReadUInt16(); channels=reader.ReadUInt16(); rate=reader.ReadInt32(); reader.ReadUInt32(); reader.ReadUInt16(); bits=reader.ReadUInt16(); }
      if(id=="data") data=reader.ReadBytes(checked((int)size)); stream.Position=end+(size&1); }
     if(data is not null && channels>0 && ((format==1 && bits is 8 or 16 or 24 or 32)||(format==3 && bits==32))) {
      int width=bits/8; if(data.Length%(width*channels)!=0) throw new InvalidDataException("Unaligned WAV data");
      var mono=new float[data.Length/width/channels]; for(int n=0;n<mono.Length;n++) { for(int c=0;c<channels;c++) {
       mono[n]+=Sample(data,(n*channels+c)*width,width,format,bits);
      } mono[n]/=channels; } return Canonical(mono,rate);
     }
    }
   }
  }
  return await Ffmpeg.DecodeAsync(path,options,cancellationToken);
 }
}
