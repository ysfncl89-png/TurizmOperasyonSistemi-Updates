# Copilot Instructions

## Proje Yönergeleri
- Preference: Use neon violet glow color #FF8B5CF6 for ComboBox styles in PersonsGeneralView (user corrected earlier pink to violet).
- Vize talebi combobox yalnızca Vize modunda görünmeli; diğer modlarda görünmemeli. Vize modunda comboboxtan seçim yapılırsa DataGrid Vize talebi seçimine göre filtrelenecek.
- MutfaGO projesi referans alınarak, TOS Lite için güncellemeler yalnızca GitHub Releases ve Velopack üzerinden yapılacak; mevcut INNO Setup entegrasyonu korunmayacak.
- Kullanıcı girişini zorunlu hale getirin; hata düzeltmeleri mevcut girişsiz kullanım modunu korumalı.

## Manifest Format
- LocalAppData Manifest must be simple JSON with fields MachineId, Version, LastModified, Hash (e.g., {"MachineId":"A-PC","Version":1,"LastModified":"2026-07-05T18:22:00","Hash":"..."})