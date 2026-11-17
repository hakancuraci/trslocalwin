# Gerçek Windows üzerinde doğrulama

Bu iş akışı henüz GitHub'a gönderilmedi ve çalıştırılmadı. `windows-latest` GitHub runner'ında çalışmak üzere hazırlanmıştır; Linux sonuçları Windows sonucu gibi raporlanmaz.

Mevcut `hakancuraci/trslocal` deposuna önerilen yerleşim:

- C# kaynak ağacı: `csharp/` altında (TrsLocal.sln, src, tests, runtimes, portable-assets, scripts).
- Workflow: depo kökündeki `.github/workflows/windows-validation.yml`.
- Önerilen ayrı dal: `csharp-windows-validation`. Bu dala push testleri başlatır; main'e otomatik birleştirme yapılmaz.

Testler: Windows self-contained publish, klasör izleyicisi testleri, Python referansları, FFmpeg format testleri, gerçek pencere handle'ı oluşturma/kapatma, worker kapanışı. Manas testi açıkken CI sabit model revizyonunu indirip depodaki MP3'ü işler ve Python referansıyla metni karşılaştırır. İndirme yalnız CI test betiğindedir; dağıtılan uygulama çevrimdışı kalır.

Betik CI'nın geçici `dist` klasörünü kullanır; test sonrası Manas dosyalarını temizler. Kendi kalıcı model klasörünüzü bu test staging klasörüne koymayın.

Rapor ve Windows paketi `trslocal-windows-validation` adlı Actions artifact'ine yüklenir. Bu test temel pencere/işlem kontrolüdür; her düğmeyi tıklayan kapsamlı bir kullanıcı arayüzü testi değildir.

Çalıştırmak için C# kaynakları ve workflow dosyası GitHub'a aktarılmalı. Sonuç ancak GitHub Actions çalışması tamamlandıktan sonra Windows'ta doğrulanmış sayılır.
