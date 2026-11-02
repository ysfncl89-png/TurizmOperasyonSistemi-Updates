# Görev İlerleme Detayları

## Değişiklikler
- Eski INNO Setup scripti `installer/TOS Lite v1.0.0.8 iss.iss` kaldırıldı.
- Eski `latest.json` tabanlı güncelleme dosyaları ve servisleri kaldırıldı: `latest.json.template`, `RELEASE_MANIFEST_GUIDE.md`, `UpdateBackgroundService`, `UpdateCheckerService`, `UpdateDownloadService`, `UpdateInstallerService`, `UpdateModels`.
- `App.xaml.cs` içindeki legacy manifest/zip/installer notification handler kaldırıldı; yalnızca başlangıçtaki Velopack kontrolü kullanılıyor.
- `Views/AboutWindow.xaml.cs`, yeni Velopack başlangıç kontrolü ve repository durumunu gösterecek şekilde güncellendi.
- `Data/appsettings.json` ve `ConfigurationProvider` yalnızca `Updates` yapılandırmasını kullanacak şekilde sadeleştirildi.
- `docs/ManualUpdate.md` ve `VERSIONING_GUIDE.md`, MutfaGO benzeri Velopack/GitHub Releases akışını anlatacak şekilde güncellendi.

## Doğrulama
- Proje derlemesi başarılı.
- Çözüm derlemesi başarılı.
- `run_tests` sonucu: 0 test bulundu; 0 başarısızlık.
- Kaynak taramasında aktif INNO Setup/legacy update çağrısı kalmadı; yalnızca talimat ve workflow planındaki tarihçe ifadeleri bulunuyor.
- `PersonsWizardView.xaml` ve `PersonsGeneralView.xaml` değiştirilmedi.
- LocalAppData manifest alanları `MachineId`, `Version`, `LastModified`, `Hash` olarak korunuyor.

## Operasyonel gereklilikler
- GitHub Actions variable: `UPDATE_REPOSITORY` (`owner/repository` biçimi).
- GitHub Actions secret: `UPDATE_REPO_TOKEN` (release yazma yetkili token).
- İlk kurulum Velopack `Setup.exe` ile yapılmalı; sonraki güncellemeler workflow release asset'lerinden otomatik gelir.
