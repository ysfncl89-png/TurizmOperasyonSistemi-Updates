# Copilot Instructions

## Proje Yönergeleri
- Preference: Use neon violet glow color #FF8B5CF6 for ComboBox styles in PersonsGeneralView (user corrected earlier pink to violet).
- Vize talebi combobox yalnızca Vize modunda görünmeli; diğer modlarda görünmemeli. Vize modunda comboboxtan seçim yapılırsa DataGrid Vize talebi seçimine göre filtrelenecek.
- MutfaGO projesi referans alınarak, TOS Lite için güncellemeler yalnızca GitHub Releases ve Velopack üzerinden yapılacak; mevcut INNO Setup entegrasyonu korunmayacak.
- Kullanıcı girişini zorunlu hale getirin; hata düzeltmeleri mevcut girişsiz kullanım modunu korumalı. Kullanıcı girişi ve yerel kullanıcı yönetimi kaldırılmalı; kullanıcı Supabase altyapısına geçeceği için uygulama login olmadan açılmalı.
- Mobil uygulamadaki Kişiler modülü, masaüstü TOS Lite Kişiler modülündeki mevcut kişi kaydetme mantığını ve işleyişini (alanlar, doğrulamalar, ilişkiler ve akış) yakından takip etmeli; masaüstü davranışından bağımsız sadeleştirme yapılmamalı. Passport capture/OCR should be on-device if feasible, be reused from both Contacts and Tour Registration quick-add, and only prefill for user review before saving.
- Mobil uygulama arayüzünde SaaS kalitesinde net ve doygun renkler, belirgin kalın metinler ve profesyonel vektör ikonları tercih ediliyor; silik/düşük kontrastlı içerik istenmiyor.
- Mobil eşitleme hatası, QR okutulunca değil “Şimdi Eşitle” düğmesine basıldıktan sonra çıkmaktadır; hata analizinde QR eşleştirme ile veri aktarımı aşamaları ayrılmalı ve kullanıcının belirttiği aşama esas alınmalıdır.
- Ayarlar eşitleme davranışı: Desktop AppSettings kartlarının kayıtları mobil Ayarlar'a da aktarılmalı; mobilde eklenmiş olup masaüstünde olmayan ayarlar korunarak masaüstüne yeni ayar olarak eklenmeli. İstenen davranış iki taraflı birleştirme (union), masaüstü tek taraflı overwrite değil.
- Kişiler modülünde silme kalıcı ve ilişkili tüm kayıtları kapsamalı; mobilde silip masaüstüyle eşitleyince masaüstünde de kişi ve ona bağlı tüm kayıtlar silinmeli, masaüstünden silip eşitleyince mobilde de aynı şekilde silinmeli. Bu kural yalnızca Kişiler modülü içindir; diğer modüllerin mevcut silme/eşitleme davranışı değiştirilmemelidir.
- Kullanıcı, mobil ile masaüstü arasındaki mevcut eşitleme davranışının sorunsuz olduğunu ve gelecekteki değişikliklerde eşitleme akışının bozulmaması için sync ile ilgili düzenlemelere dokunulmamasını tercih ediyor; gerekli olduğunda önce yalnızca hedeflenen sorunu dar kapsamda çöz, sync davranışını değiştirme.
- Pasaport alanı anlamı: “Pasaport başlangıç tarihi” düzenlenme (issue/issued) tarihidir; MRZ’deki son kullanma tarihi pasaport bitiş tarihidir. Kullanıcı önizlemede pasaport bitiş tarihinin altında kalan geçerlilik süresinin gösterilmesini istiyor; kişi formunda başlangıç/bitiş tarihleri girilince görünen mevcut süre gösterimiyle tutarlı olmalı.

## Manifest Format
- LocalAppData Manifest must be simple JSON with fields MachineId, Version, LastModified, Hash (e.g., {"MachineId":"A-PC","Version":1,"LastModified":"2026-07-05T18:22:00","Hash":"..."})