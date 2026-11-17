# TRSLocal Windows — C#

Yerel Türkçe konuşma tanıma uygulaması. [trslocal](https://github.com/hakancuraci/trslocal) projesinin C# / .NET 8 Windows x64 uyarlaması.

- **BalbalTurkce.exe:** ses/video seç, özel isimleri belirt, dönüştür, metni düzenle ve TXT kaydet.
- **BalbalIzleyici.exe:** klasördeki yeni kayıtları sırayla işler; mevcut TXT dosyalarını korur.
- **Çevrimdışı:** yalnız EXE yanındaki `models/balbal-v2-manas` modelini kullanır. Uygulama model indirmez.
- FFmpeg ve native decoder DLL kaynak ağacında dahildir. Publish çıktısı .NET runtime'ını da içerir; Python gerekmez.

## Derleme

Windows'ta .NET 8 SDK kuruluysa:

```powershell
./scripts/publish-windows.ps1
```

Çıktı: `dist/BalbalTurkce/`. Mevcut Manas modelinizi buradaki `models/balbal-v2-manas/` klasörüne kopyalayın. Ayrıntılar [README.txt](README.txt) içinde.

## Windows testleri ve indirme

[GitHub Actions](https://github.com/hakancuraci/trslocalwin/actions/workflows/windows-validation.yml) Windows runner'ında iki uygulamayı derler; pencere açılışı/kapanışı, worker, FFmpeg ve gerçek MP3 dönüşümünü test eder. Başarılı çalışmanın `trslocal-windows-validation` artifact'i Windows paketini ve JSON test raporunu içerir. Model ağırlıkları artifact'e dahil edilmez.

Model indirmesi yalnız CI testi içindir; dağıtılan uygulama çevrimdışı kalır. Sonuç için Actions çalışmasının durumuna bakın; derlenmiş olması tüm testlerin geçtiği anlamına gelmez.

[Doğrulama kapsamı](VALIDATION.md) · [CI açıklaması](WINDOWS-CI.md)

## Lisanslar

Balbal kodu Apache-2.0; model balbal Community License 1.1 altındadır. FFmpeg ve diğer bağımlılıkların bildirimleri `portable-assets/licenses/` ile `native/licenses/` içinde korunur. Arayüz WinForms ile yazılmıştır; Tkinter ile piksel düzeyinde aynı değildir.
