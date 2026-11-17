# TRSLocal C# doğrulaması

Referans: [hakancuraci/trslocal](https://github.com/hakancuraci/trslocal), `99a46ae3fc7e8b8a6eea596a815834f08c7e47bf`.
İlk doğrulama: Linux x64, .NET SDK 8.0.428, ONNX Runtime 1.32.0.

**Gerçek Windows doğrulaması başarılı:** [GitHub Actions çalışması](https://github.com/hakancuraci/trslocalwin/actions/runs/44128184716), test edilen commit `d1da87543df74f653d39333ad26380574d588803`.

## Korunan davranış

| Özellik | C# uygulaması |
|---|---|
| İki Windows uygulaması | BalbalTurkce.exe ve BalbalIzleyici.exe |
| Yerel model | Yalnız EXE yanındaki models/balbal-v2-manas |
| Model algoritması | Aynı fast ONNX grafiği/ağırlıkları, CTC/KenLM, SentencePiece, ITN |
| Yerel custom operator | Referans dağıtımdaki balbal_ops_v2/v3/v4.dll |
| Model/FFmpeg indirme | Yok; indirici kaynak kodu bu uygulamadan çıkarıldı |
| FFmpeg | Paket içinde, PATH fallback yok |
| Ses dönüşümü | Kaynaktaki gibi 48 kHz mono PCM16, ardından aynı polyphase resampler |
| Manuel pencere | Dosya seç, özel isimler, model kontrolü, dönüştür, düzenle, UTF-8 TXT kaydet |
| Klasör izleme | Üst klasör, aynı uzantı listesi, sıralı işleme |
| Kopyalama bitişi | Dosya boyutu/zamanı 10 saniye sabit; 5 saniyede tarama |
| Mevcut çıktı | Atlanır; atomik yayında da üzerine yazılmaz |
| Değişen kaynak | İşlem sırasında değişirse sonuç yayımlanmaz, tekrar beklenir |
| Hatalı dosya | Diğer dosyalar devam eder; 300 saniye sonra yeniden deneme |
| Tek izleyici | transcript/.watcher.lock ile kilit |
| Durdurma | STOP dosyası; pencere kapatıldığında Windows Job ile worker/FFmpeg kapatılır |
| Loglar | UTF-8, islem.log 2 MB / 3 yedek, data/error.log |
| Telemetri | ORT disable_telemetry_events C# karşılığı; model hub kullanılmaz |

## Çalıştırılan kontroller

- Windows x64 masaüstü uygulamaları derlendi; .NET çalışma zamanı dahil publish üretildi.
- Windows x64 native decoder DLL'si çapraz derlendi. PE dosyasında `bb_create`, `bb_decode`, `bb_text` vb. C ABI export'ları ve OS DLL import'ları kontrol edildi.
- Klasör izleyicisi/portable runtime için **20 assertion geçti**: sıralama, tek iş, TXT koruma, atomik yayın, UTF-8, değişen kaynak, hata sonrası devam, retry cooldown, dosya sabitliği, tek izleyici kilidi, STOP, iptal, eksik model ve PATH fallback olmaması.
- Önceki Python referanslarıyla **608 assertion geçti** (sayı normalizasyonu, resampling, decoder/zamanlar/güvenler).
- FFmpeg'in 19 ses/video varyantı için **49 entegrasyon assertion'ı geçti**.
- Referans depodaki **first-test2.mp3** gerçek konuşma kaydı, aynı `balbal-v2-manas` sabit ağırlıkları (`c651a680a9147b0a7eb3fd7f5f4f914f3f4ddc25`), fast runtime ve PCM16 dönüşümüyle Python ve C# tarafında çalıştırıldı. Transkriptler **karakter karakter eşit** bulundu.

Bu tek kayıtta eşitlik genel WER/CER eşitliği iddiası değildir.

## Gerçek Windows sonucu

GitHub `windows-latest` runner'ında tüm adımlar geçti:

- 20 portable/klasör izleyicisi assertion'ı.
- 608 Python referans assertion'ı.
- 46 FFmpeg entegrasyon assertion'ı (19 ses/video varyantı; Linux'a özel üç iptal testi Windows'ta koşulmaz).
- Windows decoder DLL'si yüklenerek tokenizer/KenLM/Manas inference çalıştı.
- Her iki EXE gerçek Windows pencere handle'ı oluşturdu ve normal kapandı.
- İzleyici kapanırken worker süreci sonlandı; aktif worker varlığı ayrıca kontrol edildi.
- Gerçek MP3 metin üretti ve Python Manas referansıyla karakter karakter eşleşti.

[Windows test raporu ve paket](https://github.com/hakancuraci/trslocalwin/actions/runs/44128184716/artifacts/14349655224).

İlk çalışmada yalnız test betiğinin FFmpeg sürüm çıktısını erken kesen pipeline'ı hata verdi. Tam çıktıyı okuyacak şekilde düzeltildikten sonra bütün Windows testleri geçti.

Bu kontroller tüm düğmeleri tıklayan kapsamlı bir arayüz testi veya geniş bir WER/CER benchmark'ı değildir. WinForms arayüzü kaynak Tkinter arayüzüyle piksel eşitliği iddiası taşımaz.

## Kaynaktan test

```bash
dotnet run --project tests/TrsLocal.Tests -c Release -r linux-x64
dotnet run --project tests/Balbal.Tests -c Release -r linux-x64
dotnet run --project tests/Balbal.Tests -c Release -r linux-x64 -- --ffmpeg-tests
# Manas fast dosyaları ve yerel operatör hazırsa:
dotnet run --project tests/TrsLocal.Tests -c Release -r linux-x64 -- /yerel/portable-root ses.mp3 sonuc.txt
```
