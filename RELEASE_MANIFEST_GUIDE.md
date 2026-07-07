# GitHub Release Hazırlık Notu

Bu proje, GitHub Release üzerindeki `latest.json` manifestini okur.

## Tek kural
Aşağıdaki sürüm alanları **aynı olmalı**:
- `Properties/AssemblyInfo.cs`
- GitHub Release tag
- `latest.json -> version`
- `latest.json -> minimumClientVersion`

Önerilen format: `1.0.0.1` ve `v1.0.0.1`

## Release içinde bulunması gereken dosyalar
- `latest.json`
- `TurizmOperasyonSistemi-publish.zip` veya `*.exe` / `*.msi`

## Manifest alanları
- `version` → uygulama sürümü
- `releaseType` → `app`, `migration` veya `both`
- `minimumDatabaseVersion` → migration gerekiyorsa minimum DB versiyonu
- `minimumClientVersion` → minimum client sürümü
- `mandatory` → zorunlu güncelleme mi
- `channel` → `stable`, `beta` vb.
- `releaseNotes` → kullanıcının göreceği notlar
- `downloadUrl` → indirilecek paket
- `checksum` → SHA-256 kontrol değeri

## Akış
1. `AssemblyInfo.cs` içindeki sürümü artır.
2. Aynı sürümle GitHub Release oluştur.
3. `latest.json` dosyasındaki `version` ve `downloadUrl` alanlarını aynı sürüme göre güncelle.
4. `latest.json` ve yayın paketini aynı release’e yükle.
5. Uygulama açıldığında update servisi manifesti okuyacak ve yeni sürüm varsa bildirecek.

## Örnek
- Kurulu uygulama: `1.0.0.1`
- Yeni release: `v1.0.0.1`
- `latest.json.version`: `1.0.0.1`
- `downloadUrl`: `.../v1.0.0.1/TurizmOperasyonSistemi-publish.zip`

