# 02-github-release-pipeline: Velopack paketleme ve GitHub Releases otomasyonu

MutfaGO workflow araştırması tamamlandı. Workflow tag `v*` ve manuel çalıştırma ile tetikleniyor; Windows runner üzerinde .NET SDK kuruyor, `dotnet publish --runtime win-x64 --self-contained true` çalıştırıyor, Velopack CLI `vpk` sürümünü sabitliyor, önceki release paketlerini `vpk download github` ile alıp `vpk pack` ile delta uyumlu paket üretiyor ve `vpk upload github` ile GitHub Releases’e gönderiyor. `UPDATE_REPOSITORY` Actions variable, `UPDATE_REPO_TOKEN` Actions secret ve `GH_TOKEN` ortam değişkeni kullanılıyor; token kaynakta tutulmuyor.

TOS Lite release parametreleri:
- Proje: `TurizmOperasyonSistemi.csproj`
- TFM: `net8.0-windows`
- Runtime: `win-x64`, self-contained publish
- Paket kimliği: `TurizmOperasyonSistemi`
- Ana executable: `TurizmOperasyonSistemi.exe`
- Setup ikonu: `Assets/Icons/TurizmOperasyonSistemi.ico`
- Proje sürümü/tag biçimi: üç parçalı SemVer (`1.0.10` / `v1.0.10`).
- Workflow yerel kullanıcı yolları kullanmayacak; release deposu `UPDATE_REPOSITORY` variable üzerinden gelecek.

## Scope Inventory

- **Files to create**: `.github/workflows/release.yml`.
- **Files to preserve**: `installer/TOS Lite v1.0.0.8 iss.iss` kaynak dosyası değiştirilmeyecek; yeni workflow onu çağırmayacak.
- **Security**: `UPDATE_REPO_TOKEN` yalnızca `${{ secrets.UPDATE_REPO_TOKEN }}` ile; repository metadata `${{ vars.UPDATE_REPOSITORY }}` ile alınacak.
- **Validation**: YAML yapısal kontrolü, project build ve workflow komutlarının yerel mutlak yol/token içermediğinin incelenmesi.

**Done when**: Workflow publish/pack/release adımlarını içerir, secrets/vars ile çalışır, ilk kurulum için Velopack Setup.exe ve sonraki sürümler için release paketleri üretir.
