# 03-validation-documentation: Yeni tekil güncelleme yolunu doğrulama ve belgeleme

MutfaGO davranışına göre TOS Lite artık yalnızca Velopack/GitHub Releases kullanıyor. Eski INNO Setup scripti, latest.json template'i, manifest tabanlı update servisleri ve App.xaml.cs içindeki eski notification/download/install yolu kaldırıldı. `docs/ManualUpdate.md` ve `VERSIONING_GUIDE.md` yeni ilk kurulum, tag, secret/variable, rollback ve LocalAppData manifest kurallarını anlatıyor.

## Scope Inventory

- **Removed**: `installer/TOS Lite v1.0.0.8 iss.iss`, `latest.json.template`, `RELEASE_MANIFEST_GUIDE.md`, legacy `UpdateBackgroundService`, `UpdateCheckerService`, `UpdateDownloadService`, `UpdateInstallerService`, `UpdateModels`.
- **Updated**: `App.xaml.cs`, `Data/appsettings.json`, `Services/ConfigurationProvider.cs`, `docs/ManualUpdate.md`, `VERSIONING_GUIDE.md`.
- **Preserved**: `Views/PersonsWizardView.xaml`, `Views/PersonsGeneralView.xaml`, LocalAppData manifest fields and database migration startup.
- **Validation**: full solution build, warning review, available tests, and source search for active INNO/latest.json update paths.

## Cleanup Checklist

- [ ] Visual Studio çözümündeki tüm uyarıları giderildiğinden emin olun.
- [ ] Mevcut tüm testlerin başarıyla geçtiğini doğrulayın.
- [ ] Belgelendirme güncellemelerinin eksiksiz ve doğru olduğunu kontrol edin.
- [ ] Eski `Persons` davranışları ve manifestlerle ilgili herhangi bir regresyonun olmadığından emin olun.

**Done when**: Çözüm warning-free build olur, mevcut testler geçer, dokümantasyon yalnızca Velopack akışını anlatır ve eski Persons/manifest davranışında regresyon bulunmaz.
