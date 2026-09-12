# Phase 7B — Personality System

Personality menetapkan siapa Lu-Knight; `ChatSettings.ResponseStyle` menetapkan cara jawabannya ditulis. Profil canonical berada di `Assistant/PersonalityProfile.cs`, dan `PersonalityEngine` membentuk system instruction dari identity, traits, serta interaction rules.

Alur: `AssistantController → PersonalityEngine → ChatCoordinator → GeminiChatService`. Assistant membangun personality per request. Gemini menggabungkannya dengan capability boundary serta preferensi bahasa, panjang, dan gaya respons. Pemanggilan Gemini/coordinator melalui signature lama tetap tersedia dan memakai fallback identity jika tidak menerima instruction Assistant.

Personality tidak menjadi turn dalam transcript maupun history Gemini. Clear Conversation menghapus percakapan dan konteks provider, tetapi mempertahankan personality. Local fallback tetap memakai implementasi Phase 6. Tidak ada penambahan personality pada Settings/JSON, perpindahan history Gemini, atau integrasi mood/behavior.

## Verifikasi

Regresi lengkap: **644 pemeriksaan lolos**.

```powershell
dotnet clean -c Release
dotnet build -c Release
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --assistant
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

Clean dilakukan pada konfigurasi Release karena proses Debug 7A masih aktif. Build Release berhasil tanpa warning/error. Suite Assistant mencakup 57 pemeriksaan, termasuk 22 pemeriksaan tambahan personality: profil default, semua traits/rules, payload provider, kestabilan identity lintas gaya, injection profil uji, legacy overload/fallback identity, transcript, clear, serta absennya personality dari schema konfigurasi.

Tes live terpisah dan hanya berjalan jika diminta eksplisit:

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --assistant-live
```

Verifikasi live mengirim tiga prompt umum memakai credential/model yang sudah dikonfigurasi, tanpa menyimpan perubahan preferensi. Ketiga respons memakai backend Gemini dan tetap menyebut Lu-Knight sebagai desktop companion. Friendly memakai sapaan santai; Professional memakai bahasa lebih formal dan tenang; Playful memakai sapaan hangat untuk melanjutkan kerja bersama. Respons ditinjau secara langsung, bukan diasumsikan dari payload. Tes live tidak berjalan pada suite regresi/CI biasa dan tidak menampilkan API key.
