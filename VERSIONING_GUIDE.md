# Sürümleme Rehberi

Bu projede tek sürüm kaynağı `Properties/AssemblyInfo.cs` dosyasındaki sürüm değeridir.

## Kullanılacak format
- Her yerde aynı formatı kullan: `1.0.0.1`
- GitHub Release etiketi: `v1.0.0.1`
- `latest.json` içindeki `version`: `1.0.0.1`
- `latest.json` içindeki `minimumClientVersion`: `1.0.0.1`

## Hızlı kural
- Visual Studio’da sürümü artıracaksan önce `Properties/AssemblyInfo.cs` dosyasını değiştir.
- Sonra aynı sayıyı GitHub Release tag olarak kullan.
- Son olarak `latest.json` içindeki `version` alanını aynı değerle güncelle.

## Örnek
- Uygulama sürümü: `1.0.0.1`
- Release tag: `v1.0.0.1`
- Sonraki sürüm: `1.0.0.1`
- Sonraki release tag: `v1.0.0.1`

## Not
- 3 parçalı (`1.0.0`) ve 4 parçalı (`1.0.0.1`) formatı karıştırma.
- Bu proje için tek format: **4 parçalı**.

