using System.Text.Json;
using System.Text.Json.Serialization;
namespace Balbal;

public record Word(string Text, double Start, double End, double Confidence);
public record Transcript(string Text, IReadOnlyList<Word> Words, bool NoSpeech, string Model,
 double DurationSeconds, double InferenceSeconds)
{
 public double? RealtimeFactor => InferenceSeconds > 0 ? DurationSeconds / InferenceSeconds : null;
}
public sealed record DecoderSettings
{
 [JsonPropertyName("alpha")] public double Alpha { get; init; } = .75;
 [JsonPropertyName("beta")] public double Beta { get; init; } = 1.5;
 [JsonPropertyName("temperature")] public double Temperature { get; init; } = 1.2;
 [JsonPropertyName("blank_bias")] public double BlankBias { get; init; }
 [JsonPropertyName("beam")] public int Beam { get; init; } = 64;
 [JsonPropertyName("top_tokens")] public int TopTokens { get; init; } = 12;
 [JsonPropertyName("threshold")] public double Threshold { get; init; } = 4;
 [JsonPropertyName("blank_collapse")] public double? BlankCollapse { get; init; } = .99;
 [JsonPropertyName("hotword_bonus")] public double HotwordBonus { get; init; } = 1;
 [JsonPropertyName("domain_lm")] public string? DomainLm { get; init; }
 [JsonPropertyName("domain_weight")] public double DomainWeight { get; init; } = .3;
 public void Validate()
 {
  if (!double.IsFinite(Temperature) || Temperature <= 0 || !double.IsFinite(Alpha) || Alpha < 0 || !double.IsFinite(Beta)
   || !double.IsFinite(BlankBias) || Beam < 1 || TopTokens < 1 || !double.IsFinite(Threshold) || Threshold < 0
   || BlankCollapse is {} b && !(b > 0 && b < 1) || !(DomainWeight >= 0 && DomainWeight <= 1)
   || !double.IsFinite(HotwordBonus) || HotwordBonus < 0) throw new ArgumentException("Invalid decoder settings");
 }
}
public sealed record ModelInfo(string Family, string Name, string[] Languages, string? Itn, double Window, double Overlap, int FrameSamples)
{
 public static ModelInfo Load(string directory)
 {
  var path = Path.Combine(directory, "model.json");
  using var doc = JsonDocument.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}"); var r = doc.RootElement;
  string Str(string k, string fallback) => r.TryGetProperty(k, out var x) ? x.GetString() ?? fallback : fallback;
  var family = Str("model", "balbal-v2");
  var languages = r.TryGetProperty("languages", out var l) ? (l.ValueKind == JsonValueKind.String ? [l.GetString()!] : l.EnumerateArray().Select(x=>x.GetString()!).ToArray())
   : family switch { "balbal-v2-manas" => ["tr", "en"], "balbal-v2e-zellige" => ["ary"], "balbal-v2e-calliope" => ["grc"], _ => new[]{"tr"} };
  if (languages.Length == 0) throw new ArgumentException("languages cannot be empty");
  var itn = r.TryGetProperty("itn", out var i) ? (i.ValueKind == JsonValueKind.String ? i.GetString() : null) : languages[0];
  var w = r.TryGetProperty("long_windows", out var a) ? a.EnumerateArray().Select(x=>x.GetDouble()).ToArray() : [30d, 10d];
  var hop = r.TryGetProperty("frame_samples", out var h) ? h.GetInt32() : family == "balbal-v2e-zellige" ? 1280 : 640;
  if(w.Length != 2 || !double.IsFinite(w[0]) || !double.IsFinite(w[1]) || w[0]<=0 || w[1]<0 || hop<1) throw new ArgumentException("Invalid model timing");
  return new(family, Str("name",family), languages, itn, w[0], w[1], hop);
 }
}
