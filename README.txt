TRSLOCAL — C# / .NET 8 WINDOWS x64

Bu sürüm hakancuraci/trslocal projesinin v5 taşınabilir uygulama davranışını
C# ile uygular. Python kurulumu veya Python runtime'ı gerekmez.
Referans revizyon: 99a46ae3fc7e8b8a6eea596a815834f08c7e47bf

KULLANIM
1. ZIP'i yazılabilir bir klasöre tamamen çıkarın.
2. Mevcut model klasörünüzü EXE yanına koyun:
   models/balbal-v2-manas/model.json
   models/balbal-v2-manas/onnx/model.onnx
   models/balbal-v2-manas/onnx/model.onnx.data
   models/balbal-v2-manas/lm/model.binary
   models/balbal-v2-manas/lm/tokenizer.model
   Varsa aynı modelin decoder.json dosyasını da koruyun.
   Model ağırlıkları ZIP'e dahil değildir; otomatik indirme YOKTUR.
3. BalbalTurkce.exe: dosya seç, isteğe bağlı özel isimleri virgülle yaz,
   yerel modeli kontrol et, metne dönüştür, metni düzenle ve TXT kaydet.
4. BalbalIzleyici.exe: EXE klasöründeki ses/video dosyalarını sırayla işler.
   transcript/dosya.mp3.txt üretir. Var olan TXT'leri değiştirmez.
   Yeni/değişen dosyanın 10 saniye sabit kalmasını bekler; 5 saniyede tarar.
   Hatalı dosya diğerlerini durdurmaz; 300 saniye sonra yeniden denenir.
   Alt klasörleri izlemez. Pencere kapatılırsa worker ve FFmpeg durdurulur.
   IzleyiciyiDurdur.cmd STOP dosyasıyla düzgün durdurma talebi gönderir.

FFmpeg bu WINDOWS PAKETİNE dahildir: tools/ffmpeg/ffmpeg.exe.
Sistem PATH'inden veya internetten FFmpeg/model aranmaz.
MP3/M4A/OGG/FLAC/WAV ve ses içeren video dosyaları desteklenir.
Destek kullanılan FFmpeg build'inin codec'lerine bağlıdır.

.NET 8 çalışma zamanı WINDOWS PAKETİNE dahildir; ayrıca .NET kurmak gerekmez.
Kodu derlemek için .NET 8 SDK gerekir. Windows 10/11 x64 hedeflenir.

ÇEVRİMDIŞI DAVRANIŞ
Uygulama internet istemcisi/model indiricisi içermez. Yalnızca yerel dosyalar
kullanılır. ONNX telemetri olayları devre dışı bırakılır. AppContainer, ACL,
Windows güvenlik ayarı veya güvenlik duvarı değişikliği yapılmaz. Bu, işletim
sistemi düzeyinde ağ izolasyonu iddiası değildir; kaynak proje v5 ile aynı
normal kullanıcı süreci yaklaşımıdır.

FARKLAR VE DOĞRULAMA
Pencereler WinForms ile yeniden yazılmıştır; Tkinter ile piksel düzeyinde aynı
değildir. İki pencerenin iş akışı ve Türkçe kontrolleri korunmuştur.
Gerçek Manas fast modeli ve depodaki first-test2.mp3 kullanılarak Python ile
C# transkriptlerinin birebir eşleştiği Linux üzerinde doğrulanmıştır.
Windows x64 EXE ve native decoder DLL derlenmiştir; Windows GUI'nin fiilen
çalıştırılması bu Linux ortamında doğrulanamamıştır. İlk Windows çalıştırma
kontrolleri VALIDATION.md içinde açıklanır.

GÜNLÜKLER
İşleyici: transcript/islem.log (2 MB, 3 yedek)
Hatalar: data/error.log
Var olan TXT'ler atomik ve üzerine yazmadan yayımlanır.

KAYNAKTAN DERLEME (WINDOWS)
C++ DLL hazır değilse: scripts/build-native.ps1
Tam taşınabilir paket: scripts/publish-windows.ps1
Gerekenler: .NET 8 SDK, CMake, Git; native DLL yeniden derlenecekse
Visual Studio C++ Build Tools.

LİSANS
Powered by balbal — https://github.com/erayyap/balbal
Balbal kaynak kodu Apache-2.0. Model: balbal Community License 1.1.
FFmpeg, ONNX Runtime, KenLM, SentencePiece ve Microsoft runtime bildirimleri
licenses/ ve native/licenses/ altındadır. FFmpeg GPL kaynak bilgisi:
https://github.com/imageio/imageio-ffmpeg (0.6.0 wheel, FFmpeg 7.1 Windows)
Kaynak/binary derleme bilgisi ve lisanslar dağıtımda korunmalıdır.
