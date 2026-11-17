# Windows üzerinde doğrulama

Workflow bu repoda çalıştırıldı ve **başarılı tamamlandı**: [44128184716](https://github.com/hakancuraci/trslocalwin/actions/runs/44128184716).

`main` dalına push, Windows testlerini başlatır. Actions ekranındaki Run workflow ile Manas testini açıp kapatarak manuel çalıştırma da yapılabilir. Kaynaklar bu reponun kökündedir.

Testler: Windows self-contained publish, klasör izleyicisi, Python referansları, 19 FFmpeg format varyantı, gerçek pencere açılışı/kapanışı, worker kapanışı. Manas testi sabit model revizyonunu CI içinde indirip MP3'ü işler; Python metniyle karşılaştırır. İndirme yalnız test betiğindedir; dağıtılan uygulama çevrimdışı kalır.

Paket ve JSON raporu `trslocal-windows-validation` artifact'inde tutulur. Model ağırlıkları test sonunda silinir ve artifact'e dahil edilmez. Artifacts varsayılan 7 gün saklanır.

Bu, her düğmeyi tıklayan kapsamlı bir GUI testi değildir. Test betiği geçici `dist` klasörü için tasarlanmıştır; kalıcı model klasörünüzü oraya koymayın.
