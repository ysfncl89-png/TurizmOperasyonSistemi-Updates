# Görev İlerleme Detayları

## Değişiklikler
- `.github/workflows/release.yml` oluşturuldu.
- Workflow, `v*` tag push ve manuel `workflow_dispatch` ile tetikleniyor.
- .NET 8 SDK, `win-x64` self-contained publish ve TOS Lite sürüm/tag eşleşmesi eklendi.
- Velopack CLI 1.2.161 kuruluyor; önceki release indiriliyor, `TurizmOperasyonSistemi` paket kimliğiyle paketleniyor ve GitHub Releases’e yükleniyor.
- `UPDATE_REPOSITORY` Actions variable ve `UPDATE_REPO_TOKEN` Actions secret kullanılıyor; token veya yerel kullanıcı yolu workflow’a yazılmadı.
- Manuel çalıştırma için release paketi artifact olarak saklanıyor; bu yol gerçek release yayınlamıyor.

## Doğrulama
- Workflow kaynak metni; `UPDATE_REPO_TOKEN` değerinin yalnızca GitHub secret üzerinden, repository bilgisinin yalnızca Actions variable üzerinden geldiği kontrol edildi.
- `TurizmOperasyonSistemi.csproj` için önceki görevde başarılı, warning-free build alındı.
- Workflow’un gerçek GitHub runner doğrulaması, repository Actions ayarlarında `UPDATE_REPOSITORY` ve `UPDATE_REPO_TOKEN` tanımlandıktan sonra manuel çalıştırılmalıdır.

## Notlar
- Güncelleme deposu için `UPDATE_REPOSITORY` değeri `owner/repository` biçiminde ayarlanmalıdır.
- İlk kurulum Velopack tarafından üretilen `Setup.exe` ile yapılmalıdır; sonraki güncellemeler `UpdateManager` üzerinden gelir.
